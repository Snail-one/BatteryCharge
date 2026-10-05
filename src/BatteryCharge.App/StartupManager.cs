using System.Runtime.InteropServices;
using System.Security.Principal;

namespace BatteryCharge.App;

internal sealed class StartupManager
{
    private readonly string _userSid;
    private readonly string _executablePath;
    private readonly string _taskName;

    internal StartupManager()
    {
        using var identity = WindowsIdentity.GetCurrent();
        _userSid = identity.User?.Value
            ?? throw new InvalidOperationException("无法确定当前 Windows 用户。");
        _executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定程序路径。");
        _taskName = $"BatteryCharge.Startup.{_userSid}";
    }

    internal StartupRegistration Read() => WithFolder(folder =>
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

    internal void SetEnabled(bool enabled)
    {
        // A framework-dependent DLL launched via dotnet.exe is not a stable startup target.
        if (enabled && !string.Equals(Path.GetFileName(_executablePath), "BatteryCharge.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请使用发布目录中的 BatteryCharge.exe 开启自动启动。");

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

    private object? FindTask(dynamic folder)
    {
        try
        {
            return folder.GetTask(_taskName);
        }
        catch (COMException error) when (error.HResult == unchecked((int)0x80070002))
        {
            return null;
        }
    }

    private static T WithFolder<T>(Func<dynamic, T> action)
    {
        object? service = null;
        object? folder = null;
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!;
            service = Activator.CreateInstance(type)
                ?? throw new InvalidOperationException("无法连接 Windows 任务计划程序。");
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
