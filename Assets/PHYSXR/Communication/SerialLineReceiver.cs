using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace PHYSXR.Communication
{
    public enum SerialLinkState
    {
        DISCONNECTED = 0,  // no port open (stopped, port missing, or no station found yet)
        CONNECTING,        // opening a port, or probing ports in AUTO mode
        CONNECTED,         // port open; Identified says whether PHYS-XR lines have been seen on it
        FAULTED            // the last attempt failed (in use, unsupported, read error); retrying
    }

    public sealed class SerialLinkSettings
    {
        public const string AutoPort = "AUTO";
        public const int DefaultBaudRate = 115200;  // the team's ESP32 bench prototype and this firmware

        public string PortName = AutoPort;
        public int BaudRate = DefaultBaudRate;
        public int ReconnectDelayMs = 1000;
        public int MaxReconnectDelayMs = 4000;

        // AUTO mode keeps a port only if a PHYS-XR line (contract line or
        // #HELLO) arrives within this window. The firmware sends 10 lines/s
        // and #HELLO every 5 s, so 3 s covers a board that resets on open.
        public int ProbeWindowMs = 3000;

        // With no bytes for this long, check that the port still exists, so an
        // unplugged adapter is noticed even if the driver reports no error.
        public int SilenceCheckMs = 1500;

        public bool IsAuto => string.IsNullOrWhiteSpace(PortName)
                              || string.Equals(PortName.Trim(), AutoPort, StringComparison.OrdinalIgnoreCase);

        public SerialLinkSettings Clone() => (SerialLinkSettings)MemberwiseClone();

        public bool SameLink(SerialLinkSettings other)
        {
            return other != null && BaudRate == other.BaudRate
                   && (IsAuto ? other.IsAuto : string.Equals(PortName.Trim(), other.PortName?.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public override string ToString() => (IsAuto ? AutoPort : PortName.Trim().ToUpperInvariant()) + " @ " + BaudRate;
    }

    // One state change of the link, for telemetry and the console. Only
    // changes are queued (a port that stays missing is reported once).
    public struct SerialLinkEvent
    {
        public long atMs;
        public SerialLinkState state;
        public string port;
        public string detail;
    }

    // A diagnostic ('#') line with its arrival time; display/telemetry only.
    public struct SerialDiagnosticLine
    {
        public long arrivalMs;
        public string text;
    }

    public struct SerialLinkStatus
    {
        public SerialLinkState state;
        public string port;
        public string lastError;
        public bool identified;
        public bool legacyFirmware;
        public long connects;
        public long reconnects;
        public long bytes;
        public long contractLines;
        public long diagnosticLines;
        public long legacyLines;
        public long noiseLines;
        public long oversizedLines;
        public long droppedLines;
        public long lastLineMs;  // arrival clock of the last complete line; -1 if none
        public long sinceMs;     // arrival clock when the current state began
    }

    // USB-serial transport for the ESP32 station: one background reader
    // thread owns the port, frames '\n' lines and stamps their arrival time
    // (T2) from the same clock the game uses; nothing else runs off the main
    // thread. Contract lines go on a bounded queue drained by SensorFeed
    // (IDatagramSource), exactly as UDP datagrams do, so they meet the same
    // validator, pipeline, freshness rules and decision layer. '#' lines go on
    // a separate bounded queue for the diagnostics display; they can never
    // reach PhysicalState.
    //
    // Lifecycle: Start() starts at most one reader thread; Stop() ends it and
    // closes the port within about one read timeout and waits for it. The
    // thread reconnects by itself after errors, unplug/replug and ESP32
    // resets, with a delay that grows to MaxReconnectDelayMs on repeated
    // failures. No exception escapes to the caller.
    //
    // AUTO port: candidate ports (Bluetooth ports skipped) are opened in turn
    // and kept only when PHYS-XR lines arrive; the last good port is tried
    // first. A named port is opened directly and reported CONNECTED once
    // open; Identified turns true when PHYS-XR lines arrive.
    public sealed class SerialLineReceiver : IDatagramSource, IDisposable
    {
        public const int DefaultQueueCapacity = 256;
        public const int DiagnosticQueueCapacity = 64;
        public const int EventQueueCapacity = 64;
        private const int ReadChunk = 1024;

        private static int activeReaders;

        private readonly ISerialPortFactory factory;
        private readonly SerialLinkSettings settings;
        private readonly Func<long> arrivalClock;
        private readonly int queueCapacity;
        private readonly ConcurrentQueue<UDPReceiver.Datagram> queue = new ConcurrentQueue<UDPReceiver.Datagram>();
        private readonly ConcurrentQueue<SerialDiagnosticLine> diagnostics = new ConcurrentQueue<SerialDiagnosticLine>();
        private readonly ConcurrentQueue<SerialLinkEvent> events = new ConcurrentQueue<SerialLinkEvent>();
        private readonly object statusLock = new object();
        private readonly object lifecycleLock = new object();
        private readonly SerialLineFramer framer = new SerialLineFramer();
        private readonly List<byte[]> lines = new List<byte[]>();
        private readonly ManualResetEvent wake = new ManualResetEvent(false);

        private SerialLinkStatus status;
        private string lastEventKey;
        private string lastFaultKey;
        private string lastGoodPort;
        private Thread thread;
        private Thread previousThread;
        private volatile bool running;

        public SerialLineReceiver(ISerialPortFactory factory, SerialLinkSettings settings, Func<long> arrivalClock = null,
            int queueCapacity = DefaultQueueCapacity)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.settings = (settings ?? new SerialLinkSettings()).Clone();
            this.queueCapacity = Math.Max(1, queueCapacity);
            if (arrivalClock == null)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                arrivalClock = () => stopwatch.ElapsedMilliseconds;
            }
            this.arrivalClock = arrivalClock;
            status.state = SerialLinkState.DISCONNECTED;
            status.lastLineMs = -1;
            status.port = this.settings.IsAuto ? SerialLinkSettings.AutoPort : this.settings.PortName.Trim().ToUpperInvariant();
        }

        // Reader threads alive in this process, across all receivers (tests use
        // it to prove repeated Start/Stop leaks none).
        public static int ActiveReaderThreads => Volatile.Read(ref activeReaders);

        public SerialLinkSettings Settings => settings.Clone();

        public bool IsRunning => running;

        public SerialLinkStatus Status
        {
            get
            {
                lock (statusLock)
                    return status;
            }
        }

        public int QueuedCount => queue.Count;

        public void Start()
        {
            lock (lifecycleLock)
            {
                if (running)
                    return;
                // One reader at a time: never start while a previous one that
                // outlived Stop()'s join is still inside its read.
                if (previousThread != null && previousThread.IsAlive && !previousThread.Join(3000))
                    return;
                running = true;
                wake.Reset();
                thread = new Thread(Run) { IsBackground = true, Name = "PHYSXR SerialLineReceiver" };
                thread.Start();
            }
        }

        // Ends the reader thread and releases the port. Safe to call repeatedly.
        public void Stop()
        {
            Thread t;
            lock (lifecycleLock)
            {
                running = false;
                wake.Set();
                t = thread;
                thread = null;
                if (t != null)
                    previousThread = t;
            }
            if (t != null && t != Thread.CurrentThread)
                t.Join(2000);
        }

        public void Dispose()
        {
            Stop();
        }

        public bool TryDequeue(out UDPReceiver.Datagram datagram)
        {
            return queue.TryDequeue(out datagram);
        }

        public bool TryDequeueDiagnostic(out SerialDiagnosticLine line)
        {
            return diagnostics.TryDequeue(out line);
        }

        public bool TryDequeueEvent(out SerialLinkEvent linkEvent)
        {
            return events.TryDequeue(out linkEvent);
        }

        public void ClearQueue()
        {
            while (queue.TryDequeue(out _))
            {
            }
        }

        // ------------------------------------------------------------ reader thread

        private void Run()
        {
            Interlocked.Increment(ref activeReaders);
            int delay = settings.ReconnectDelayMs;
            try
            {
                while (running)
                {
                    bool identified = settings.IsAuto ? RunAuto() : RunFixed(settings.PortName.Trim().ToUpperInvariant());
                    if (!running)
                        break;
                    delay = identified ? settings.ReconnectDelayMs : Math.Min(delay * 2, settings.MaxReconnectDelayMs);
                    Sleep(identified ? settings.ReconnectDelayMs : delay);
                }
            }
            catch (Exception ex)
            {
                // Last-resort guard: a bug here must not kill the game.
                SetState(SerialLinkState.FAULTED, Status.port, "reader stopped: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                SetState(SerialLinkState.DISCONNECTED, Status.port, "stopped");
                Interlocked.Decrement(ref activeReaders);
            }
        }

        // Returns true if the session identified a PHYS-XR station.
        private bool RunFixed(string port)
        {
            ISerialPortIO io = TryOpen(port);
            return io != null && Session(io, port, false);
        }

        private bool RunAuto()
        {
            if (!factory.IsSupported)
            {
                SetState(SerialLinkState.FAULTED, SerialLinkSettings.AutoPort, "USB serial is only supported on the Windows desktop build");
                return false;
            }

            var candidates = new List<string>();
            if (lastGoodPort != null)
                candidates.Add(lastGoodPort);
            IReadOnlyList<SerialPortInfo> ports;
            try
            {
                ports = factory.ListPorts();
            }
            catch (Exception ex)
            {
                SetState(SerialLinkState.FAULTED, SerialLinkSettings.AutoPort, "could not list COM ports: " + ex.Message);
                return false;
            }
            foreach (SerialPortInfo p in ports)
                if (!p.IsBluetooth && !candidates.Contains(p.Name))
                    candidates.Add(p.Name);

            if (candidates.Count == 0)
            {
                SetState(SerialLinkState.DISCONNECTED, SerialLinkSettings.AutoPort, ports.Count == 0
                    ? "no COM ports present (is the ESP32 plugged in?)"
                    : "no USB COM ports, only Bluetooth (" + string.Join(", ", NamesOf(ports)) + "); is the ESP32 plugged in?");
                return false;
            }

            var tried = new List<string>();
            foreach (string port in candidates)
            {
                if (!running)
                    return false;
                ISerialPortIO io = TryOpen(port);
                if (io == null)
                {
                    tried.Add(port + ": " + Status.lastError);
                    continue;
                }
                if (Session(io, port, true))
                {
                    lastGoodPort = port;
                    return true;
                }
                tried.Add(port + ": no PHYS-XR data");
            }

            if (running)
                SetState(SerialLinkState.DISCONNECTED, SerialLinkSettings.AutoPort, "no PHYS-XR station found (" + string.Join("; ", tried) + ")");
            return false;
        }

        private static List<string> NamesOf(IReadOnlyList<SerialPortInfo> ports)
        {
            var names = new List<string>();
            foreach (SerialPortInfo p in ports)
                names.Add(p.Name);
            return names;
        }

        private ISerialPortIO TryOpen(string port)
        {
            SetState(SerialLinkState.CONNECTING, port, "opening " + port + " @ " + settings.BaudRate);
            try
            {
                return factory.Open(port, settings.BaudRate);
            }
            catch (SerialOpenException ex)
            {
                SetState(SerialLinkState.FAULTED, port, ex.Message);
            }
            catch (Exception ex)
            {
                SetState(SerialLinkState.FAULTED, port, port + " could not be opened: " + ex.Message);
            }
            return null;
        }

        // Reads one open port until Stop(), a device error, the port vanishing,
        // or (probe mode) no PHYS-XR line within the probe window. Always
        // closes the port. Returns true if PHYS-XR lines were seen.
        private bool Session(ISerialPortIO io, string port, bool probe)
        {
            var chunk = new byte[ReadChunk];
            framer.Reset();
            long openedAt = arrivalClock();
            long lastByteAt = openedAt;
            long lastExistCheck = openedAt;
            bool identified = false;

            lock (statusLock)
            {
                status.identified = false;
                status.connects++;
                if (status.connects > 1)
                    status.reconnects++;
            }
            if (!probe)
                SetState(SerialLinkState.CONNECTED, port, "port open, waiting for PHYS-XR data");
            else
                SetState(SerialLinkState.CONNECTING, port, "probing " + port + " for PHYS-XR data");

            try
            {
                while (running)
                {
                    int read;
                    try
                    {
                        read = io.Read(chunk, 0, chunk.Length);
                    }
                    catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is InvalidOperationException)
                    {
                        if (running)
                            SetState(SerialLinkState.FAULTED, port, "device error: " + ex.Message);
                        return identified;
                    }

                    long now = arrivalClock();
                    if (read > 0)
                    {
                        lastByteAt = now;
                        lock (statusLock)
                            status.bytes += read;
                        lines.Clear();
                        framer.Push(chunk, 0, read, lines);
                        for (int i = 0; i < lines.Count; i++)
                        {
                            if (Route(lines[i], now) && !identified)
                            {
                                identified = true;
                                lock (statusLock)
                                    status.identified = true;
                                SetState(SerialLinkState.CONNECTED, port, "PHYS-XR station data on " + port);
                            }
                        }
                        lock (statusLock)
                            status.oversizedLines = framer.OversizedLines;
                        continue;
                    }

                    if (probe && !identified && now - openedAt >= settings.ProbeWindowMs)
                        return false;

                    if (now - lastByteAt >= settings.SilenceCheckMs && now - lastExistCheck >= settings.SilenceCheckMs)
                    {
                        lastExistCheck = now;
                        if (!factory.PortExists(port))
                        {
                            SetState(SerialLinkState.DISCONNECTED, port, port + " was removed (USB unplugged?)");
                            return identified;
                        }
                    }
                }
                return identified;
            }
            finally
            {
                try
                {
                    io.Dispose();
                }
                catch (Exception)
                {
                    // Closing is best-effort.
                }
            }
        }

        // Returns true for a line that identifies a PHYS-XR station.
        private bool Route(byte[] line, long arrivalMs)
        {
            SerialLineKind kind = SerialLineClassifier.Classify(line);
            lock (statusLock)
            {
                status.lastLineMs = arrivalMs;
                switch (kind)
                {
                    case SerialLineKind.Contract: status.contractLines++; break;
                    case SerialLineKind.Diagnostic: status.diagnosticLines++; break;
                    case SerialLineKind.Legacy: status.legacyLines++; status.legacyFirmware = true; break;
                    case SerialLineKind.Noise: status.noiseLines++; break;
                }
            }

            switch (kind)
            {
                case SerialLineKind.Contract:
                    queue.Enqueue(new UDPReceiver.Datagram { data = line, arrivalMs = arrivalMs });
                    while (queue.Count > queueCapacity && queue.TryDequeue(out _))
                    {
                        lock (statusLock)
                            status.droppedLines++;
                    }
                    return true;

                case SerialLineKind.Diagnostic:
                    string text = SerialLineClassifier.Text(line, 400);
                    diagnostics.Enqueue(new SerialDiagnosticLine { arrivalMs = arrivalMs, text = text });
                    while (diagnostics.Count > DiagnosticQueueCapacity && diagnostics.TryDequeue(out _))
                    {
                    }
                    return text.StartsWith("#HELLO", StringComparison.Ordinal) && text.IndexOf("proto=", StringComparison.Ordinal) > 0;

                default:
                    return false;
            }
        }

        private void SetState(SerialLinkState state, string port, string detail)
        {
            long now = arrivalClock();
            bool faultLike = state == SerialLinkState.FAULTED || state == SerialLinkState.DISCONNECTED;
            lock (statusLock)
            {
                if (status.state != state)
                    status.sinceMs = now;
                status.state = state;
                status.port = port;
                if (faultLike && detail != "stopped")
                    status.lastError = detail;
                else if (state == SerialLinkState.CONNECTED)
                    status.lastError = null;
            }

            // Retry loops must not flood the log: while the last outcome was a
            // fault, the retry's CONNECTING step is silent and an identical
            // fault is not repeated. Anything new (another error, a
            // connection) is reported, and CONNECTED re-arms the next fault.
            string key = state + "|" + port + "|" + detail;
            if (key == lastEventKey)
                return;
            if (state == SerialLinkState.CONNECTING && lastFaultKey != null)
                return;
            if (faultLike && detail != "stopped")
            {
                if (key == lastFaultKey)
                    return;
                lastFaultKey = key;
            }
            else if (state == SerialLinkState.CONNECTED)
            {
                lastFaultKey = null;
            }
            lastEventKey = key;
            events.Enqueue(new SerialLinkEvent { atMs = now, state = state, port = port, detail = detail });
            while (events.Count > EventQueueCapacity && events.TryDequeue(out _))
            {
            }
        }

        private void Sleep(int ms)
        {
            wake.WaitOne(Math.Max(0, ms));
        }
    }
}
