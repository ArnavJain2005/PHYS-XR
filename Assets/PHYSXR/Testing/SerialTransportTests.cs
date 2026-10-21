using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using PHYSXR.Communication;
using PHYSXR.Core.Data;
using PHYSXR.Core.Enums;
using PHYSXR.Decision;
using PHYSXR.PhysicalWorld;
using PHYSXR.WorldStateSystem;

namespace PHYSXR.Testing
{
    // USB-serial transport for the ESP32 station: framing, line routing,
    // validation of the firmware's exact output, diagnostics, the reader
    // thread's lifecycle (connect, unplug, replug, reboot, stop) and the full
    // serial -> SensorFeed -> pipeline -> WorldState -> EncounterDecisionLayer
    // path. A fake port stands in for the COM port, so these run anywhere; the
    // SER-17 checks call the real kernel32 implementation on Windows.
    //
    // They do not replace the hardware acceptance run (Docs/HARDWARE_SERIAL.md).
    public static class SerialTransportTests
    {
        // The exact strings Firmware/tests/semantics_test.cpp asserts that
        // physxr_semantics.h formatStateLine() produces.
        public const string FirmwareGoldenState =
            "v=1;dev=phys-xr-01;boot=2864130047;seq=1234;ts=987654;presence=NEAR;confidence=HIGH;"
            + "disturbance=false;eventType=;eventId=0;tEvent=0";

        public const string FirmwareGoldenEvent =
            "v=1;dev=phys-xr-01;boot=2864130047;seq=1235;ts=987702;presence=VERY_NEAR;confidence=LOW;"
            + "disturbance=true;eventType=DISTURBANCE;eventId=17;tEvent=987690";

        public static int RunAll()
        {
            int passed = 0;
            int failed = 0;

            void Run(string name, Action test)
            {
                try
                {
                    test();
                    passed++;
                    Console.WriteLine("PASS: " + name);
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine("FAIL: " + name + " - " + ex.Message);
                }
            }

            Run("SER-01_Framer_PartialMultipleCrlfAndEmptyLines", Framer);
            Run("SER-02_Framer_OversizedLineDiscardedAndCounted", FramerOversized);
            Run("SER-03_Classifier_ContractDiagLegacyNoise", Classifier);
            Run("SER-04_Validator_AcceptsFirmwareGoldenLines", GoldenLines);
            Run("SER-05_Validator_RejectsBadFieldsVersionNumbersAndSize", ValidatorRejects);
            Run("SER-06_Diagnostics_ParseAgesAndMalformed", Diagnostics);
            Run("SER-07_Receiver_MissingPort_FaultedRetriesLogsOnce", MissingPort);
            Run("SER-08_Receiver_ConnectsFramesStampsAndRoutes", ConnectAndRoute);
            Run("SER-09_Receiver_DeviceError_ReconnectsWithoutGluingLines", Unplug);
            Run("SER-10_Receiver_PortRemovedSilently_DisconnectedThenReplug", SilentRemoval);
            Run("SER-11_Receiver_PortInUse_ClearMessage", PortInUse);
            Run("SER-12_Receiver_RepeatedStartStop_NoLeakedThreadsOrHandles", Lifecycle);
            Run("SER-13_Receiver_Auto_ProbesSkipsBluetoothPicksStation", AutoProbe);
            Run("SER-14_Receiver_Auto_NoPortsOrNoStation_Reported", AutoNone);
            Run("SER-15_Receiver_UnsupportedPlatform_Faulted", Unsupported);
            Run("SER-16_Receiver_BoundedQueue_DropsOldestAndCounts", BoundedQueue);
            Run("SER-17_Win32_MissingPortAndNames", Win32Real);
            Run("SER-18a_EndToEnd_NearDisturbance_Investigate_OnePulse", E2eInvestigate);
            Run("SER-18b_EndToEnd_VeryNearHighInThreatZone_Protect", E2eProtect);
            Run("SER-18c_EndToEnd_NearLow_Warn", E2eWarn);
            Run("SER-18d_EndToEnd_DiagAndLegacyOnly_NeverChangeState", E2eDiagOnly);
            Run("SER-18e_EndToEnd_LinkLost_StaleUnavailableNeutral", E2eLinkLost);
            Run("SER-18f_EndToEnd_Reboot_ResyncBeforeEvents", E2eReboot);
            Run("SER-18g_EndToEnd_SerialAndSimulatorDecideIdentically", E2eParity);
            Run("SER-18h_EndToEnd_PhysicalFloodCannotStartAttack", E2eNoAttack);
            Run("SER-19_RawPrintable_EscapesControlBytesAndTruncates", RawPrintable);
            Run("SER-20_RawCapture_OffByDefault_NothingQueued_NoWritePath", RawCaptureOff);
            Run("SER-21_RawCapture_On_EscapedLinesSameCounters", RawCaptureOn);
            Run("SER-22_RawCapture_FloodBoundedByQueueSizeAndRate", RawCaptureFlood);

            Console.WriteLine("SerialTransportTests: " + passed + " passed, " + failed + " failed");
            return failed;
        }

        // ------------------------------------------------------------ fakes

        private sealed class FakePort
        {
            public readonly string Name;
            public readonly string Target;
            public readonly ConcurrentQueue<byte[]> Incoming = new ConcurrentQueue<byte[]>();
            public volatile bool Fail;
            public volatile bool Removed;
            public volatile bool InUse;
            public int Opens;
            public int OpenHandles;

            public FakePort(string name, string target = @"\Device\Silabser0")
            {
                Name = name;
                Target = target;
            }

            public void Send(string text)
            {
                Incoming.Enqueue(Encoding.UTF8.GetBytes(text));
            }
        }

        private sealed class FakeIO : ISerialPortIO
        {
            private readonly FakePort port;
            private bool disposed;

            public FakeIO(FakePort port)
            {
                this.port = port;
            }

            public int Read(byte[] buffer, int offset, int count)
            {
                if (disposed)
                    throw new ObjectDisposedException(port.Name);
                if (port.Fail)
                    throw new IOException(port.Name + " read failed: device not connected (1167)");
                if (!port.Removed && port.Incoming.TryDequeue(out byte[] chunk))
                {
                    int n = Math.Min(count, chunk.Length);
                    Buffer.BlockCopy(chunk, 0, buffer, offset, n);
                    if (n < chunk.Length)
                    {
                        var rest = new byte[chunk.Length - n];
                        Buffer.BlockCopy(chunk, n, rest, 0, rest.Length);
                        port.Incoming.Enqueue(rest);
                    }
                    return n;
                }
                Thread.Sleep(5);  // the real port waits up to its read timeout
                return 0;
            }

