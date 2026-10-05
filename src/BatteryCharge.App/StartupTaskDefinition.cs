using System.Xml.Linq;
using BatteryCharge.Core;

namespace BatteryCharge.App;

internal sealed record StartupRegistration(bool Enabled, bool UsesCurrentPath,
    string? CurrentExecutablePath = null, string? RegisteredExecutablePath = null);

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
                new XElement(Ns + "Description", UiText.Get("StartupTaskDescription")),
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

    internal static XElement ParseOwned(string xml, string userSid, Func<string, string?>? resolveUserSid = null)
    {
        var task = XElement.Parse(xml);
        if (task.Name != Ns + "Task"
            || (string?)task.Element(Ns + "RegistrationInfo")?.Element(Ns + "Source") != Owner
            || !MatchesUser((string?)task.Element(Ns + "Principals")?.Element(Ns + "Principal")?.Element(Ns + "UserId"),
                userSid, resolveUserSid))
            throw new InvalidOperationException(UiText.Get("ForeignStartupTask"));
        return task;
    }

    internal static StartupRegistration Read(string xml, string userSid, string executablePath,
        Func<string, string?>? resolveUserSid = null)
    {
        var task = ParseOwned(xml, userSid, resolveUserSid);
        var principal = task.Element(Ns + "Principals")?.Element(Ns + "Principal");
        var trigger = task.Element(Ns + "Triggers")?.Element(Ns + "LogonTrigger");
        var actions = task.Element(Ns + "Actions")?.Elements().ToArray();
        var enabled = (bool?)task.Element(Ns + "Settings")?.Element(Ns + "Enabled") != false
            && trigger is not null
            && (bool?)trigger.Element(Ns + "Enabled") != false
            && MatchesUser((string?)trigger.Element(Ns + "UserId"), userSid, resolveUserSid)
            && (string?)principal?.Element(Ns + "LogonType") == "InteractiveToken"
            && (string?)principal?.Element(Ns + "RunLevel") == "HighestAvailable"
            && actions is { Length: 1 }
            && actions[0].Name == Ns + "Exec"
            && (string?)actions[0].Element(Ns + "Arguments") == "--startup";
        var registeredPath = actions is { Length: 1 } && actions[0].Name == Ns + "Exec"
            ? (string?)actions[0].Element(Ns + "Command") : null;
        var usesCurrentPath = string.Equals(registeredPath, executablePath, StringComparison.OrdinalIgnoreCase);
        return new StartupRegistration(enabled, usesCurrentPath, executablePath, registeredPath);
    }

    private static bool MatchesUser(string? taskUserId, string userSid, Func<string, string?>? resolveUserSid)
    {
        if (string.IsNullOrWhiteSpace(taskUserId))
            return false;
        if (string.Equals(taskUserId, userSid, StringComparison.OrdinalIgnoreCase))
            return true;
        // Task Scheduler accepts both SIDs and account names. Compare identities,
        // not their XML spelling, while still rejecting tasks for other users.
        if (taskUserId.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase))
            return false;
        return resolveUserSid is not null
            && string.Equals(resolveUserSid(taskUserId), userSid, StringComparison.OrdinalIgnoreCase);
    }
}
