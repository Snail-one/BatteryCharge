using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using BatteryCharge.Core;

namespace BatteryCharge.App;

[SupportedOSPlatform("windows")]
internal static class StartupPathSecurity
{
    internal static IDisposable Acquire(string executable)
    {
        var full = Path.GetFullPath(executable);
        if (new DriveInfo(Path.GetPathRoot(full)!).DriveType != DriveType.Fixed)
            throw new IOException(UiText.Get("UnsafeStartupPath", full));
        var directory = Path.GetDirectoryName(full)!;
        var lease = SafeDirectory.Acquire(directory);
        try
        {
            // Keep the executable pinned while checking its ACL and registering the task.
            var file = SafeDirectory.OpenRegularFile(full);
            try
            {
                for (var index = 0; index < lease.Paths.Count; index++)
                {
                    var security = new DirectoryInfo(lease.Paths[index]).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
                    if (!IsProtected(security, ancestor: index != lease.Paths.Count - 1))
                        throw new IOException(UiText.Get("UnsafeStartupPath", full));
                }
                var fileSecurity = new FileInfo(full).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
                if (!IsProtected(fileSecurity, ancestor: false))
                    throw new IOException(UiText.Get("UnsafeStartupPath", full));
                return new RegistrationLease(lease, file);
            }
            catch { file.Dispose(); throw; }
        }
        catch { lease.Dispose(); throw; }
    }

    internal static bool IsProtected(FileSystemSecurity security, bool ancestor)
    {
        // An untrusted owner can grant themselves write access even if the present ACL is read-only.
        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !IsTrusted(owner))
            return false;
        var dangerous = FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles
            | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        if (!ancestor)
            dangerous |= FileSystemRights.Write;
        var dangerousMask = (uint)dangerous | 0x10000000u | 0x40000000u; // GENERIC_ALL | GENERIC_WRITE
        var descriptor = new RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(), 0);
        if (descriptor.DiscretionaryAcl is null) // Null DACL grants unrestricted access.
            return false;
        foreach (var rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>())
        {
            if (rule.AccessControlType == AccessControlType.Allow
                && (rule.PropagationFlags & PropagationFlags.InheritOnly) == 0
                && ((uint)rule.FileSystemRights & dangerousMask) != 0
                && !IsTrusted((SecurityIdentifier)rule.IdentityReference))
                return false; // Conservative: do not try to override broad allows with deny entries.
        }
        return true;
    }

    private static bool IsTrusted(SecurityIdentifier identity) =>
        identity.IsWellKnown(WellKnownSidType.LocalSystemSid)
        || identity.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)
        // NT SERVICE\TrustedInstaller, a fixed Windows service SID.
        || identity.Value == "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

    private sealed class RegistrationLease(IDisposable directory, IDisposable file) : IDisposable
    {
        public void Dispose() { file.Dispose(); directory.Dispose(); }
    }
}
