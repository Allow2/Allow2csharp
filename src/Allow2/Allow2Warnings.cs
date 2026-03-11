namespace Allow2;

/// <summary>
/// Progressive warning levels for approaching time limits.
/// Levels are ordered by severity. The warning controller fires once
/// per level transition (never repeats the same level).
///
/// The thresholds match the Brave browser implementation:
///   15min, 5min, 1min, 30sec, 10sec, then blocked.
/// </summary>
public enum WarningLevel
{
    None = 0,
    FifteenMinutes = 1,
    FiveMinutes = 2,
    OneMinute = 3,
    ThirtySeconds = 4,
    TenSeconds = 5,
    Blocked = 6
}

/// <summary>
/// Warning controller that maps remaining seconds to a WarningLevel
/// and fires callbacks on level transitions. Designed for use with
/// progressive countdown UIs: banners, overlays, block screens.
///
/// Usage:
///   var controller = new Allow2WarningController();
///   var level = controller.Process(remainingSeconds, (level, secs) => {
///       // Show/update warning UI
///   });
/// </summary>
internal class Allow2WarningController
{
    private WarningLevel _lastLevel = WarningLevel.None;

    /// <summary>
    /// Map remaining seconds to the appropriate warning level.
    /// Static and pure -- no side effects.
    /// </summary>
    /// <param name="seconds">Remaining seconds until time expires</param>
    /// <returns>The corresponding warning level</returns>
    internal static WarningLevel ForRemainingSeconds(int seconds) => seconds switch
    {
        <= 0 => WarningLevel.Blocked,
        < 10 => WarningLevel.TenSeconds,
        < 30 => WarningLevel.ThirtySeconds,
        < 60 => WarningLevel.OneMinute,
        < 300 => WarningLevel.FiveMinutes,
        < 900 => WarningLevel.FifteenMinutes,
        _ => WarningLevel.None
    };

    /// <summary>
    /// Process a remaining-seconds value. Fires the callback when the warning
    /// level increases (transitions to a more severe level). Never fires for
    /// the same level twice in a row.
    /// </summary>
    /// <param name="remainingSeconds">Seconds remaining for the activity</param>
    /// <param name="callback">Called on level transitions with (newLevel, remainingSeconds)</param>
    /// <returns>The current warning level</returns>
    internal WarningLevel Process(int remainingSeconds, Action<WarningLevel, int>? callback)
    {
        var level = ForRemainingSeconds(remainingSeconds);
        if (level > _lastLevel)
        {
            _lastLevel = level;
            callback?.Invoke(level, remainingSeconds);
        }

        return level;
    }

    /// <summary>
    /// Reset to initial state. Call when time is extended, a new child
    /// is selected, or the activity transitions from blocked to allowed.
    /// </summary>
    internal void Reset()
    {
        _lastLevel = WarningLevel.None;
    }

    /// <summary>
    /// Get the last emitted warning level without triggering any callback.
    /// </summary>
    internal WarningLevel LastLevel => _lastLevel;
}
