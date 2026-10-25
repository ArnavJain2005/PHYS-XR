using System;
using PHYSXR.Communication;
using PHYSXR.Game.Flow;

namespace PHYSXR.Testing
{
    // PANEL suite: the main menu's sensor panel view (SensorPanelModel). It
    // must show what the diagnostics model holds and nothing else: invalid
    // readings never read as 0, stale data is marked, link errors are shown,
    // and the non-serial modes list no readings at all.
    public static class SensorPanelModelTests
    {
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

            Run("PANEL-01_Connected_ValidReadings_ShownWithClassAndAge", P01);
            Run("PANEL-02_InvalidSonar_NoValidReading_NeverZero", P02);
            Run("PANEL-03_NeverValid_SaysSo", P03);
            Run("PANEL-04_TofFar_NothingInRange", P04);
            Run("PANEL-05_DiagnosticsOlderThan2s_Stale", P05);
            Run("PANEL-06_NoDiagYet_NoData", P06);
            Run("PANEL-07_Faulted_ShowsLinkError", P07);
            Run("PANEL-08_OldSketch_Notice", P08);
            Run("PANEL-09_ConnectedNoData_NoticeAfter3s", P09);
            Run("PANEL-10_Pins_ParsedFromHello", P10);
            Run("PANEL-11_ImuStages_AndStationError", P11);
            Run("PANEL-12_NonSerialModes_ListNoReadings", P12);
            Run("PANEL-13_EveryStateHasAWord", P13);

            Console.WriteLine("SensorPanelModelTests: " + passed + " passed, " + failed + " failed");
            return failed;
        }

        private const string Hello = "#HELLO proto=1;fw=1.0.0;dev=phys-xr-01;boot=7;baud=115200;sda=21;scl=23;trig=25;echo=26;mpu=0x68;tof=0x29";

        private static string Diag(string tof = "tof_ok=1;tof_far=0;tof_cm=41.9;tof_age=12;tof_cls=NEAR",
            string us = "us_ok=1;us_cm=43.0;us_age=40;us_cls=NEAR",
            string imu = "imu_ok=1;imu_g=1.002;imu_d=0.0040;imu_on=0.0800;imu_cal=1")
        {
            return "#DIAG ts=5000;" + tof + ";" + us + ";" + imu + ";evt=3;seq=812;loop_us=950";
        }

        private static StationDiagnostics Station(long at, string diag = null, bool hello = true)
        {
            var s = new StationDiagnostics();
            if (hello)
                s.Apply(new SerialDiagnosticLine { arrivalMs = at, text = Hello });
            if (diag != null)
                s.Apply(new SerialDiagnosticLine { arrivalMs = at, text = diag });
            return s;
        }

        private static SerialLinkStatus Link(SerialLinkState state, bool identified = true, string error = null, bool legacy = false, long since = 0)
        {
            return new SerialLinkStatus
            {
                state = state,
                port = "COM6",
                identified = identified,
                lastError = error,
                legacyFirmware = legacy,
                contractLines = 120,
                diagnosticLines = 30,
                lastLineMs = 990,
                sinceMs = since
            };
        }

        private static SensorPanelModel.Readout R(SensorPanelModel.View v, string name)
        {
            foreach (SensorPanelModel.Readout r in v.Readings)
                if (r.Name == name)
                    return r;
            throw new Exception("no readout " + name);
        }

