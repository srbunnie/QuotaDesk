using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Ports;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AIUsageMonitor;

/// <summary>
/// Wire format of the 3.5" USB-C "smart screens" (Turing Smart Screen "revision A" and the UsbPCMonitor clones, 320x480).
/// They are not Windows displays: the screen shows up as a serial port and the PC pushes RGB565 bitmaps to it.
/// Kept as pure functions so it can be checked without the hardware.
/// </summary>
internal static class UsbScreenProtocol
{
    public const int Width = 320, Height = 480;
    public const int ChunkBytes = Width * 8;
    private const byte Hello = 69, DisplayBitmap = 197;

    public static byte[] HelloPacket() => [Hello, Hello, Hello, Hello, Hello, Hello];

    /// <summary>Six bytes: x, y, ex and ey packed as four 10-bit fields, then the command.</summary>
    public static byte[] Command(byte command, int x, int y, int ex, int ey) =>
    [
        (byte)(x >> 2),
        (byte)(((x & 3) << 6) + (y >> 4)),
        (byte)(((y & 15) << 4) + (ex >> 6)),
        (byte)(((ex & 63) << 2) + (ey >> 8)),
        (byte)(ey & 255),
        command
    ];

    /// <summary>Announces that RGB565 pixels for the given rectangle follow.</summary>
    public static byte[] BitmapCommand(int x, int y, int width, int height) => Command(DisplayBitmap, x, y, x + width - 1, y + height - 1);

    /// <summary>Little-endian RGB565, row by row (2 bytes per pixel).</summary>
    public static byte[] ToRgb565(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[bitmap.Width * 4];
            var result = new byte[bitmap.Width * bitmap.Height * 2];
            var o = 0;
            for (var y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (var x = 0; x < row.Length; x += 4)
                {
                    var v = ((row[x + 2] & 0xF8) << 8) | ((row[x + 1] & 0xFC) << 3) | (row[x] >> 3);
                    result[o++] = (byte)v;
                    result[o++] = (byte)(v >> 8);
                }
            }
            return result;
        }
        finally { bitmap.UnlockBits(data); }
    }
}

/// <summary>One open serial link to a 3.5" USB screen. <see cref="Send"/> blocks for a second or two: call it off the UI thread.</summary>
internal sealed class UsbSerialScreen : IDisposable
{
    private const string UsbEnum = @"SYSTEM\CurrentControlSet\Enum\USB\VID_1A86&PID_5722"; // CH340 inside the Turing 3.5"
    private readonly object _gate = new();
    private SerialPort? _port;

    public bool IsOpen => _port is { IsOpen: true };
    public string? PortName => _port?.PortName;

    /// <summary>COM port of a connected screen, or null. Windows keeps the registry entry of unplugged devices, so the port must also be listed as present.</summary>
    public static string? FindPortName()
    {
        try
        {
            using var device = Registry.LocalMachine.OpenSubKey(UsbEnum);
            if (device is null) return null;
            var present = SerialPort.GetPortNames();
            foreach (var instance in device.GetSubKeyNames())
            {
                using var parameters = device.OpenSubKey(instance + @"\Device Parameters");
                if (parameters?.GetValue("PortName") is string name && present.Contains(name, StringComparer.OrdinalIgnoreCase)) return name;
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return null;
    }

    public bool TryOpen(string portName)
    {
        lock (_gate)
        {
            Close();
            SerialPort? port = null;
            try
            {
                port = new SerialPort(portName, 115200) { Handshake = Handshake.RequestToSend, ReadTimeout = 500, WriteTimeout = 3000 };
                port.Open();
                var hello = UsbScreenProtocol.HelloPacket();
                port.Write(hello, 0, hello.Length);
                // The official Turing 3.5" never answers; UsbPCMonitor models do, and the 5" and 7" ones have other resolutions.
                var reply = new byte[6];
                try { for (var got = 0; got < reply.Length;) got += port.Read(reply, got, reply.Length - got); }
                catch (TimeoutException) { reply[0] = 0; }
                port.DiscardInBuffer();
                if (reply[0] is 2 or 3 && reply.All(b => b == reply[0])) { port.Dispose(); return false; }
                _port = port;
                return true;
            }
            // TimeoutException: the hello write stalls on the RTS handshake. Without it here the open port leaked and
            // every later attempt failed with "access denied" until the finalizer ran.
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or TimeoutException)
            {
                port?.Dispose();
                return false;
            }
        }
    }

    /// <summary>Sends one full 320x480 RGB565 frame.</summary>
    public void Send(byte[] rgb565)
    {
        lock (_gate)
        {
            var port = _port is { IsOpen: true } ? _port : throw new InvalidOperationException("screen not connected");
            var command = UsbScreenProtocol.BitmapCommand(0, 0, UsbScreenProtocol.Width, UsbScreenProtocol.Height);
            port.Write(command, 0, command.Length);
            for (var offset = 0; offset < rgb565.Length; offset += UsbScreenProtocol.ChunkBytes)
                port.Write(rgb565, offset, Math.Min(UsbScreenProtocol.ChunkBytes, rgb565.Length - offset));
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            var port = _port;
            _port = null;
            try { port?.Dispose(); } catch (IOException) { }
        }
    }

    public void Dispose() => Close();
}