            public void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                Interlocked.Decrement(ref port.OpenHandles);
            }
        }

        private sealed class FakePorts : ISerialPortFactory
        {
            public readonly Dictionary<string, FakePort> Ports = new Dictionary<string, FakePort>();
            public bool IsSupported { get; set; } = true;
            public int OpenAttempts;

            public FakePort Add(string name, string target = @"\Device\Silabser0")
            {
                var p = new FakePort(name, target);
                lock (Ports)
                    Ports[name] = p;
                return p;
            }

            public ISerialPortIO Open(string portName, int baudRate)
            {
                Interlocked.Increment(ref OpenAttempts);
                if (!IsSupported)
                    throw new SerialOpenException("USB serial is only supported on the Windows desktop build");
                FakePort p;
                lock (Ports)
                    Ports.TryGetValue(portName, out p);
                if (p == null || p.Removed)
                    throw new SerialOpenException(portName + " not found (is the ESP32 plugged in?)");
                if (p.InUse)
                    throw new SerialOpenException(portName + " is in use by another program (close the Arduino Serial Monitor)");
                Interlocked.Increment(ref p.Opens);
                Interlocked.Increment(ref p.OpenHandles);
                return new FakeIO(p);
            }

            public IReadOnlyList<SerialPortInfo> ListPorts()
            {
                var list = new List<SerialPortInfo>();
                lock (Ports)
                    foreach (FakePort p in Ports.Values)
                        if (!p.Removed)
                            list.Add(new SerialPortInfo { Name = p.Name, Target = p.Target });
                return list;
            }

            public bool PortExists(string portName)
            {
                lock (Ports)
                    return Ports.TryGetValue(portName, out FakePort p) && !p.Removed;
            }
        }

        private static SerialLinkSettings Fast(string port = "COM5")
        {
            return new SerialLinkSettings
            {
                PortName = port,
                ReconnectDelayMs = 20,
                MaxReconnectDelayMs = 40,
                ProbeWindowMs = 150,
                SilenceCheckMs = 60
            };
        }

        private static void WaitFor(Func<bool> condition, string what, int timeoutMs = 3000)
        {
            var sw = Stopwatch.StartNew();
            while (!condition())
            {
                if (sw.ElapsedMilliseconds > timeoutMs)
                    throw new Exception("timed out waiting for " + what);
                Thread.Sleep(5);
            }
        }

        private static List<SerialLinkEvent> Events(SerialLineReceiver r)
        {
            var list = new List<SerialLinkEvent>();
            while (r.TryDequeueEvent(out SerialLinkEvent e))
                list.Add(e);
            return list;
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }

        private static void AssertEq<T>(T expected, T actual, string context)
        {
            if (!Equals(expected, actual))
                throw new Exception(context + ": expected " + expected + " but got " + actual);
        }

        private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

        // ------------------------------------------------------------ framing

        private static void Framer()
        {
            var f = new SerialLineFramer();
            var lines = new List<byte[]>();
            byte[] a = B("v=1;seq=1;ts=");
            f.Push(a, 0, a.Length, lines);
            AssertEq(0, lines.Count, "partial line held back");
            byte[] b = B("5\r\n#DIAG ts=5\n\nv=1;seq=2\n v");
            f.Push(b, 0, b.Length, lines);
            AssertEq(4, lines.Count, "three terminated lines plus one empty");
            AssertEq("v=1;seq=1;ts=5", Encoding.UTF8.GetString(lines[0]), "CR stripped, partial joined");
            AssertEq("#DIAG ts=5", Encoding.UTF8.GetString(lines[1]), "second line");
            AssertEq(0, lines[2].Length, "empty line");
            AssertEq("v=1;seq=2", Encoding.UTF8.GetString(lines[3]), "third line");
            AssertEq(2, f.PendingBytes, "tail kept");
            f.Reset();
            AssertEq(0, f.PendingBytes, "reset drops the tail");
        }

        private static void FramerOversized()
        {
            var f = new SerialLineFramer();
            var lines = new List<byte[]>();
            byte[] big = new byte[2000];
            for (int i = 0; i < big.Length; i++)
                big[i] = (byte)'x';
            f.Push(big, 0, big.Length, lines);
            AssertTrue(f.PendingBytes <= f.MaxLineBytes, "buffer bounded");
            byte[] tail = B("yyy\nok\n");
            f.Push(tail, 0, tail.Length, lines);
            AssertEq(1, lines.Count, "only the next line is delivered");
            AssertEq("ok", Encoding.UTF8.GetString(lines[0]), "next line intact");
            AssertEq(1L, f.OversizedLines, "counted once");
        }

        private static void Classifier()
        {
            AssertEq(SerialLineKind.Contract, SerialLineClassifier.Classify(B(FirmwareGoldenState)), "contract");
            AssertEq(SerialLineKind.Contract, SerialLineClassifier.Classify(B("seq=1;ts=2")), "legacy-format contract");
            AssertEq(SerialLineKind.Contract, SerialLineClassifier.Classify(B("v=1;garbage")), "malformed contract still validated");
            AssertEq(SerialLineKind.Diagnostic, SerialLineClassifier.Classify(B("#DIAG ts=1")), "diag");
            AssertEq(SerialLineKind.Legacy, SerialLineClassifier.Classify(B("Distance = 41.97 cm")), "old HC-SR04 sketch");
            AssertEq(SerialLineKind.Legacy, SerialLineClassifier.Classify(B("LASER Distance: 120 mm")), "old bench sketch");
            AssertEq(SerialLineKind.Noise, SerialLineClassifier.Classify(B("rst:0x1 (POWERON_RESET),boot:0x13 (SPI_FAST_FLASH_BOOT)")), "ROM boot text");
            AssertEq(SerialLineKind.Empty, SerialLineClassifier.Classify(B("  ")), "blank");
        }

        // ------------------------------------------------------------ validation

        private static ValidationResult V(string line, bool requireVersion = true)
        {
            return new PacketValidator("phys-xr-01") { RequireVersion = requireVersion }.Validate(B(line));
        }

