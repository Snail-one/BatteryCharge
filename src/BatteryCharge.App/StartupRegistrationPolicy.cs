using BatteryCharge.Core;

namespace BatteryCharge.App;

internal static class StartupRegistrationPolicy
{
    internal static StartupRegistration Enforce(StartupRegistration registration,
        Action<string> validatePath, Action disable)
    {
        if (!registration.Enabled)
            return registration;
        try
        {
            validatePath(registration.RegisteredExecutablePath!);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            // Only report disabled after Task Scheduler confirms the operation succeeded.
            disable();
            return registration with { Enabled = false, SecurityError = UiText.Get("UnsafeStartupDisabled") };
        }
        return registration;
    }
}
