using Microsoft.Win32;
namespace FreeAppLock;
internal static class StartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string PreferenceKey = @"Software\FreeAppLock";
    internal static bool StartsInTray(string[] args) => args.Contains("--tray", StringComparer.OrdinalIgnoreCase);
    internal static string Command(string executable) => "\"" + executable + "\" --tray";
    internal static bool ResolveEnabled(object? preference, bool userEntry, bool machineEntry)
        => preference is not int enabled || enabled != 0 ? userEntry || machineEntry : false;
    internal static bool IsEnabled()
    {
        using var preference = Registry.CurrentUser.OpenSubKey(PreferenceKey);
        using var user = Registry.CurrentUser.OpenSubKey(RunKey);
        using var machine = Registry.LocalMachine.OpenSubKey(RunKey);
        return ResolveEnabled(preference?.GetValue("StartupEnabled"), user?.GetValue("FreeAppLock") is string, machine?.GetValue("FreeAppLock") is string);
    }
    internal static void ReconcileInstalledStartup()
    {
        using var machine = Registry.LocalMachine.OpenSubKey(RunKey);
        if (!string.Equals(machine?.GetValue("FreeAppLock") as string, Command(Environment.ProcessPath ?? ""), StringComparison.OrdinalIgnoreCase)) return;
        using var user = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        user?.DeleteValue("FreeAppLock", false);
    }
    internal static void SetEnabled(bool enabled)
    {
        using var user = Registry.CurrentUser.CreateSubKey(RunKey);
        using var machine = Registry.LocalMachine.OpenSubKey(RunKey);
        if (enabled && machine?.GetValue("FreeAppLock") is not string)
            user.SetValue("FreeAppLock", Command(Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the executable.")));
        else user.DeleteValue("FreeAppLock", false);
        using var preference = Registry.CurrentUser.CreateSubKey(PreferenceKey);
        preference.SetValue("StartupEnabled", enabled ? 1 : 0, RegistryValueKind.DWord);
    }
}