        private static void GoldenLines()
        {
            var validator = new PacketValidator("phys-xr-01") { RequireVersion = true };
            ValidationResult state = validator.Validate(B(FirmwareGoldenState));
            AssertTrue(state.isValid, "golden state line: " + state.failureReason);
            AssertEq("NEAR", state.packet.presence, "presence");
            AssertEq(false, state.packet.hasEvent, "no event");
            ValidationResult evt = validator.Validate(B(FirmwareGoldenEvent));
            AssertTrue(evt.isValid, "golden event line: " + evt.failureReason);
            AssertEq(17L, evt.packet.eventId, "event id");
            AssertEq(987690L, evt.packet.eventTimestamp, "tEvent");
            AssertEq("VERY_NEAR", evt.packet.presence, "presence");
            AssertTrue(new PhysicalStateParser().TryParseState(evt.packet, out PhysicalState s) && s.confidence == ConfidenceLevel.LOW, "parses to LOW");
        }

        private static void ValidatorRejects()
        {
            string g = FirmwareGoldenState;
            void Rejected(string line, PacketRejectCode code, string why, bool requireVersion = true)
            {
                ValidationResult r = V(line, requireVersion);
                AssertTrue(!r.isValid, why + " should be rejected");
                AssertEq(code, r.code, why);
            }

            Rejected(g.Replace("v=1;", string.Empty), PacketRejectCode.VERSION_UNSUPPORTED, "missing v on the serial link");
            AssertTrue(V(g.Replace("v=1;", string.Empty), false).isValid, "missing v still allowed when not required (legacy)");
            Rejected(g.Replace("v=1", "v=2"), PacketRejectCode.VERSION_UNSUPPORTED, "v=2");
            Rejected(g.Replace("presence=NEAR", "presence=1"), PacketRejectCode.INVALID_FIELD, "numeric enum");
            Rejected(g.Replace("presence=NEAR", "presence=7"), PacketRejectCode.INVALID_FIELD, "undefined numeric enum");
            Rejected(g.Replace("presence=NEAR", "presence=near"), PacketRejectCode.INVALID_FIELD, "lower-case enum");
            Rejected(g.Replace("confidence=HIGH;", string.Empty), PacketRejectCode.INVALID_FIELD, "missing confidence");
            Rejected(g.Replace("disturbance=false", "disturbance=maybe"), PacketRejectCode.INVALID_FIELD, "bad bool");
            Rejected(g.Replace("ts=987654", "ts=NaN"), PacketRejectCode.INVALID_FIELD, "NaN ts");
            Rejected(g.Replace("ts=987654", "ts=Infinity"), PacketRejectCode.INVALID_FIELD, "Infinity ts");
            Rejected(g.Replace("ts=987654", "ts=-5"), PacketRejectCode.INVALID_FIELD, "negative ts");
            Rejected(g.Replace("ts=987654", "ts=98.7"), PacketRejectCode.INVALID_FIELD, "fractional ts");
            Rejected(g.Replace("seq=1234;", string.Empty), PacketRejectCode.INVALID_FIELD, "missing seq");
            Rejected(g.Replace("eventId=0", "eventId=abc"), PacketRejectCode.INVALID_FIELD, "non-numeric event id");
            Rejected(g.Replace("eventId=0", "eventId=-3"), PacketRejectCode.INVALID_FIELD, "negative event id");
            Rejected(g.Replace("tEvent=0", "tEvent=NaN"), PacketRejectCode.INVALID_FIELD, "NaN tEvent");
            Rejected(g.Replace("dev=phys-xr-01", "dev=other"), PacketRejectCode.FOREIGN_DEVICE, "foreign device");
            Rejected("v=1;seq", PacketRejectCode.MALFORMED, "key without value separator");
            Rejected("Distance = 41.97 cm", PacketRejectCode.VERSION_UNSUPPORTED, "raw bench line if it ever reached the validator");
            Rejected("Distance = 41.97 cm", PacketRejectCode.INVALID_FIELD, "raw bench line without the version rule", false);
            Rejected(g + ";pad=" + new string('x', 600), PacketRejectCode.BAD_SIZE, "over 512 bytes");

            var validator = new PacketValidator("phys-xr-01") { RequireVersion = true };
            ValidationResult bad = validator.Validate(new byte[] { 0x76, 0x3D, 0xFF, 0xFE, 0x3B });
            AssertEq(PacketRejectCode.MALFORMED, bad.code, "invalid UTF-8");
        }

        private static void Diagnostics()
        {
            var d = new StationDiagnostics();
            AssertEq("HELLO", d.Apply(new SerialDiagnosticLine
            {
                arrivalMs = 100,
                text = "#HELLO proto=1;fw=1.0.0;dev=phys-xr-01;boot=7;baud=115200;sda=21;scl=23;trig=25;echo=26;mpu=0x68;tof=0x29"
            }), "hello tag");
            AssertTrue(d.ProtocolSupported, "proto 1 supported");
            AssertTrue(d.PinSummary.Contains("SCL 23") && d.PinSummary.Contains("TRIG 25"), "pins reported: " + d.PinSummary);

            d.Apply(new SerialDiagnosticLine
            {
                arrivalMs = 1000,
                text = "#DIAG ts=900;tof_ok=1;tof_far=0;tof_cm=41.9;tof_age=12;tof_cls=NEAR;us_ok=0;us_cm=43.0;us_age=700;us_cls=NEAR;"
                       + "imu_ok=1;imu_g=1.002;imu_d=NaN;imu_on=0.0800;imu_cal=1;evt=3;seq=812;loop_us=950"
            });
            AssertTrue(d.Tof.valid && Math.Abs(d.Tof.value - 41.9f) < 0.01f, "tof reading");
            AssertEq(212L, d.AgeMs(d.Tof, 1200), "tof age = device age + time since arrival");
            AssertTrue(!d.Ultrasonic.valid && d.AgeMs(d.Ultrasonic, 1000) == 700, "invalid ultrasonic keeps its last age");
            AssertEq(0f, d.ImuDisturbance, "NaN parsed as 0, not propagated");
            AssertEq(1, d.ImuStage, "armed");
            AssertTrue(d.DiagnosticsFresh(2500) && !d.DiagnosticsFresh(3500), "diagnostics staleness");
            string summary = d.Summary(1200);
            AssertTrue(summary.Contains("41.9 cm") && summary.Contains("INVALID") && summary.Contains("ARMED"), "summary: " + summary);

            AssertEq(null, d.Apply(new SerialDiagnosticLine { arrivalMs = 1, text = "#DIAG nonsense" }), "malformed diag");
            AssertEq(1L, d.MalformedLines, "malformed counted");

            d.Apply(new SerialDiagnosticLine { arrivalMs = 5, text = "#HELLO proto=2;fw=9" });
            AssertTrue(!d.ProtocolSupported, "proto 2 flagged unsupported");
        }

        // ------------------------------------------------------------ receiver

