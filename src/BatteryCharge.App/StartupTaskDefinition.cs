using System.Xml.Linq;

namespace BatteryCharge.App;

internal sealed record StartupRegistration(bool Enabled, bool UsesCurrentPath);

internal static class StartupTaskDefinition
{
    internal const string Owner = "BatteryCharge.Standalone.Startup";
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    internal static string Create(string userSid, string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userSid);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var task = new XElement(Ns + "Task", new XAttribute("version", "1.2"),
            new XElement(Ns + "RegistrationInfo",
                new XElement(Ns + "Description", "电池充电助手：用户登录后自动启动到托盘。"),
                new XElement(Ns + "Source", Owner)),
            new XElement(Ns + "Triggers",
                new XElement(Ns + "LogonTrigger",
                    new XElement(Ns + "Enabled", true),
                    new XElement(Ns + "UserId", userSid),
                    new XElement(Ns + "Delay", "PT10S"))),
            new XElement(Ns + "Principals",
                new XElement(Ns + "Principal", new XAttribute("id", "CurrentUser"),
                    new XElement(Ns + "UserId", userSid),
                    new XElement(Ns + "LogonType", "InteractiveToken"),
                    new XElement(Ns + "RunLevel", "HighestAvailable"))),
            new XElement(Ns + "Settings",
                new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(Ns + "DisallowStartIfOnBatteries", false),
                new XElement(Ns + "StopIfGoingOnBatteries", false),
                new XElement(Ns + "StartWhenAvailable", true),
                new XElement(Ns + "Enabled", true),
                new XElement(Ns + "ExecutionTimeLimit", "PT0S")),
            new XElement(Ns + "Actions", new XAttribute("Context", "CurrentUser"),
                new XElement(Ns + "Exec",
                    new XElement(Ns + "Command", executablePath),
                    new XElement(Ns + "Arguments", "--startup"))));
        return task.ToString(SaveOptions.DisableFormatting);
    }

    internal static XElement ParseOwned(string xml, string userSid)
    {
        var task = XElement.Parse(xml);
        if (task.Name != Ns + "Task"
            || (string?)task.Element(Ns + "RegistrationInfo")?.Element(Ns + "Source") != Owner
            || (string?)task.Element(Ns + "Principals")?.Element(Ns + "Principal")?.Element(Ns + "UserId") != userSid)
            throw new InvalidOperationException("同名计划任务不属于当前用户的电池充电助手，未修改该任务。");
        return task;
    }

    internal static StartupRegistration Read(string xml, string userSid, string executablePath)
    {
        var task = ParseOwned(xml, userSid);
        var principal = task.Element(Ns + "Principals")?.Element(Ns + "Principal");
        var trigger = task.Element(Ns + "Triggers")?.Element(Ns + "LogonTrigger");
        var actions = task.Element(Ns + "Actions")?.Elements().ToArray();
        var enabled = (bool?)task.Element(Ns + "Settings")?.Element(Ns + "Enabled") != false
            && trigger is not null
            && (bool?)trigger.Element(Ns + "Enabled") != false
            && (string?)trigger.Element(Ns + "UserId") == userSid
            && (string?)principal?.Element(Ns + "LogonType") == "InteractiveToken"
            && (string?)principal?.Element(Ns + "RunLevel") == "HighestAvailable"
            && actions is { Length: 1 }
            && actions[0].Name == Ns + "Exec"
            && (string?)actions[0].Element(Ns + "Arguments") == "--startup";
        var usesCurrentPath = actions is { Length: 1 }
            && string.Equals((string?)actions[0].Element(Ns + "Command"), executablePath, StringComparison.OrdinalIgnoreCase);
        return new StartupRegistration(enabled, usesCurrentPath);
    }
}
