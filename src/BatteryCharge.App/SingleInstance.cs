using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using BatteryCharge.Core;
using Microsoft.Win32.SafeHandles;

namespace BatteryCharge.App;

// A private namespace with an enabled administrator SID and high integrity boundary.
// Ordinary processes cannot precreate or open the objects inside it.
internal sealed class SingleInstance : IDisposable
{
    private IntPtr _namespace;
    private Mutex? _mutex;
    internal EventWaitHandle ShowWindow { get; private set; } = null!;
    internal bool IsFirst { get; private set; }
    internal bool CanActivate { get; private set; }

    internal SingleInstance(string alias = "BatteryChargeSecureV2")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        if (alias.Length > 80 || alias.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '.'))
            throw new ArgumentException("Invalid private namespace name.", nameof(alias));
        var boundary = Native.CreateBoundaryDescriptorW(alias, 0);
        IntPtr admin = IntPtr.Zero, high = IntPtr.Zero, security = IntPtr.Zero;
        try
        {
            if (boundary == IntPtr.Zero
                || !Native.ConvertStringSidToSidW("S-1-5-32-544", out admin)
                || !Native.AddSIDToBoundaryDescriptor(ref boundary, admin)
                || !Native.ConvertStringSidToSidW("S-1-16-12288", out high)
                || !Native.AddIntegrityLabelToBoundaryDescriptor(ref boundary, high)
                || !Native.ConvertStringSecurityDescriptorToSecurityDescriptorW(
                    "D:P(A;;GA;;;SY)(A;;GA;;;BA)S:(ML;;NWNR;;;HI)", 1, out security, out _))
                throw NativeError();
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = security };
            _namespace = Native.CreatePrivateNamespaceW(ref attributes, boundary, alias);
            if (_namespace == IntPtr.Zero && Marshal.GetLastWin32Error() == 183)
                _namespace = Native.OpenPrivateNamespaceW(boundary, alias);
            if (_namespace == IntPtr.Zero)
                throw NativeError();

            var mutexHandle = Native.CreateMutexW(ref attributes, true, $"{alias}\\Instance");
            IsFirst = Marshal.GetLastWin32Error() != 183;
            if (mutexHandle.IsInvalid)
            {
                mutexHandle.Dispose();
                throw NativeError();
            }
            _mutex = new Mutex(false);
            var initialMutexHandle = _mutex.SafeWaitHandle;
            _mutex.SafeWaitHandle = mutexHandle;
            initialMutexHandle.Dispose();

            // Only the real owner creates the event. A duplicate opens it, waiting
            // briefly for initialization, rather than confusing its own new event
            // with evidence of an instance running in another session/location.
            var path = Path.GetFullPath(Environment.ProcessPath!).ToUpperInvariant();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
            using var process = Process.GetCurrentProcess();
            var eventName = $"{alias}\\ShowWindow.{process.SessionId}.{hash}";
            SafeWaitHandle? eventHandle = null;
            if (IsFirst)
            {
                eventHandle = Native.CreateEventW(ref attributes, false, false, eventName);
                if (eventHandle.IsInvalid)
                {
                    eventHandle.Dispose();
                    throw NativeError();
                }
            }
            else
            {
                for (var attempt = 0; attempt < 25; attempt++)
                {
                    var opened = Native.OpenEventW(0x100002, false, eventName); // SYNCHRONIZE | EVENT_MODIFY_STATE
                    if (!opened.IsInvalid) { eventHandle = opened; break; }
                    var error = Marshal.GetLastWin32Error();
                    opened.Dispose();
                    if (error != 2)
                        throw new IOException(UiText.Get("InstanceUnavailable"), new Win32Exception(error));
                    Thread.Sleep(10);
                }
                CanActivate = eventHandle is not null;
            }
            if (eventHandle is not null)
            {
                ShowWindow = new EventWaitHandle(false, EventResetMode.AutoReset);
                var initialEventHandle = ShowWindow.SafeWaitHandle;
                ShowWindow.SafeWaitHandle = eventHandle;
                initialEventHandle.Dispose();
            }
        }
        catch { Dispose(); throw; }
        finally
        {
            if (security != IntPtr.Zero) Native.LocalFree(security);
            if (high != IntPtr.Zero) Native.LocalFree(high);
            if (admin != IntPtr.Zero) Native.LocalFree(admin);
            if (boundary != IntPtr.Zero) Native.DeleteBoundaryDescriptor(boundary);
        }
    }

    private static IOException NativeError() => new(UiText.Get("InstanceUnavailable"),
        new Win32Exception(Marshal.GetLastWin32Error()));

    public void Dispose()
    {
        if (_mutex is not null)
        {
            if (IsFirst) _mutex.ReleaseMutex();
            _mutex.Dispose();
            _mutex = null;
        }
        ShowWindow?.Dispose();
        if (_namespace != IntPtr.Zero)
        {
            Native.ClosePrivateNamespace(_namespace, IsFirst ? 1u : 0u);
            _namespace = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        internal int Length;
        internal IntPtr Descriptor;
        internal int Inherit;
    }

    private static class Native
    {
        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateBoundaryDescriptorW(string name, uint flags);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AddSIDToBoundaryDescriptor(ref IntPtr boundary, IntPtr sid);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AddIntegrityLabelToBoundaryDescriptor(ref IntPtr boundary, IntPtr sid);
        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern void DeleteBoundaryDescriptor(IntPtr boundary);
        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreatePrivateNamespaceW(ref SecurityAttributes attributes, IntPtr boundary, string alias);
        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr OpenPrivateNamespaceW(IntPtr boundary, string alias);
        [DllImport("kernel32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool ClosePrivateNamespace(IntPtr handle, uint flags);
        [DllImport("advapi32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ConvertStringSidToSidW(string sid, out IntPtr result);
        [DllImport("advapi32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, uint revision,
            out IntPtr descriptor, out uint size);
        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern IntPtr LocalFree(IntPtr memory);
        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeWaitHandle CreateEventW(ref SecurityAttributes attributes,
            [MarshalAs(UnmanagedType.Bool)] bool manualReset, [MarshalAs(UnmanagedType.Bool)] bool initialState, string name);
        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeWaitHandle OpenEventW(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, string name);
        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeWaitHandle CreateMutexW(ref SecurityAttributes attributes,
            [MarshalAs(UnmanagedType.Bool)] bool initialOwner, string name);
    }
}