        private static void MissingPort()
        {
            var ports = new FakePorts();
            var r = new SerialLineReceiver(ports, Fast("COM9"));
            r.Start();
            try
            {
                WaitFor(() => ports.OpenAttempts >= 4, "several retries");
                SerialLinkStatus s = r.Status;
                AssertEq(SerialLinkState.FAULTED, s.state, "faulted");
                AssertTrue(s.lastError.Contains("not found"), "reason: " + s.lastError);
                int faults = 0;
                foreach (SerialLinkEvent e in Events(r))
                    if (e.state == SerialLinkState.FAULTED)
                        faults++;
                AssertEq(1, faults, "the repeated identical fault is logged once");
            }
            finally
            {
                r.Stop();
            }
        }

        private static void ConnectAndRoute()
        {
            var ports = new FakePorts();
            FakePort p = ports.Add("COM5");
            long now = 5000;
            var r = new SerialLineReceiver(ports, Fast(), () => Interlocked.Read(ref now));
            r.Start();
            try
            {
                WaitFor(() => r.Status.state == SerialLinkState.CONNECTED, "connected");
                AssertTrue(!r.Status.identified, "not identified before data");
                p.Send("rst:0x1 (POWERON_RESET)\r\n#HELLO proto=1;fw=1.0.0\n" + FirmwareGoldenState.Substring(0, 30));
                WaitFor(() => r.Status.identified, "identified by #HELLO");
                Interlocked.Exchange(ref now, 5040);
                p.Send(FirmwareGoldenState.Substring(30) + "\nDistance = 41.97 cm\n");
                WaitFor(() => r.QueuedCount == 1 && r.Status.legacyLines == 1, "contract line queued");
                AssertTrue(r.TryDequeue(out UDPReceiver.Datagram d), "dequeued");
                AssertEq(FirmwareGoldenState, Encoding.UTF8.GetString(d.data), "line rebuilt across reads");
                AssertEq(5040L, d.arrivalMs, "stamped when the line completed");
                AssertTrue(r.TryDequeueDiagnostic(out SerialDiagnosticLine diag) && diag.text.StartsWith("#HELLO"), "diag routed separately");
                SerialLinkStatus s = r.Status;
                AssertEq(1L, s.noiseLines, "boot text counted as noise");
                AssertTrue(s.legacyFirmware, "legacy firmware flagged");
                AssertEq(1L, s.connects, "one connection");
            }
            finally
            {
                r.Stop();
            }
            AssertEq(0, p.OpenHandles, "port closed on stop");
            AssertEq(SerialLinkState.DISCONNECTED, r.Status.state, "disconnected after stop");
        }

        private static void Unplug()
        {
            var ports = new FakePorts();
            FakePort p = ports.Add("COM5");
            var r = new SerialLineReceiver(ports, Fast());
            r.Start();
            try
            {
                WaitFor(() => r.Status.state == SerialLinkState.CONNECTED, "connected");
                p.Send("v=1;seq=1;half-a-li");
                WaitFor(() => r.Status.bytes > 0, "partial read");
                p.Fail = true;
                WaitFor(() => r.Status.state == SerialLinkState.FAULTED, "faulted on device error");
                AssertTrue(r.Status.lastError.Contains("device error"), "reason: " + r.Status.lastError);
                // FAULTED is published in the catch; the handle closes just after.
                WaitFor(() => p.OpenHandles == 0, "failed handle closed");
                p.Fail = false;
                WaitFor(() => r.Status.state == SerialLinkState.CONNECTED && p.Opens >= 2, "reconnected");
                p.Send("ne\n" + FirmwareGoldenState + "\n");
                WaitFor(() => r.QueuedCount >= 1, "line after reconnect");
                r.TryDequeue(out UDPReceiver.Datagram d);
                AssertEq(FirmwareGoldenState, Encoding.UTF8.GetString(d.data), "first full line after reconnect, no glued fragment");
                AssertEq(0, r.QueuedCount, "fragment 'ne' was not a line");
                AssertTrue(r.Status.reconnects >= 1, "reconnect counted");
            }
            finally
            {
                r.Stop();
            }
            AssertEq(0, p.OpenHandles, "closed");
        }

        private static void SilentRemoval()
        {
            var ports = new FakePorts();
            FakePort p = ports.Add("COM5");
            var r = new SerialLineReceiver(ports, Fast());
            r.Start();
            try
            {
                WaitFor(() => r.Status.state == SerialLinkState.CONNECTED, "connected");
                p.Removed = true;
                WaitFor(() => r.Status.lastError != null && r.Status.lastError.Contains("removed"), "removal noticed");
                WaitFor(() => p.OpenHandles == 0, "handle released after removal");
                p.Removed = false;
                WaitFor(() => r.Status.state == SerialLinkState.CONNECTED && p.Opens >= 2, "replugged");
            }
            finally
            {
                r.Stop();
            }
        }

        private static void PortInUse()
        {
            var ports = new FakePorts();
            ports.Add("COM5").InUse = true;
            var r = new SerialLineReceiver(ports, Fast());
            r.Start();
            try
            {
                WaitFor(() => r.Status.state == SerialLinkState.FAULTED, "faulted");
                AssertTrue(r.Status.lastError.Contains("Serial Monitor"), "actionable message: " + r.Status.lastError);
            }
            finally
            {
                r.Stop();
            }
        }

        private static void Lifecycle()
        {
            int baseline = SerialLineReceiver.ActiveReaderThreads;
            var ports = new FakePorts();
            FakePort p = ports.Add("COM5");
            var r = new SerialLineReceiver(ports, Fast());
            for (int i = 0; i < 20; i++)
            {
                r.Start();
                r.Start();  // second Start is a no-op
                AssertTrue(SerialLineReceiver.ActiveReaderThreads <= baseline + 1, "never more than one reader");
                if (i % 2 == 0)
                    WaitFor(() => r.Status.state == SerialLinkState.CONNECTED, "connected " + i);
                var sw = Stopwatch.StartNew();
                r.Stop();
                AssertTrue(sw.ElapsedMilliseconds < 1000, "Stop returns promptly (" + sw.ElapsedMilliseconds + " ms)");
                AssertEq(baseline, SerialLineReceiver.ActiveReaderThreads, "reader ended after stop " + i);
                AssertEq(0, p.OpenHandles, "port released after stop " + i);
            }
            r.Stop();
            r.Dispose();
            AssertEq(baseline, SerialLineReceiver.ActiveReaderThreads, "no leaked readers");
        }

