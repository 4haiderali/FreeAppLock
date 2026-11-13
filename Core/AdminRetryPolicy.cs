namespace FreeAppLock;

/// <summary>
/// Best-effort admin password throttling, persisted with LockSettings.
/// This cannot protect against a user who can alter settings.json or system time.
/// </summary>
internal static class AdminRetryPolicy
{
    internal const int FailuresPerRound = 5;
    private const int MaxCooldownLevel = 5;

    internal static TimeSpan Remaining(LockSettings settings, DateTimeOffset now)
    {
        if (settings.AdminBlockedUntilUtc is not { } until || until <= now)
            return TimeSpan.Zero;
        return until - now;
    }

    /// <summary>Returns a nonzero wait period when a failed attempt starts a cooldown.</summary>
    internal static TimeSpan RecordFailure(LockSettings settings, DateTimeOffset now)
    {
        settings.AdminFailedAttempts = Math.Clamp(settings.AdminFailedAttempts, 0, FailuresPerRound - 1) + 1;
        if (settings.AdminFailedAttempts < FailuresPerRound) return TimeSpan.Zero;

        // 60, 120, 240, 480, then 900 seconds maximum.
        int seconds = Math.Min(900, 60 * (1 << Math.Min(settings.AdminCooldownLevel, 4)));
        var duration = TimeSpan.FromSeconds(seconds);
        settings.AdminBlockedUntilUtc = now.Add(duration);
        settings.AdminCooldownLevel = Math.Min(settings.AdminCooldownLevel + 1, MaxCooldownLevel);
        settings.AdminFailedAttempts = 0;
        return duration;
    }

    internal static void Reset(LockSettings settings)
    {
        settings.AdminFailedAttempts = 0;
        settings.AdminCooldownLevel = 0;
        settings.AdminBlockedUntilUtc = null;
    }
}