        private static void AssertEq<T>(T expected, T actual, string context)
        {
            if (!Equals(expected, actual))
                throw new Exception(context + ": expected " + expected + " but got " + actual);
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void P01()
        {
            var v = SensorPanelModel.ForSerial(true, Link(SerialLinkState.CONNECTED), "COM6", 115200, Station(1000, Diag()), 1200);
            AssertEq("CONNECTED", v.LinkWord, "link word");
            AssertEq(SensorPanelModel.Mark.Ok, v.LinkMark, "link mark");
            AssertTrue(v.Source.Contains("COM6") && v.Source.Contains("115200"), "source: " + v.Source);
            AssertEq("phys-xr-01", v.Station[0].Value, "station id");
            AssertTrue(v.Station[1].Value.StartsWith("1.0.0") && v.Station[1].Value.Contains("protocol 1"), "firmware: " + v.Station[1].Value);
            SensorPanelModel.Readout tof = R(v, "ToF");
            AssertEq("41.9 cm", tof.Value, "tof value");
            AssertEq("VALID", tof.State, "tof state");
            AssertTrue(tof.Detail.Contains("class NEAR") && tof.Detail.Contains("age 0.2 s"), "tof detail: " + tof.Detail);
            AssertEq("43.0 cm", R(v, "Sonar").Value, "sonar value");
            AssertTrue(R(v, "IMU").Value.Contains("1.002 g"), "imu value");
            AssertEq(string.Empty, v.Message, "no message when all is well");
        }

        private static void P02()
        {
            var v = SensorPanelModel.ForSerial(true, Link(SerialLinkState.CONNECTED), "COM6", 115200,
                Station(1000, Diag(us: "us_ok=0;us_cm=0.0;us_age=4100;us_cls=NONE")), 1100);
            SensorPanelModel.Readout us = R(v, "Sonar");
            AssertEq("No valid reading", us.Value, "invalid value text");
            AssertEq("INVALID", us.State, "state");
            AssertEq(SensorPanelModel.Mark.Fault, us.Mark, "mark");
            AssertTrue(!us.Value.Contains("0"), "an invalid reading must not show a number: " + us.Value);
            AssertTrue(us.Detail.Contains("last valid 4.2 s ago"), "detail: " + us.Detail);
        }

        private static void P03()
        {
            var v = SensorPanelModel.ForSerial(true, Link(SerialLinkState.CONNECTED), "COM6", 115200,
                Station(1000, Diag(us: "us_ok=0;us_cm=0.0;us_age=-1;us_cls=NONE")), 1100);
            AssertEq("never valid since boot", R(v, "Sonar").Detail, "never valid");
        }

        private static void P04()
        {
            var v = SensorPanelModel.ForSerial(true, Link(SerialLinkState.CONNECTED), "COM6", 115200,
                Station(1000, Diag(tof: "tof_ok=1;tof_far=1;tof_cm=819.0;tof_age=10;tof_cls=NONE")), 1100);
            AssertEq("Nothing in range", R(v, "ToF").Value, "far");
            AssertEq("VALID", R(v, "ToF").State, "far is valid");
        }

        private static void P05()
        {
            var v = SensorPanelModel.ForSerial(true, Link(SerialLinkState.CONNECTED), "COM6", 115200, Station(1000, Diag()), 1000 + 2500);
            foreach (SensorPanelModel.Readout r in v.Readings)
            {
                AssertEq("STALE", r.State, r.Name + " state");
                AssertEq(SensorPanelModel.Mark.Notice, r.Mark, r.Name + " mark");
                AssertTrue(r.Detail.Contains("2.5 s ago"), r.Name + " detail: " + r.Detail);
            }
            AssertEq("CONNECTED - DATA STALE", v.LinkWord, "link word");
        }

        private static void P06()
        {
            var v = SensorPanelModel.ForSerial(true, Link(SerialLinkState.CONNECTED), "COM6", 115200, Station(1000), 1100);
            AssertEq(3, v.Readings.Count, "three readouts");
            foreach (SensorPanelModel.Readout r in v.Readings)
            {
                AssertEq("NO DATA", r.State, r.Name);
                AssertEq("No data yet", r.Value, r.Name);
            }
        }

        private static void P07()
        {
            var link = Link(SerialLinkState.FAULTED, false, "COM6 is in use by another program (close the Arduino Serial Monitor)");
            var v = SensorPanelModel.ForSerial(true, link, "COM6", 115200, new StationDiagnostics(), 5000);
            AssertEq("FAULT", v.LinkWord, "word");
            AssertEq(SensorPanelModel.Mark.Fault, v.LinkMark, "mark");
            AssertTrue(v.Message.Contains("in use"), "message: " + v.Message);
            AssertEq(SensorPanelModel.Mark.Fault, v.MessageMark, "message mark");
            AssertEq("not identified", v.Station[0].Value, "no identity");

            var off = SensorPanelModel.ForSerial(true, Link(SerialLinkState.DISCONNECTED, false, "COM13 not found"), "COM13", 115200, new StationDiagnostics(), 5000);
            AssertEq("DISCONNECTED", off.LinkWord, "disconnected word");
            AssertTrue(off.Message.Contains("not found"), "disconnected message");
        }

        private static void P08()
        {
            var v = SensorPanelModel.ForSerial(true, Link(SerialLinkState.CONNECTED, false, null, true), "COM6", 115200, new StationDiagnostics(), 5000);
            AssertEq("CONNECTED - OLD SKETCH", v.LinkWord, "word");
            AssertEq(SensorPanelModel.Mark.Notice, v.LinkMark, "mark");
            AssertTrue(v.Message.Contains("physxr_station"), "message: " + v.Message);
        }

        private static void P09()
        {
            var early = SensorPanelModel.ForSerial(true, Link(SerialLinkState.CONNECTED, false, since: 1000), "AUTO", 115200, new StationDiagnostics(), 2000);
            AssertEq(string.Empty, early.Message, "no notice in the first 3 s");
            var late = SensorPanelModel.ForSerial(true, Link(SerialLinkState.CONNECTED, false, since: 1000), "AUTO", 115200, new StationDiagnostics(), 4500);
            AssertTrue(late.Message.Contains("no PHYS-XR lines"), "notice after 3 s: " + late.Message);
            AssertTrue(late.Source.Contains("AUTO > COM6"), "auto resolved: " + late.Source);
        }

        private static void P10()
        {
            var v = SensorPanelModel.ForSerial(true, Link(SerialLinkState.CONNECTED), "COM6", 115200, Station(1000, Diag()), 1100);
            AssertEq(6, v.Pins.Count, "pin count");
            AssertEq("SCL", v.Pins[1].Name, "SCL name");
            AssertEq("23", v.Pins[1].Value, "SCL value from #HELLO");
            AssertEq("0x29", v.Pins[5].Value, "ToF address");
            var none = SensorPanelModel.ForSerial(true, Link(SerialLinkState.CONNECTED), "COM6", 115200, Station(1000, Diag(), hello: false), 1100);
            AssertEq(0, none.Pins.Count, "no pins without #HELLO");
        }

        private static void P11()
        {
            var cal = SensorPanelModel.ForSerial(true, Link(SerialLinkState.CONNECTED), "COM6", 115200,
                Station(1000, Diag(imu: "imu_ok=1;imu_g=1.0;imu_d=0.0;imu_on=0.08;imu_cal=0")), 1100);
            AssertEq("CALIBRATING", R(cal, "IMU").State, "calibrating");
            var fault = SensorPanelModel.ForSerial(true, Link(SerialLinkState.CONNECTED), "COM6", 115200,
                Station(1000, Diag(imu: "imu_ok=1;imu_g=1.6;imu_d=0.0;imu_on=0.08;imu_cal=3")), 1100);
            AssertEq(SensorPanelModel.Mark.Fault, R(fault, "IMU").Mark, "fault mark");

            StationDiagnostics s = Station(1000, Diag());
            s.Apply(new SerialDiagnosticLine { arrivalMs = 1050, text = "#ERR sensor=tof;msg=read_failed" });
            var err = SensorPanelModel.ForSerial(true, Link(SerialLinkState.CONNECTED), "COM6", 115200, s, 1100);
            AssertTrue(err.Message.Contains("read_failed"), "station error shown: " + err.Message);
            var later = SensorPanelModel.ForSerial(true, Link(SerialLinkState.CONNECTED), "COM6", 115200, s, 1050 + 31000);
            AssertTrue(!later.Message.Contains("read_failed"), "old station error no longer the message");
        }

        private static void P12()
        {
            foreach (SensorPanelModel.Mode m in new[] { SensorPanelModel.Mode.VirtualOnly, SensorPanelModel.Mode.Simulated, SensorPanelModel.Mode.Udp })
            {
                var v = SensorPanelModel.ForMode(m, 47800);
                AssertEq(0, v.Readings.Count, m + " readings");
                AssertTrue(!v.ShowsLiveReadings, m + " live readings flag");
                AssertTrue(v.Explanation.Length > 40 && v.LinkWord.Length > 0, m + " explanation");
            }
            AssertTrue(SensorPanelModel.ForMode(SensorPanelModel.Mode.Simulated, 47800).Explanation.Contains("never presented as physical"),
                "simulated mode says its data is not physical");
        }

        private static void P13()
        {
            var views = new[]
            {
                SensorPanelModel.ForSerial(false, default, "AUTO", 115200, null, 0),
                SensorPanelModel.ForSerial(true, Link(SerialLinkState.CONNECTING, false), "AUTO", 115200, new StationDiagnostics(), 100),
                SensorPanelModel.ForSerial(true, Link(SerialLinkState.CONNECTED), "COM6", 115200, Station(1000, Diag()), 1100),
                SensorPanelModel.ForSerial(true, Link(SerialLinkState.FAULTED, false, "x"), "COM6", 115200, new StationDiagnostics(), 100)
            };
            foreach (var v in views)
            {
                AssertTrue(!string.IsNullOrEmpty(v.LinkWord), "link word for " + v.LinkMark);
                foreach (SensorPanelModel.Readout r in v.Readings)
                    AssertTrue(!string.IsNullOrEmpty(r.State) && !string.IsNullOrEmpty(r.Value), r.Name + " has words");
            }
            AssertEq("STARTING", views[0].LinkWord, "not yet open");
            AssertEq("CONNECTING", views[1].LinkWord, "connecting");
        }
    }
}