        private static void AutoProbe()
        {
            var ports = new FakePorts();
            FakePort silent = ports.Add("COM3");
            FakePort bt = ports.Add("COM4", @"\Device\BthModem0");
            FakePort station = ports.Add("COM7");
            silent.Send("some other device\n");
            station.Send("#HELLO proto=1;fw=1.0.0\n");
            var r = new SerialLineReceiver(ports, Fast(SerialLinkSettings.AutoPort));
            r.Start();
            try
            {
                WaitFor(() => r.Status.state == SerialLinkState.CONNECTED && r.Status.identified, "station found", 4000);
                AssertEq("COM7", r.Status.port, "picked the PHYS-XR port");
                AssertEq(0, bt.Opens, "Bluetooth port never opened");
                AssertEq(0, silent.OpenHandles, "non-station port closed after probe");
                station.Send(FirmwareGoldenState + "\n");
                WaitFor(() => r.QueuedCount == 1, "data from the found port");
            }
            finally
            {
                r.Stop();
            }
        }

        private static void AutoNone()
        {
            var ports = new FakePorts();
            var r = new SerialLineReceiver(ports, Fast(SerialLinkSettings.AutoPort));
            r.Start();
            try
            {
                WaitFor(() => r.Status.lastError != null && r.Status.lastError.Contains("no COM ports"), "no ports reported");
                AssertEq(SerialLinkState.DISCONNECTED, r.Status.state, "disconnected, not connected");
                ports.Add("COM3").Send("hello from a printer\n");
                WaitFor(() => r.Status.lastError != null && r.Status.lastError.Contains("no PHYS-XR station found"), "no station reported", 4000);
                AssertTrue(!r.Status.identified, "never claims a station");
            }
            finally
            {
                r.Stop();
            }
        }

        private static void Unsupported()
        {
            var ports = new FakePorts { IsSupported = false };
            ports.Add("COM5");
            var r = new SerialLineReceiver(ports, Fast());
            r.Start();
            try
            {
                WaitFor(() => r.Status.state == SerialLinkState.FAULTED, "faulted");
                AssertTrue(r.Status.lastError.Contains("Windows"), "reason: " + r.Status.lastError);
            }
            finally
            {
                r.Stop();
            }
        }

        private static void BoundedQueue()
        {
            var ports = new FakePorts();
            FakePort p = ports.Add("COM5");
            var r = new SerialLineReceiver(ports, Fast(), null, queueCapacity: 8);
            r.Start();
            try
            {
                var sb = new StringBuilder();
                for (int i = 1; i <= 50; i++)
                    sb.Append("v=1;seq=").Append(i).Append('\n');
                p.Send(sb.ToString());
                WaitFor(() => r.Status.contractLines == 50, "all lines read");
                AssertTrue(r.QueuedCount <= 8, "queue bounded: " + r.QueuedCount);
                AssertEq(42L, r.Status.droppedLines, "drops counted");
                r.TryDequeue(out UDPReceiver.Datagram first);
                AssertEq("v=1;seq=43", Encoding.UTF8.GetString(first.data), "oldest dropped, newest kept");
            }
            finally
            {
                r.Stop();
            }
        }

        private static void Win32Real()
        {
            AssertTrue(Win32SerialPorts.IsValidPortName("COM1") && Win32SerialPorts.IsValidPortName("com13"), "valid names");
            AssertTrue(!Win32SerialPorts.IsValidPortName("COM") && !Win32SerialPorts.IsValidPortName("COM0")
                       && !Win32SerialPorts.IsValidPortName("LPT1") && !Win32SerialPorts.IsValidPortName("COM1;rm"), "invalid names");

            var win = new Win32SerialPorts();
            if (!win.IsSupported)
            {
                Console.WriteLine("  (not Windows: kernel32 checks skipped)");
                return;
            }
            IReadOnlyList<SerialPortInfo> list = win.ListPorts();
            Console.WriteLine("  COM ports present: " + (list.Count == 0 ? "none" : string.Join(", ", list)));
            AssertTrue(!win.PortExists("COM250"), "COM250 absent");
            try
            {
                win.Open("COM250", 115200).Dispose();
                throw new Exception("opening a missing port should fail");
            }
            catch (SerialOpenException ex)
            {
                AssertTrue(ex.Message.Contains("not found"), "message: " + ex.Message);
            }

            // The real backend inside the receiver: faults cleanly, never throws.
            var r = new SerialLineReceiver(win, Fast("COM250"));
            r.Start();
            try
            {
                WaitFor(() => r.Status.state == SerialLinkState.FAULTED, "real backend faulted");
            }
            finally
            {
                r.Stop();
            }
        }

        // ------------------------------------------------------------ end to end

        // Real SerialLineReceiver (fake port) -> SensorFeed -> pipeline ->
        // gated WorldStateManager -> EncounterDecisionLayer(R1-R8), as
        // SensorService composes them for USB serial. Time is the injected
        // clock; the reader thread only stamps it.
        private sealed class SerialRig : IDisposable
        {
            private long now = 100000;
            public readonly FakePorts Ports = new FakePorts();
            public readonly FakePort Port;
            public readonly SerialLineReceiver Receiver;
            public readonly PhysicalStateManager Physical = new PhysicalStateManager();
            public readonly PhysicalCommunicationPipeline Pipeline;
            public readonly SensorFeed Feed;
            public readonly WorldStateManager World;
            public readonly EncounterDecisionLayer Layer;
            public readonly List<PacketOutcome> Log = new List<PacketOutcome>();
            public int Pulses;
            public uint Boot = 2864130047u;
            public long Seq;
            private long bootAt;

            public SerialRig()
            {
                Port = Ports.Add("COM5");
                Receiver = new SerialLineReceiver(Ports, Fast(), () => Interlocked.Read(ref now));
                Pipeline = new PhysicalCommunicationPipeline(new PacketValidator("phys-xr-01") { RequireVersion = true },
                    new PhysicalStateParser(), Physical, new PhysicalEventManager());
                Feed = new SensorFeed(Receiver, Pipeline);
                World = new WorldStateManager(Physical, () => Now);
                Layer = new EncounterDecisionLayer(new RuleBasedDecisionProvider(decisionClock: () => Now), () => Now);
                World.UpdatePlayerState(new PlayerState { distanceToGhost = 8f });
                World.UpdateVirtualState(new VirtualState { threatLevel = 0.2f });
                World.UpdateGhostState(new GhostState());
                bootAt = now - 1;
                Receiver.Start();
                WaitFor(() => Receiver.Status.state == SerialLinkState.CONNECTED, "rig connected");
            }

            public long Now => Interlocked.Read(ref now);

