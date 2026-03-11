namespace Allow2.Models;

/// <summary>
/// Per-activity result within a check response.
/// </summary>
public record ActivityResult(
    ulong Id,
    string Name,
    bool Allowed,
    bool Banned,
    int Remaining,
    long Expires,
    bool TimeblockAllowed
);

/// <summary>
/// Result of a permission check from the Allow2 API.
/// </summary>
public record CheckResult(
    bool Allowed,
    List<ActivityResult> Activities,
    string DayTypeToday,
    string DayTypeTomorrow,
    long Timestamp
);
