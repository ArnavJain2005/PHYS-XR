using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using PHYSXR.Communication;
using PHYSXR.Core.Data;
using PHYSXR.Core.Enums;
using PHYSXR.PhysicalWorld;

namespace PHYSXR.TestHarness
{
    // Live check of a real ESP32 station over USB serial, using the same
    // classes the game uses (SerialLineReceiver -> SensorFeed ->
    // PacketValidator(RequireVersion) -> PhysicalCommunicationPipeline). It
    // prints link changes, '#' station lines, every state/event change and a
    // once-a-second summary, then exits.
    //
    //   dotnet run -c Release --project Tools/TestHarness -- --serial-monitor <AUTO|COMn> [seconds] [baud]
    //
    // Exit code: 0 if FRESH state lines were accepted, 1 otherwise. Close the
    // Arduino Serial Monitor (and Unity) first: only one program can hold the port.
    public static class SerialMonitor
    {
        public static int Run(string[] args)
        {
            string port = args.Length > 1 ? args[1] : SerialLinkSettings.AutoPort;
            int seconds = args.Length > 2 && int.TryParse(args[2], out int s) ? s : 30;
            int baud = args.Length > 3 && int.TryParse(args[3], out int b) ? b : SerialLinkSettings.DefaultBaudRate;

            var ports = new Win32SerialPorts();
            Console.WriteLine("COM ports: " + string.Join(", ", ports.ListPorts()));
            Console.WriteLine("Monitoring " + port + " @ " + baud + " for " + seconds + " s");

            var clock = Stopwatch.StartNew();
            Func<long> now = () => clock.ElapsedMilliseconds;
            var receiver = new SerialLineReceiver(ports, new SerialLinkSettings { PortName = port, BaudRate = baud }, now);
            var physical = new PhysicalStateManager();
            var pipeline = new PhysicalCommunicationPipeline(new PacketValidator("phys-xr-01") { RequireVersion = true },
                new PhysicalStateParser(), physical, new PhysicalEventManager());
            var feed = new SensorFeed(receiver, pipeline);
            var station = new StationDiagnostics();

            receiver.Start();
            string lastState = null;
            long nextSummary = 1000;
            long freshAccepted = 0;
            try
            {
                while (clock.ElapsedMilliseconds < seconds * 1000L)
                {
                    Thread.Sleep(50);
                    long t = now();

                    while (receiver.TryDequeueEvent(out SerialLinkEvent e))
                        Console.WriteLine(Stamp(t) + "LINK  " + e.state + " " + e.port + ": " + e.detail);
                    while (receiver.TryDequeueDiagnostic(out SerialDiagnosticLine line))
                    {
                        string tag = station.Apply(line);
                        if (tag != "DIAG")
                            Console.WriteLine(Stamp(line.arrivalMs) + "STA   " + line.text);
                    }

                    foreach (PacketOutcome o in feed.Pump(t))
                    {
                        if (!o.accepted)
                        {
                            Console.WriteLine(Stamp(o.arrivalMs) + "REJ   " + o.code + ": " + o.reason);
                            continue;
                        }
                        if (feed.Status == PhysicalSourceStatus.FRESH || pipeline.GetSourceStatus(t) == PhysicalSourceStatus.FRESH)
                            freshAccepted++;
                        if (o.isNewBoot)
                            Console.WriteLine(Stamp(o.arrivalMs) + "BOOT  new boot " + o.boot);
                        string state = o.state.presence + "/" + o.state.confidence;
                        if (state != lastState)
                        {
                            Console.WriteLine(Stamp(o.arrivalMs) + "STATE " + state + "  (seq " + o.sequence + ", boot " + o.boot + ")");
                            lastState = state;
                        }
                        if (o.eventResult != EventAcceptance.NoEvent)
                            Console.WriteLine(Stamp(o.arrivalMs) + "EVENT " + o.eventId + " " + o.eventResult);
                    }

                    if (t >= nextSummary)
                    {
                        nextSummary += 1000;
                        SerialLinkStatus st = receiver.Status;
                        Console.WriteLine(Stamp(t) + "----  " + st.state + " " + st.port + (st.identified ? " (station)" : string.Empty)
                            + "  source " + feed.Status + "  accepted " + pipeline.PacketsAccepted + " rejected " + pipeline.PacketsRejected
                            + " lost " + pipeline.PacketsLost + " noise " + (st.noiseLines + st.legacyLines) + " reconnects " + st.reconnects);
                        foreach (string l in station.Summary(t).Split('\n'))
                            if (l.Length > 0)
                                Console.WriteLine("            " + l);
                    }
                }
            }
            finally
            {
                receiver.Stop();
            }

            Console.WriteLine("Port released. FRESH state lines accepted: " + freshAccepted);
            return freshAccepted > 0 ? 0 : 1;
        }

        private static string Stamp(long ms) => (ms / 1000.0).ToString("F2", CultureInfo.InvariantCulture).PadLeft(7) + "  ";
    }
}