            // What physxr_station.ino prints: physxr::formatStateLine + '\n'.
            public string Line(string presence, string confidence = "HIGH", long eventId = 0, long tEvent = 0)
            {
                Seq++;
                bool e = eventId != 0;
                return "v=1;dev=phys-xr-01;boot=" + Boot + ";seq=" + Seq + ";ts=" + (Now - bootAt) + ";presence=" + presence
                       + ";confidence=" + confidence + ";disturbance=" + (e ? "true" : "false") + ";eventType=" + (e ? "DISTURBANCE" : "")
                       + ";eventId=" + eventId + ";tEvent=" + (e ? tEvent : 0);
            }

            public long DeviceNow => Now - bootAt;

            public void Reboot(uint boot)
            {
                Boot = boot;
                Seq = 0;
                bootAt = Now - 1;
            }

            // Writes text to the port, waits for the reader to queue the
            // expected number of contract lines, then runs one frame.
            public GhostAction Frame(string text, int contractLines, long dtMs = 100)
            {
                Interlocked.Add(ref now, dtMs);
                if (text != null)
                {
                    long before = Receiver.Status.contractLines;
                    Port.Send(text);
                    WaitFor(() => Receiver.Status.contractLines >= before + contractLines && Port.Incoming.IsEmpty, "lines read");
                    if (contractLines == 0)
                        WaitFor(() => Port.Incoming.IsEmpty, "bytes consumed");
                    Thread.Sleep(10);
                }
                foreach (PacketOutcome o in Feed.Pump(Now))
                {
                    Log.Add(o);
                    if (o.acceptedEvent != null)
                    {
                        World.NotifyAcceptedEvent(o.acceptedEvent);
                        Pulses++;
                    }
                }
                World.UpdateSourceStatus(Feed.Status);
                return Layer.Decide(World.GetCurrentWorldState());
            }

            public GhostAction State(string presence, string confidence = "HIGH")
            {
                return Frame(Line(presence, confidence) + "\n", 1);
            }

            public int Count(EventAcceptance result)
            {
                int n = 0;
                foreach (PacketOutcome o in Log)
                    if (o.eventResult == result)
                        n++;
                return n;
            }

            public void Dispose()
            {
                Receiver.Stop();
            }
        }

        private static void E2eInvestigate()
        {
            using (var rig = new SerialRig())
            {
                for (int i = 0; i < 4; i++)
                    rig.State("NEAR");
                AssertEq(PhysicalSourceStatus.FRESH, rig.Feed.Status, "fresh");

                long tEvent = rig.DeviceNow;
                GhostAction first = GhostAction.FOLLOW;
                for (int i = 0; i < 3; i++)  // the firmware repeats an event on 3 lines
                {
                    GhostAction a = rig.Frame(rig.Line("NEAR", "HIGH", 1, tEvent) + "\n", 1);
                    if (i == 0)
                        first = a;
                }
                AssertEq(GhostAction.INVESTIGATE, first, "R3 in the frame the event arrives");
                AssertTrue(rig.Layer.Inner.LastDecisionReason.StartsWith("INVESTIGATE"), "reason: " + rig.Layer.Inner.LastDecisionReason);
                AssertEq(1, rig.Count(EventAcceptance.ACCEPTED), "accepted once");
                AssertEq(2, rig.Count(EventAcceptance.DUPLICATE), "repeats deduplicated");
                AssertEq(1, rig.Pulses, "one pulse");
            }
        }

        private static void E2eProtect()
        {
            using (var rig = new SerialRig())
            {
                for (int i = 0; i < 3; i++)
                    rig.State("NONE");
                rig.World.UpdatePlayerState(new PlayerState { distanceToGhost = 3f, inThreatZone = true });
                AssertEq(GhostAction.PROTECT, rig.State("VERY_NEAR", "HIGH"), "R1 PROTECT");
                AssertTrue(rig.Layer.LastDecisionReason.StartsWith("PROTECT"), "reason: " + rig.Layer.LastDecisionReason);

                // LOW confidence (sensors disagree / one invalid) never gives R1.
                using (var low = new SerialRig())
                {
                    for (int i = 0; i < 3; i++)
                        low.State("NONE");
                    low.World.UpdatePlayerState(new PlayerState { distanceToGhost = 3f, inThreatZone = true });
                    AssertTrue(low.State("VERY_NEAR", "LOW") != GhostAction.PROTECT, "no PROTECT on LOW confidence");
                }
            }
        }

        private static void E2eWarn()
        {
            using (var rig = new SerialRig())
            {
                for (int i = 0; i < 3; i++)
                    rig.State("NONE");
                AssertEq(GhostAction.WARN, rig.State("NEAR", "LOW"), "R4 WARN");
                AssertTrue(rig.Layer.Inner.LastDecisionReason.StartsWith("WARN: physical condition"), "reason: " + rig.Layer.Inner.LastDecisionReason);
            }
        }

        private static void E2eDiagOnly()
        {
            using (var rig = new SerialRig())
            {
                for (int i = 0; i < 10; i++)
                {
                    GhostAction a = rig.Frame("#DIAG ts=1;tof_ok=1;tof_cm=5.0;tof_cls=VERY_NEAR;us_ok=1;us_cm=5.0;us_cls=VERY_NEAR;imu_ok=1;imu_d=2.0\n"
                                              + "Distance = 3.00 cm\nLASER Distance: 30 mm\n", 0);
                    AssertEq(GhostAction.FOLLOW, a, "raw readings cannot drive a decision");
                }
                AssertEq(PhysicalSourceStatus.UNAVAILABLE, rig.Feed.Status, "no state lines -> UNAVAILABLE");
                PhysicalState p = rig.World.GetCurrentWorldState().physical;
                AssertTrue(p.presence == PresenceLevel.NONE && !p.disturbance, "physical neutral");
                AssertEq(0L, rig.Pipeline.PacketsReceived, "nothing reached the validator");
                AssertTrue(rig.Receiver.Status.legacyFirmware, "legacy sketch detected");
            }
        }

