using System.ComponentModel;
using System.Runtime.InteropServices;
using BatteryCharge.Core;
using Microsoft.Win32.SafeHandles;

namespace BatteryCharge.App;

// Hold every directory component open without write/delete sharing on Windows.
// This prevents both replacement and changing a checked directory into a reparse point.
internal sealed class SafeDirectory : IDisposable
{
    private readonly List<SafeFileHandle> _handles = [];
    internal IReadOnlyList<string> Paths { get; private set; } = [];

    internal static SafeDirectory Acquire(string path, bool create = false)
    {
        var lease = new SafeDirectory();
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full)!;
            if (OperatingSystem.IsWindows() && (root.StartsWith(@"\\", StringComparison.Ordinal)
                || root.Length != 3 || root[1] != ':'))
                throw new IOException(UiText.Get("UnsafeDirectory", full));
            var paths = new List<string> { root };
            var current = root;
            foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                paths.Add(current);
            }
            foreach (var directory in paths)
            {
                if (create && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory); // Parent components are already pinned.
                if (OperatingSystem.IsWindows())
                {
                    var handle = Native.CreateFileW(directory, 0x80, 1, IntPtr.Zero, 3,
                        0x02000000 | 0x00200000, IntPtr.Zero); // BACKUP_SEMANTICS | OPEN_REPARSE_POINT
                    if (handle.IsInvalid)
                    {
                        var error = Marshal.GetLastWin32Error();
                        handle.Dispose();
                        throw new IOException(UiText.Get("UnsafeDirectory", directory), new Win32Exception(error));
                    }
                    lease._handles.Add(handle);
                    if (!Native.GetFileInformationByHandleEx(handle, 9, out var info, 8)
                        || (info.Attributes & (uint)FileAttributes.ReparsePoint) != 0
                        || (info.Attributes & (uint)FileAttributes.Directory) == 0)
                        throw new IOException(UiText.Get("UnsafeDirectory", directory));
                }
                else if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    // Non-Windows branch supports portable behavior checks, not Windows race guarantees.
                    throw new IOException(UiText.Get("UnsafeDirectory", directory));
                }
            }
            lease.Paths = paths;
            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    internal static SafeFileHandle OpenRegularFile(string path, bool readData = false)
    {
        var access = readData ? 0x80020080u : 0x20080u;
        var handle = Native.CreateFileW(path, access, 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (!handle.IsInvalid && Native.GetFileInformationByHandleEx(handle, 9, out var info, 8)
            && (info.Attributes & (uint)(FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0)
            return handle;
        var error = Marshal.GetLastWin32Error();
        handle.Dispose();
        throw new IOException(UiText.Get("UnsafeStartupPath", path), new Win32Exception(error));
    }

    public void Dispose()
    {
        for (var index = _handles.Count - 1; index >= 0; index--)
            _handles[index].Dispose();
        _handles.Clear();
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct AttributeTag { internal uint Attributes; internal uint Tag; }

        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFileW(string name, uint access, uint share,
            IntPtr security, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass,
            out AttributeTag information, uint size);
    }
}
