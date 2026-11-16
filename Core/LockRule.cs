namespace FreeAppLock;

public sealed class LockRule
{
    public int? AwaySeconds { get; set; } = 0;
    public int? MaximumUnlockedSeconds { get; set; }
    public bool LockOnMinimize { get; set; } = true;
    public string Summary => $"Away: {Duration(AwaySeconds)} · session: {Duration(MaximumUnlockedSeconds)} · minimize: {(LockOnMinimize ? "lock" : "keep session")}";
    static string Duration(int? seconds) => seconds is null ? "never" : seconds == 0 ? "instantly" : seconds < 60 ? $"{seconds}s" : $"{seconds/60.0:0.##}m";
    internal void Validate()
    {
        if (AwaySeconds is < 0 or > 86400 || MaximumUnlockedSeconds is < 1 or > 86400)
            throw new System.IO.InvalidDataException("Rule durations must be between zero and 24 hours (session limit must be positive).");
    }
}
public sealed class BrowserProfileRule
{
    public string Name { get; set; } = "Browser profile";
    public string Browser { get; set; } = "brave";
    public LockRule Timing { get; set; } = new();
}
internal sealed class UnlockLease
{
    internal long GrantedAt { get; }
    internal long? AwayAt { get; set; }
    internal UnlockLease(long grantedAt) => GrantedAt = grantedAt;
}
internal static class LockTiming
{
    internal static bool Expired(LockRule rule, UnlockLease lease, long now, bool focused, bool minimized)
    {
        if (rule.MaximumUnlockedSeconds is { } maximum && now-lease.GrantedAt >= maximum*1000L) return true;
        if (minimized && rule.LockOnMinimize) return true;
        if (focused) { lease.AwayAt=null; return false; }
        lease.AwayAt ??= now;
        return rule.AwaySeconds is { } delay && now-lease.AwayAt.Value >= delay*1000L;
    }
}