        private static void E2eLinkLost()
        {
            using (var rig = new SerialRig())
            {
                for (int i = 0; i < 4; i++)
                    rig.State("NEAR", "LOW");
                AssertEq(GhostAction.WARN, rig.State("NEAR", "LOW"), "WARN while fresh");

                rig.Port.Removed = true;  // USB unplugged; the driver goes silent
                GhostAction a = GhostAction.WARN;
                PhysicalSourceStatus at600 = PhysicalSourceStatus.FRESH;
                for (int i = 0; i < 40; i++)
                {
                    a = rig.Frame(null, 0);
                    Thread.Sleep(10);  // let the reader see the advanced clock
                    if (i == 5)
                        at600 = rig.Feed.Status;
                }
                AssertTrue(rig.Receiver.Status.state != SerialLinkState.CONNECTED, "link down: " + rig.Receiver.Status.state);
                bool removalLogged = false;
                foreach (SerialLinkEvent e in Events(rig.Receiver))
                    removalLogged |= e.state == SerialLinkState.DISCONNECTED && e.detail.Contains("removed");
                AssertTrue(removalLogged, "removal reported; now: " + rig.Receiver.Status.lastError);
                AssertEq(PhysicalSourceStatus.STALE, at600, "STALE after 0.6 s");
                AssertEq(PhysicalSourceStatus.UNAVAILABLE, rig.Feed.Status, "UNAVAILABLE after 4 s");
                AssertEq(PresenceLevel.NONE, rig.World.GetCurrentWorldState().physical.presence, "neutral physical state");
                AssertEq(GhostAction.FOLLOW, a, "back to FOLLOW: nothing substitutes for the lost sensor");

                rig.Port.Removed = false;  // replugged; the ESP32 rebooted with it
                WaitFor(() => rig.Receiver.Status.state == SerialLinkState.CONNECTED, "reconnected");
                rig.Reboot(77);
                rig.State("NEAR", "LOW");
                AssertEq(PhysicalSourceStatus.FRESH, rig.Feed.Status, "fresh again after replug");
                AssertEq(1L, rig.Pipeline.Reboots, "reboot recognised from the new boot id");
            }
        }

        private static void E2eReboot()
        {
            using (var rig = new SerialRig())
            {
                for (int i = 0; i < 4; i++)
                    rig.State("NEAR");
                rig.Reboot(99);
                long t = rig.DeviceNow;
                rig.Frame(rig.Line("NEAR", "HIGH", 1, t) + "\n", 1);
                AssertEq(EventAcceptance.RESYNCING, rig.Log[rig.Log.Count - 1].eventResult, "event right after reboot waits for resync");
                rig.Frame(rig.Line("NEAR", "HIGH", 1, t) + "\n", 1);
                rig.Frame(rig.Line("NEAR", "HIGH", 1, t) + "\n", 1);
                AssertEq(1, rig.Count(EventAcceptance.ACCEPTED), "accepted after two in-order lines");
                AssertEq(1L, rig.Pipeline.Reboots, "reboot counted");
            }
        }

        // The same semantic script through the simulator path (UDP queue) and
        // the serial path must give the same decisions frame by frame.
        private static void E2eParity()
        {
            var script = new List<(string presence, string confidence, bool disturb, bool threatZone)>();
            for (int i = 0; i < 4; i++) script.Add(("NONE", "HIGH", false, false));
            for (int i = 0; i < 4; i++) script.Add(("NEAR", "LOW", false, false));
            for (int i = 0; i < 4; i++) script.Add(("NEAR", "HIGH", i == 1, false));
            for (int i = 0; i < 6; i++) script.Add(("VERY_NEAR", "HIGH", false, i >= 2));
            for (int i = 0; i < 30; i++) script.Add(("NONE", "HIGH", false, false));

            List<GhostAction> serial = new List<GhostAction>();
            using (var rig = new SerialRig())
            {
                long eventId = 0, tEvent = 0;
                int repeats = 0;
                foreach (var step in script)
                {
                    if (step.disturb)
                    {
                        eventId++;
                        tEvent = rig.DeviceNow + 100;
                        repeats = 3;
                    }
                    rig.World.UpdatePlayerState(new PlayerState { distanceToGhost = 8f, inThreatZone = step.threatZone });
                    bool carry = repeats-- > 0;
                    serial.Add(rig.Frame(rig.Line(step.presence, step.confidence, carry ? eventId : 0, carry ? tEvent : 0) + "\n", 1));
                }
            }

            List<GhostAction> simulated = new List<GhostAction>();
            long now = 100000;
            var station = new SimulatedSensorStation();
            var queue = new UDPReceiver(0, () => now);
            var physical = new PhysicalStateManager();
            var pipeline = new PhysicalCommunicationPipeline(new PacketValidator("phys-xr-01") { RequireVersion = true },
                new PhysicalStateParser(), physical, new PhysicalEventManager());
            var feed = new SensorFeed(queue, pipeline);
            var world = new WorldStateManager(physical, () => now);
            var layer = new EncounterDecisionLayer(new RuleBasedDecisionProvider(decisionClock: () => now), () => now);
            world.UpdateVirtualState(new VirtualState { threatLevel = 0.2f });
            world.UpdateGhostState(new GhostState());
            foreach (var step in script)
            {
                now += 100;
                station.SetPresence((PresenceLevel)Enum.Parse(typeof(PresenceLevel), step.presence));
                station.SetConfidence((ConfidenceLevel)Enum.Parse(typeof(ConfidenceLevel), step.confidence));
                if (step.disturb)
                    station.TriggerDisturbance(now);
                world.UpdatePlayerState(new PlayerState { distanceToGhost = 8f, inThreatZone = step.threatZone });
                foreach (byte[] d in station.Poll(now))
                    queue.Enqueue(d, now);
                station.ClearOutbox();
                foreach (PacketOutcome o in feed.Pump(now))
                    if (o.acceptedEvent != null)
                        world.NotifyAcceptedEvent(o.acceptedEvent);
                world.UpdateSourceStatus(feed.Status);
                simulated.Add(layer.Decide(world.GetCurrentWorldState()));
            }

            AssertEq(simulated.Count, serial.Count, "same length");
            for (int i = 0; i < serial.Count; i++)
                AssertEq(simulated[i], serial[i], "frame " + i + " decision");
            AssertTrue(serial.Contains(GhostAction.WARN) && serial.Contains(GhostAction.INVESTIGATE) && serial.Contains(GhostAction.PROTECT),
                "script exercised WARN, INVESTIGATE and PROTECT: " + string.Join(",", serial));
        }

        // Physical input alone never starts an attack: ATTACK needs the
        // encounter layer's own conditions (committed encounter, warning, cooldown).
        private static void E2eNoAttack()
        {
            using (var rig = new SerialRig())
            {
                rig.World.UpdatePlayerState(new PlayerState { distanceToGhost = 1.2f, inThreatZone = true });
                long id = 0;
                for (int i = 0; i < 40; i++)
                {
                    bool evt = i % 4 == 0;
                    if (evt)
                        id++;
                    GhostAction a = rig.Frame(rig.Line("VERY_NEAR", "HIGH", evt ? id : 0, evt ? rig.DeviceNow : 0) + "\n", 1);
                    AssertTrue(a != GhostAction.ATTACK, "ATTACK from sensor data at frame " + i + ": " + rig.Layer.LastDecisionReason);
                }
                AssertTrue(rig.Count(EventAcceptance.ACCEPTED) >= 5, "events were flowing");
            }
        }

