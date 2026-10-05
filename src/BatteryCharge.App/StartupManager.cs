using System.Runtime.InteropServices;
using System.Security.Principal;
using BatteryCharge.Core;

namespace BatteryCharge.App;

internal sealed class StartupManager : IStartupManager
{
    private readonly string _userSid;
    private readonly string _executablePath;
    private readonly string _taskName;

    internal StartupManager()
    {
        using var identity = WindowsIdentity.GetCurrent();
        _userSid = identity.User?.Value
            ?? throw new InvalidOperationException(UiText.Get("CurrentUserMissing"));
        _executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException(UiText.Get("ExecutableMissing"));
        _taskName = $"BatteryCharge.Startup.{_userSid}";
    }

    public StartupRegistration Read() => WithFolder(folder =>
    {
        object? task = FindTask(folder);
        try
        {
            return task is null ? new StartupRegistration(false, true)
                : StartupTaskDefinition.Read((string)((dynamic)task).Xml, _userSid, _executablePath);
        }
        finally
        {
            Release(task);
        }
    });

    public void SetEnabled(bool enabled)
    {
        // A framework-dependent DLL launched via dotnet.exe is not a stable startup target.
        if (enabled && !string.Equals(Path.GetFileName(_executablePath), "BatteryCharge.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(UiText.Get("UsePublishedExecutable"));

        WithFolder(folder =>
        {
            object? existing = FindTask(folder);
            try
            {
                if (existing is not null)
                    StartupTaskDefinition.ParseOwned((string)((dynamic)existing).Xml, _userSid);

                if (enabled)
                {
                    // TASK_CREATE_OR_UPDATE = 6; TASK_LOGON_INTERACTIVE_TOKEN = 3.
                    // No password is stored. Registering a logon trigger does not start the app now.
                    object registered = folder.RegisterTask(_taskName,
                        StartupTaskDefinition.Create(_userSid, _executablePath), 6, _userSid, null, 3, null);
                    Release(registered);
                }
                else if (existing is not null)
                {
                    folder.DeleteTask(_taskName, 0);
                }
                return true;
            }
            finally
            {
                Release(existing);
            }
        });
    }

    private object? FindTask(dynamic folder) =>
        StartupTaskLookup.Find(() => folder.GetTask(_taskName));

    public void RemoveForCleanup()
    {
        SetEnabled(false);
        WithFolder(folder =>
        {
            object? remaining = FindTask(folder);
            try
            {
                if (remaining is not null)
                    throw new IOException(UiText.Get("CleanupTaskRemains"));
                return true;
            }
            finally
            {
                Release(remaining);
            }
        });
    }

    private static T WithFolder<T>(Func<dynamic, T> action)
    {
        object? service = null;
        object? folder = null;
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!;
            service = Activator.CreateInstance(type)
                ?? throw new InvalidOperationException(UiText.Get("SchedulerUnavailable"));
            ((dynamic)service).Connect();
            folder = ((dynamic)service).GetFolder(@"\");
            return action(folder);
        }
        finally
        {
            Release(folder);
            Release(service);
        }
    }

    private static void Release(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
            Marshal.ReleaseComObject(instance);
    }
}
