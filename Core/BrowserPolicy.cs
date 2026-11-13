namespace FreeAppLock;

internal static class BrowserPolicy
{
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "brave", "chrome", "msedge", "firefox", "opera", "opera_gx", "operagx", "vivaldi",
        "chromium", "librewolf", "waterfox", "floorp", "zen", "thorium", "arc", "browser"
    };
    internal static bool PreservesTabs(string processName) => Browsers.Contains(processName);
    internal static bool ShouldTerminate(string processName, bool enabled) => enabled && !PreservesTabs(processName);
}