        // ------------------------------------------------------------ raw monitor mode

        private static void RawPrintable()
        {
            AssertEq("Scanning...", SerialLineClassifier.Printable(B("Scanning...")), "plain text unchanged");

            byte[] hostile = { (byte)'o', (byte)'k', 0x1B, (byte)'[', (byte)'2', (byte)'J', 0x07, 0x00, (byte)'\t', 0xFF, 0x7F, (byte)'\\' };
            string escaped = SerialLineClassifier.Printable(hostile);
            AssertEq("ok\\x1B[2J\\x07\\x00\\x09\\xFF\\x7F\\x5C", escaped, "ESC, BEL, NUL, tab, non-ASCII, DEL and backslash escaped");
            foreach (char c in escaped)
                AssertTrue(c >= 0x20 && c < 0x7F, "only printable ASCII in output, found " + (int)c);

            var longLine = new byte[10000];
            for (int i = 0; i < longLine.Length; i++)
                longLine[i] = (byte)'A';
            AssertEq(new string('A', 160) + "...(+9840 bytes)", SerialLineClassifier.Printable(longLine, 160), "cut to 160 chars");

            var escapes = new byte[100];
            for (int i = 0; i < escapes.Length; i++)
                escapes[i] = 0x1B;
            AssertEq("\\x1B\\x1B...(+98 bytes)", SerialLineClassifier.Printable(escapes, 10), "an escape is never split by the limit");
        }

        private static void RawCaptureOff()
        {
            AssertTrue(!new SerialLinkSettings().CaptureRawLines, "raw capture is off by default");

            // The port interface has no way to send bytes at all.
            foreach (System.Reflection.MethodInfo m in typeof(ISerialPortIO).GetMethods())
                AssertTrue(m.Name == "Read" || m.Name == "Dispose", "unexpected port method " + m.Name);

            var ports = new FakePorts();
            FakePort p = ports.Add("COM5");
            var r = new SerialLineReceiver(ports, Fast());
            r.Start();
            try
            {
                WaitFor(() => r.Status.state == SerialLinkState.CONNECTED, "connected");
                p.Send("Scanning...\nDistance = 41.97 cm\n");
                WaitFor(() => r.Status.noiseLines == 1 && r.Status.legacyLines == 1, "lines counted");
                AssertTrue(!r.TryDequeueRaw(out _), "nothing queued when raw capture is off");
                AssertEq(0L, r.Status.rawDropped, "no raw drops");
            }
            finally
            {
                r.Stop();
            }
        }

        private static void RawCaptureOn()
        {
            var ports = new FakePorts();
            FakePort p = ports.Add("COM5");
            SerialLinkSettings settings = Fast();
            settings.CaptureRawLines = true;
            var r = new SerialLineReceiver(ports, settings);
            r.Start();
            try
            {
                WaitFor(() => r.Status.state == SerialLinkState.CONNECTED, "connected");
                p.Send("I2C device found at 0x68\r\nDistance = 41.97 cm\n#HELLO proto=1;fw=1.0.0\n"
                       + FirmwareGoldenState + "\n\u001b[2Jboom\n");
                WaitFor(() => r.Status.contractLines == 1 && r.Status.diagnosticLines == 1
                              && r.Status.noiseLines == 2 && r.Status.legacyLines == 1, "all five lines classified as without --raw");

                var raw = new List<SerialRawLine>();
                while (r.TryDequeueRaw(out SerialRawLine line))
                    raw.Add(line);
                AssertEq(3, raw.Count, "noise and old-sketch lines captured, nothing else");
                AssertEq("I2C device found at 0x68", raw[0].text, "CR stripped by the framer");
                AssertEq(SerialLineKind.Noise, raw[0].kind, "scanner text is noise");
                AssertEq("Distance = 41.97 cm", raw[1].text, "old sketch line");
                AssertEq(SerialLineKind.Legacy, raw[1].kind, "old sketch kind");
                AssertEq("\\x1B[2Jboom", raw[2].text, "terminal escape neutralised");

                AssertEq(1, r.QueuedCount, "the contract line still goes to the pipeline");
                AssertTrue(r.TryDequeueDiagnostic(out SerialDiagnosticLine d) && d.text.StartsWith("#HELLO"), "#HELLO still routed to diagnostics");
                AssertTrue(r.Status.identified, "identification unchanged");
            }
            finally
            {
                r.Stop();
            }
        }

        private static void RawCaptureFlood()
        {
            var ports = new FakePorts();
            FakePort p = ports.Add("COM5");
            SerialLinkSettings settings = Fast();
            settings.CaptureRawLines = true;
            var r = new SerialLineReceiver(ports, settings);
            r.Start();
            try
            {
                WaitFor(() => r.Status.state == SerialLinkState.CONNECTED, "connected");
                var sb = new StringBuilder();
                for (int i = 0; i < 500; i++)
                    sb.Append("noise ").Append(i).Append('\n');
                sb.Append(new string('x', 4000)).Append('\n');  // longer than any line the framer keeps
                p.Send(sb.ToString());
                WaitFor(() => r.Status.noiseLines == 500 && r.Status.oversizedLines == 1, "flood read");

                int queued = 0;
                string last = null;
                while (r.TryDequeueRaw(out SerialRawLine line))
                {
                    queued++;
                    last = line.text;
                    AssertTrue(line.text.Length <= SerialLineReceiver.RawLineMaxChars + 24, "line length bounded");
                }
                AssertEq(SerialLineReceiver.RawQueueCapacity, queued, "raw queue bounded");
                AssertEq(500L - SerialLineReceiver.RawQueueCapacity, r.Status.rawDropped, "drops counted");
                AssertEq("noise 499", last, "newest kept, oldest dropped; oversized line never captured");
            }
            finally
            {
                r.Stop();
            }

            var limiter = new SerialRawLineLimiter(20);
            int printed = 0;
            for (long t = 0; t < 1000; t++)
                if (limiter.Allow(t))
                    printed++;
            AssertEq(20, printed, "at most 20 lines printed in one second");
            AssertEq(980L, limiter.Suppressed, "the rest counted, not printed");
            AssertTrue(limiter.Allow(1000), "next second prints again");
        }
    }
}
