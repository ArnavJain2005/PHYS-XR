using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace PHYSXR.Communication
{
    // One open serial port, as SerialLineReceiver uses it. Read() blocks for
    // at most a short timeout and returns 0 when nothing arrived; any device
    // error (unplugged adapter, driver failure) is thrown as IOException.
    public interface ISerialPortIO : IDisposable
    {
        int Read(byte[] buffer, int offset, int count);
    }

    public struct SerialPortInfo
    {
        public string Name;    // "COM5"
        public string Target;  // driver device, e.g. "\Device\Silabser0"; empty if unknown

        // Bluetooth SPP ports can hang for seconds on open, so automatic port
        // detection skips them (they can still be selected by name).
        public bool IsBluetooth => Target != null && Target.IndexOf("BthModem", StringComparison.OrdinalIgnoreCase) >= 0;

        public override string ToString() => string.IsNullOrEmpty(Target) ? Name : Name + " (" + Target + ")";
    }

    // Opens and lists ports. Production uses Win32SerialPorts; tests use a fake.
    public interface ISerialPortFactory
    {
        bool IsSupported { get; }

        // Throws SerialOpenException with a user-readable reason.
        ISerialPortIO Open(string portName, int baudRate);

        IReadOnlyList<SerialPortInfo> ListPorts();

        bool PortExists(string portName);
    }

    public class SerialOpenException : Exception
    {
        public SerialOpenException(string message)
            : base(message)
        {
        }
    }

    // USB serial on Windows through kernel32, as the team's earlier
    // ESP32Receiver did. System.IO.Ports is not part of the project's
    // .NET Standard 2.1 API level, and this needs no player-setting change.
    //
    // The port is opened 8N1 at the requested baud with DTR and RTS held
    // de-asserted: on ESP32 dev boards those lines drive the EN/IO0
    // auto-reset circuit, so leaving them off means opening the port does
    // not hold the board in reset or bootloader mode.
    //
    // Read timeouts: return as soon as any byte is available, otherwise
    // after ReadTimeoutMs. The reader thread therefore never busy-waits and
    // notices Stop() within that time.
    //
    // On any other platform (Quest/Android) IsSupported is false and Open
    // throws, which the receiver reports as FAULTED.
    public sealed class Win32SerialPorts : ISerialPortFactory
    {
        public const int ReadTimeoutMs = 100;

        public bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public ISerialPortIO Open(string portName, int baudRate)
        {
            if (!IsSupported)
                throw new SerialOpenException("USB serial is only supported on the Windows desktop build");
            if (!IsValidPortName(portName))
                throw new SerialOpenException("'" + portName + "' is not a COM port name");
            if (baudRate <= 0)
                throw new SerialOpenException("invalid baud rate " + baudRate);

            IntPtr handle = Native.CreateFileW(@"\\.\" + portName, Native.GENERIC_READ | Native.GENERIC_WRITE, 0, IntPtr.Zero,
                Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == Native.INVALID_HANDLE_VALUE)
            {
                int error = Marshal.GetLastWin32Error();
                switch (error)
                {
                    case Native.ERROR_FILE_NOT_FOUND:
                    case Native.ERROR_PATH_NOT_FOUND:
                        throw new SerialOpenException(portName + " not found (is the ESP32 plugged in?)");
                    case Native.ERROR_ACCESS_DENIED:
                        throw new SerialOpenException(portName + " is in use by another program (close the Arduino Serial Monitor)");
                    default:
                        throw new SerialOpenException(portName + " could not be opened: " + Describe(error));
                }
            }

            try
            {
                Configure(handle, baudRate);
            }
            catch
            {
                Native.CloseHandle(handle);
                throw;
            }
            return new Port(handle, portName);
        }

        public IReadOnlyList<SerialPortInfo> ListPorts()
        {
            var ports = new List<SerialPortInfo>();
            if (!IsSupported)
                return ports;

            foreach (string name in QueryAllDosDevices())
            {
                if (IsValidPortName(name))
                    ports.Add(new SerialPortInfo { Name = name, Target = QueryTarget(name) ?? string.Empty });
            }
            ports.Sort((a, b) => PortNumber(a.Name).CompareTo(PortNumber(b.Name)));
            return ports;
        }

        public bool PortExists(string portName)
        {
            return IsSupported && IsValidPortName(portName) && QueryTarget(portName) != null;
        }

        public static bool IsValidPortName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 4 || name.Length > 6
                || !name.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                return false;
            for (int i = 3; i < name.Length; i++)
                if (name[i] < '0' || name[i] > '9')
                    return false;
            return PortNumber(name) > 0;
        }

        private static int PortNumber(string name)
        {
            return int.TryParse(name.Substring(3), out int n) ? n : int.MaxValue;
        }

        private static void Configure(IntPtr handle, int baudRate)
        {
            Native.SetupComm(handle, 4096, 1024);

            var dcb = new Native.DCB();
            dcb.DCBlength = (uint)Marshal.SizeOf(typeof(Native.DCB));
            if (!Native.GetCommState(handle, ref dcb))
                throw new SerialOpenException("GetCommState failed: " + Describe(Marshal.GetLastWin32Error()));

            dcb.BaudRate = (uint)baudRate;
            dcb.ByteSize = 8;
            dcb.Parity = 0;    // NOPARITY
            dcb.StopBits = 0;  // ONESTOPBIT
            // fBinary only: no parity check, no flow control, DTR/RTS disabled,
            // no abort-on-error (so a framing glitch does not stall reads).
            dcb.Flags = Native.DCB_FBINARY;
            if (!Native.SetCommState(handle, ref dcb))
                throw new SerialOpenException("baud " + baudRate + " rejected by the driver: " + Describe(Marshal.GetLastWin32Error()));

            var timeouts = new Native.COMMTIMEOUTS
            {
                ReadIntervalTimeout = uint.MaxValue,
                ReadTotalTimeoutMultiplier = uint.MaxValue,
                ReadTotalTimeoutConstant = ReadTimeoutMs,
                WriteTotalTimeoutMultiplier = 0,
                WriteTotalTimeoutConstant = 200
            };
            if (!Native.SetCommTimeouts(handle, ref timeouts))
                throw new SerialOpenException("SetCommTimeouts failed: " + Describe(Marshal.GetLastWin32Error()));

            // Drop whatever the driver buffered before we opened (old lines).
            Native.PurgeComm(handle, Native.PURGE_RXCLEAR | Native.PURGE_RXABORT);
        }

        private static string Describe(int error)
        {
            return new Win32Exception(error).Message + " (" + error + ")";
        }

        private static List<string> QueryAllDosDevices()
        {
            var names = new List<string>();
            int size = 16384;
            for (int attempt = 0; attempt < 6; attempt++, size *= 2)
            {
                var buffer = new char[size];
                int count = Native.QueryDosDeviceW(null, buffer, buffer.Length);
                if (count == 0)
                {
                    if (Marshal.GetLastWin32Error() == Native.ERROR_INSUFFICIENT_BUFFER)
                        continue;
                    return names;
                }

                int start = 0;
                for (int i = 0; i < count; i++)
                {
                    if (buffer[i] != '\0')
                        continue;
                    if (i > start)
                        names.Add(new string(buffer, start, i - start));
                    start = i + 1;
                }
                return names;
            }
            return names;
        }

        private static string QueryTarget(string name)
        {
            var buffer = new char[1024];
            int count = Native.QueryDosDeviceW(name, buffer, buffer.Length);
            if (count == 0)
                return null;
            int end = Array.IndexOf(buffer, '\0');
            return new string(buffer, 0, end < 0 ? count : end);
        }

        private sealed class Port : ISerialPortIO
        {
            private IntPtr handle;
            private readonly string name;

            public Port(IntPtr handle, string name)
            {
                this.handle = handle;
                this.name = name;
            }

            public int Read(byte[] buffer, int offset, int count)
            {
                if (handle == IntPtr.Zero)
                    throw new ObjectDisposedException(name);
                if (offset < 0 || count < 0 || offset + count > buffer.Length)
                    throw new ArgumentOutOfRangeException(nameof(count));

                // ReadFile has no offset parameter; read into a pinned slice.
                GCHandle pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                try
                {
                    IntPtr target = IntPtr.Add(pin.AddrOfPinnedObject(), offset);
                    if (!Native.ReadFile(handle, target, (uint)count, out uint read, IntPtr.Zero))
                    {
                        int error = Marshal.GetLastWin32Error();
                        throw new IOException(name + " read failed: " + Describe(error));
                    }
                    return (int)read;
                }
                finally
                {
                    pin.Free();
                }
            }

            public void Dispose()
            {
                IntPtr h = handle;
                handle = IntPtr.Zero;
                if (h != IntPtr.Zero)
                    Native.CloseHandle(h);
            }
        }

        private static class Native
        {
            public const uint GENERIC_READ = 0x80000000;
            public const uint GENERIC_WRITE = 0x40000000;
            public const uint OPEN_EXISTING = 3;
            public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
            public const int ERROR_FILE_NOT_FOUND = 2;
            public const int ERROR_PATH_NOT_FOUND = 3;
            public const int ERROR_ACCESS_DENIED = 5;
            public const int ERROR_INSUFFICIENT_BUFFER = 122;
            public const uint PURGE_RXABORT = 0x0002;
            public const uint PURGE_RXCLEAR = 0x0008;
            public const uint DCB_FBINARY = 0x0001;

            [StructLayout(LayoutKind.Sequential)]
            public struct DCB
            {
                public uint DCBlength;
                public uint BaudRate;
                public uint Flags;
                public ushort wReserved;
                public ushort XonLim;
                public ushort XoffLim;
                public byte ByteSize;
                public byte Parity;
                public byte StopBits;
                public sbyte XonChar;
                public sbyte XoffChar;
                public sbyte ErrorChar;
                public sbyte EofChar;
                public sbyte EvtChar;
                public ushort wReserved1;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct COMMTIMEOUTS
            {
                public uint ReadIntervalTimeout;
                public uint ReadTotalTimeoutMultiplier;
                public uint ReadTotalTimeoutConstant;
                public uint WriteTotalTimeoutMultiplier;
                public uint WriteTotalTimeoutConstant;
            }

            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern IntPtr CreateFileW(string fileName, uint access, uint share, IntPtr security,
                uint creation, uint flags, IntPtr template);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool CloseHandle(IntPtr handle);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool GetCommState(IntPtr handle, ref DCB dcb);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool SetCommState(IntPtr handle, ref DCB dcb);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool SetCommTimeouts(IntPtr handle, ref COMMTIMEOUTS timeouts);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool SetupComm(IntPtr handle, uint inQueue, uint outQueue);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool PurgeComm(IntPtr handle, uint flags);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool ReadFile(IntPtr handle, IntPtr buffer, uint toRead, out uint read, IntPtr overlapped);

            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern int QueryDosDeviceW(string deviceName, [Out] char[] targetPath, int max);
        }
    }
}
