using System.ComponentModel;
using System.Runtime.InteropServices;
using BatteryCharge.Core;
using Microsoft.Win32.SafeHandles;

namespace BatteryCharge.App;

internal sealed class EnergyDevice : IEnergyTransport, IDisposable
{
    private SafeFileHandle? _handle;
    private bool _disposed;

    public uint Query(uint controlCode, uint input) => Exchange(controlCode, input, requireReply: true);
    public void Send(uint controlCode, uint input) => Exchange(controlCode, input, requireReply: false);

    private SafeFileHandle Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handle is { IsInvalid: false, IsClosed: false })
            return _handle;

        // FILE_READ_DATA | FILE_WRITE_DATA; FILE_SHARE_READ | FILE_SHARE_WRITE.
        // This is an existing manufacturer's device, not a regular file.
        var handle = Native.CreateFileW(@"\\.\EnergyDrv", 0x3, 0x3,
            IntPtr.Zero, 3, 0x80, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var code = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw DriverError(UiText.Get("OpenDevice"), code);
        }

        _handle = handle;
        return handle;
    }

    private uint Exchange(uint controlCode, uint input, bool requireReply)
    {
        if (!Native.DeviceIoControl(Open(), controlCode, ref input, sizeof(uint),
            out var output, sizeof(uint), out var returned, IntPtr.Zero))
        {
            var code = Marshal.GetLastWin32Error();
            throw DriverError(UiText.Get("AccessDevice", controlCode), code);
        }

        // Set commands may return no payload. Queries must contain a complete uint.
        if (requireReply && returned != sizeof(uint))
            throw new IOException(UiText.Get("InvalidReply", returned));

        return output;
    }

    private static IOException DriverError(string operation, int code)
    {
        var hint = code switch
        {
            2 or 3 => UiText.Get("DriverMissing"),
            5 => UiText.Get("DriverAccessDenied"),
            1 or 50 => UiText.Get("DriverUnsupported"),
            _ => UiText.Get("DriverCheck")
        };
        var nativeError = new Win32Exception(code);
        return new IOException(UiText.Get("DriverFailure", operation, hint, code), nativeError);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _handle?.Dispose();
    }

    private static class Native
    {
        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFileW(
            string fileName, uint desiredAccess, uint shareMode,
            IntPtr securityAttributes, uint creationDisposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeviceIoControl(
            SafeFileHandle device, uint controlCode,
            ref uint input, uint inputSize, out uint output, uint outputSize,
            out uint returned, IntPtr overlapped);
    }
}
