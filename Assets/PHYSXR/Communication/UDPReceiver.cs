using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace PHYSXR.Communication
{
    // Plain C# UDP listener - deliberately not a MonoBehaviour, since it has
    // no per-frame Unity lifecycle needs and must be testable without a
    // scene or a live ESP32 connection.
    //
    // Contains no semantic/decision logic: it only ever hands raw bytes
    // onward. Validation and parsing happen elsewhere.
    //
    // Two delivery modes:
    //  - Event mode (original): PacketReceived is raised for each datagram.
    //    From the socket loop this happens ON THE SOCKET THREAD, so handlers
    //    must be thread-safe.
    //  - Queue mode (QueueOnReceive = true, used by the game): the socket
    //    thread only stamps the arrival time (T2, from a Stopwatch-based
    //    clock) and puts the bytes on a bounded concurrent queue. The main
    //    thread drains it with TryDequeue early in each frame, so nothing
    //    downstream ever runs off the main thread. When the queue is full the
    //    oldest datagram is dropped and counted.
    //
    // Socket errors close the client and retry the bind every RebindDelayMs
    // until Stop(); gameplay never sees an exception from here.
    public class UDPReceiver
    {
        public const int DefaultQueueCapacity = 1024;
        public const int RebindDelayMs = 2000;

        public event Action<byte[]> PacketReceived;

        public struct Datagram
        {
            public byte[] data;
            public long arrivalMs;
        }

        private readonly int port;
        private readonly ConcurrentQueue<Datagram> queue = new ConcurrentQueue<Datagram>();
        private readonly int queueCapacity;
        private readonly Func<long> arrivalClock;
        private UdpClient client;
        private Thread listenThread;
        private volatile bool running;
        private long dropped;
        private long socketErrors;

        public UDPReceiver(int port)
            : this(port, null)
        {
        }

        // arrivalClock: milliseconds, monotonic, safe to call from any thread.
        // Defaults to a private Stopwatch.
        public UDPReceiver(int port, Func<long> arrivalClock, int queueCapacity = DefaultQueueCapacity)
        {
            this.port = port;
            this.queueCapacity = Math.Max(1, queueCapacity);
            if (arrivalClock == null)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                arrivalClock = () => stopwatch.ElapsedMilliseconds;
            }
            this.arrivalClock = arrivalClock;
        }

        public bool IsRunning => running;

        public int Port => port;

        // True while a socket is actually bound.
        public bool IsBound => client != null;

        public bool QueueOnReceive { get; set; }

        public long DroppedDatagrams => Interlocked.Read(ref dropped);

        public long SocketErrors => Interlocked.Read(ref socketErrors);

        public int QueuedCount => queue.Count;

        public string LastError { get; private set; }

        // Opens the socket and starts the listen thread. If the port cannot be
        // bound, the thread keeps retrying; Start() itself does not throw.
        public void Start()
        {
            if (running)
                return;

            running = true;
            listenThread = new Thread(ListenLoop) { IsBackground = true, Name = "PHYSXR UDPReceiver" };
            listenThread.Start();
        }

        public void Stop()
        {
            running = false;
            UdpClient c = client;
            client = null;
            try
            {
                c?.Close();
            }
            catch (Exception)
            {
                // Closing is best-effort.
            }
        }

        // Exposed so callers (and tests) can push bytes through the exact
        // same path the socket loop uses, without opening a real socket -
        // this is what makes the receiver testable without an ESP32.
        public void Dispatch(byte[] data)
        {
            if (QueueOnReceive)
                Enqueue(data, arrivalClock());
            else
                PacketReceived?.Invoke(data);
        }

        // Queue-mode entry with an explicit arrival time (simulator and tests).
        public void Enqueue(byte[] data, long arrivalMs)
        {
            if (data == null)
                return;

            queue.Enqueue(new Datagram { data = data, arrivalMs = arrivalMs });
            while (queue.Count > queueCapacity && queue.TryDequeue(out _))
                Interlocked.Increment(ref dropped);
        }

        public bool TryDequeue(out Datagram datagram)
        {
            return queue.TryDequeue(out datagram);
        }

        public void ClearQueue()
        {
            while (queue.TryDequeue(out _))
            {
            }
        }

        private void ListenLoop()
        {
            var remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

            while (running)
            {
                if (client == null && !TryBind())
                {
                    SleepWhileRunning(RebindDelayMs);
                    continue;
                }

                byte[] data;
                try
                {
                    data = client.Receive(ref remoteEndPoint);
                }
                catch (SocketException ex)
                {
                    if (!running)
                        break; // socket closed via Stop()
                    RecordError("receive: " + ex.SocketErrorCode);
                    CloseClient();
                    SleepWhileRunning(RebindDelayMs);
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    if (!running)
                        break;
                    CloseClient();
                    continue;
                }
                catch (NullReferenceException)
                {
                    // Stop() cleared the client between the check and Receive.
                    if (!running)
                        break;
                    continue;
                }

                Dispatch(data);
            }
        }

        private bool TryBind()
        {
            try
            {
                client = new UdpClient(port);
                LastError = null;
                return true;
            }
            catch (SocketException ex)
            {
                RecordError("bind port " + port + ": " + ex.SocketErrorCode);
                client = null;
                return false;
            }
        }

        private void CloseClient()
        {
            try
            {
                client?.Close();
            }
            catch (Exception)
            {
                // Best-effort.
            }
            client = null;
        }

        private void RecordError(string message)
        {
            Interlocked.Increment(ref socketErrors);
            LastError = message;
        }

        private void SleepWhileRunning(int milliseconds)
        {
            int slept = 0;
            while (running && slept < milliseconds)
            {
                Thread.Sleep(50);
                slept += 50;
            }
        }
    }
}
