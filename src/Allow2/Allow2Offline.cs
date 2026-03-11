namespace Allow2;

using Allow2.Models;

/// <summary>
/// Offline handler with grace period and deny-by-default behavior.
///
/// Caches the last successful check result. When the device loses connectivity:
///   1. During grace period: returns the cached result (last known state)
///   2. After grace period: returns a deny-all result (blocked)
///
/// This matches the security principle: no connectivity = no access after grace expires.
/// </summary>
internal class Allow2OfflineHandler
{
    private CheckResult? _cachedResult;
    private DateTime _cacheTimestamp;

    /// <summary>
    /// Default grace period in seconds (5 minutes).
    /// During this period, the cached result is returned when offline.
    /// </summary>
    internal const int DefaultGraceSeconds = 300;

    /// <summary>
    /// Store a successful check result from the API.
    /// Called by Allow2Checker after each successful API response.
    /// </summary>
    /// <param name="result">The check result to cache</param>
    internal void Cache(CheckResult result)
    {
        _cachedResult = result;
        _cacheTimestamp = DateTime.UtcNow;
    }

    /// <summary>
    /// Get the cached check result, or null if nothing is cached.
    /// </summary>
    internal CheckResult? GetCached()
    {
        return _cachedResult;
    }

    /// <summary>
    /// True if a cached result exists and is within the grace period.
    /// </summary>
    /// <param name="graceSeconds">Grace period in seconds (default 300)</param>
    internal bool IsInGracePeriod(int graceSeconds = DefaultGraceSeconds)
    {
        return _cachedResult != null &&
               (DateTime.UtcNow - _cacheTimestamp).TotalSeconds < graceSeconds;
    }

    /// <summary>
    /// Seconds elapsed since the last cached result.
    /// Returns int.MaxValue if nothing is cached.
    /// </summary>
    internal int SecondsSinceLastCheck()
    {
        if (_cachedResult == null)
        {
            return int.MaxValue;
        }

        return (int)(DateTime.UtcNow - _cacheTimestamp).TotalSeconds;
    }

    /// <summary>
    /// Get the appropriate result for offline operation.
    ///
    /// Within grace period: returns the cached result (last known state).
    /// After grace period or no cache: returns a deny-all result.
    /// </summary>
    /// <param name="graceSeconds">Grace period in seconds (default 300)</param>
    /// <returns>The cached result or a deny-all fallback</returns>
    internal CheckResult GetOfflineResult(int graceSeconds = DefaultGraceSeconds)
    {
        if (IsInGracePeriod(graceSeconds) && _cachedResult != null)
        {
            return _cachedResult;
        }

        // Deny-by-default: no cache or grace expired
        return new CheckResult(
            Allowed: false,
            Activities: new List<ActivityResult>(),
            DayTypeToday: "",
            DayTypeTomorrow: "",
            Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
    }

    /// <summary>
    /// Clear the cached result. Call on unpair or credential revocation.
    /// </summary>
    internal void Clear()
    {
        _cachedResult = null;
    }
}
