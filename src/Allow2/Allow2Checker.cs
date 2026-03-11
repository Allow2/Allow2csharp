namespace Allow2;

using System.Text.Json;
using Allow2.Models;

/// <summary>
/// Permission check loop. Periodically calls the Allow2 check API and tracks
/// per-activity state.
///
/// Activity ID 8 (Screen Time) is the master device-level switch.
/// When Screen Time is blocked, the entire device should lock.
/// </summary>
internal class Allow2Checker
{
    private readonly Allow2Api _api;
    private CheckResult? _cachedResult;

    private const ulong ScreenTimeActivity = 8;

    internal Allow2Checker(Allow2Api api)
    {
        _api = api;
    }

    /// <summary>
    /// Perform a single permission check against the Allow2 API.
    /// </summary>
    /// <param name="creds">Stored pairing credentials</param>
    /// <param name="deviceToken">Application version token (identifies the app, not the device)</param>
    /// <param name="childId">Currently selected child ID</param>
    /// <param name="activities">Activities to check permissions for</param>
    /// <param name="log">Whether to record usage on the server (true for active use)</param>
    /// <returns>A CheckResult with per-activity allowed/remaining status</returns>
    internal async Task<CheckResult> CheckAsync(
        Allow2StoredCredentials creds,
        string deviceToken,
        ulong? childId,
        List<ActivityCheck> activities,
        bool log = true)
    {
        var activityList = new List<object>();
        foreach (var a in activities)
        {
            activityList.Add(new Dictionary<string, object>
            {
                ["id"] = (ulong)a.Id,
                ["log"] = a.Log
            });
        }

        var body = new Dictionary<string, object>
        {
            ["userId"] = creds.UserId,
            ["pairId"] = creds.PairId,
            ["pairToken"] = creds.PairToken,
            ["deviceToken"] = deviceToken,
            ["tz"] = TimeZoneInfo.Local.Id,
            ["log"] = log,
            ["activities"] = activityList
        };

        if (childId.HasValue)
        {
            body["childId"] = childId.Value;
        }

        try
        {
            using var response = await _api.PostAsync(
                _api.ServiceUrl,
                "/serviceapi/check",
                body);

            var result = ParseCheckResult(response);
            _cachedResult = result;
            return result;
        }
        catch (Allow2Exception ex) when (ex.Code == Allow2ErrorCode.Http401)
        {
            // HTTP 401 = credentials revoked, device unpaired
            throw;
        }
        catch (Exception)
        {
            // Network/timeout error -- fall back to offline cache
            return GetOfflineResult();
        }
    }

    /// <summary>
    /// Get the cached offline result, if available.
    /// </summary>
    internal CheckResult? GetCached()
    {
        return _cachedResult;
    }

    /// <summary>
    /// Clear the offline cache.
    /// </summary>
    internal void Clear()
    {
        _cachedResult = null;
    }

    private CheckResult GetOfflineResult()
    {
        if (_cachedResult != null)
        {
            return _cachedResult;
        }

        // No cache -- deny by default (offline-deny pattern)
        return new CheckResult(
            Allowed: false,
            Activities: new List<ActivityResult>(),
            DayTypeToday: "",
            DayTypeTomorrow: "",
            Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
    }

    private static CheckResult ParseCheckResult(JsonDocument doc)
    {
        var root = doc.RootElement;

        var allowed = !root.TryGetProperty("allowed", out var allowedProp)
            || allowedProp.GetBoolean();

        var dayTypeToday = root.TryGetProperty("dayType", out var dt)
            ? dt.GetString() ?? ""
            : root.TryGetProperty("dayTypes", out var dts) && dts.TryGetProperty("today", out var todayProp)
                ? todayProp.GetString() ?? ""
                : "";

        var dayTypeTomorrow = root.TryGetProperty("dayTypes", out var dts2) && dts2.TryGetProperty("tomorrow", out var tomorrowProp)
            ? tomorrowProp.GetString() ?? ""
            : "";

        var timestamp = root.TryGetProperty("timestamp", out var ts)
            ? ts.GetInt64()
            : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var activityResults = new List<ActivityResult>();

        if (root.TryGetProperty("activities", out var activitiesElement))
        {
            if (activitiesElement.ValueKind == JsonValueKind.Object)
            {
                // Object keyed by activity ID
                foreach (var prop in activitiesElement.EnumerateObject())
                {
                    activityResults.Add(ParseActivityResult(prop.Value, prop.Name));
                }
            }
            else if (activitiesElement.ValueKind == JsonValueKind.Array)
            {
                // Array of activity objects
                foreach (var item in activitiesElement.EnumerateArray())
                {
                    var id = item.TryGetProperty("id", out var idProp)
                        ? idProp.GetUInt64().ToString()
                        : "0";
                    activityResults.Add(ParseActivityResult(item, id));
                }
            }
        }

        return new CheckResult(
            Allowed: allowed,
            Activities: activityResults,
            DayTypeToday: dayTypeToday,
            DayTypeTomorrow: dayTypeTomorrow,
            Timestamp: timestamp
        );
    }

    private static ActivityResult ParseActivityResult(JsonElement element, string fallbackId)
    {
        var id = element.TryGetProperty("id", out var idProp)
            ? idProp.GetUInt64()
            : ulong.TryParse(fallbackId, out var parsed) ? parsed : 0;

        var name = element.TryGetProperty("activity", out var nameProp)
            ? nameProp.GetString() ?? ""
            : element.TryGetProperty("name", out var nameProp2)
                ? nameProp2.GetString() ?? ""
                : "";

        var actAllowed = !element.TryGetProperty("allowed", out var aProp)
            || aProp.GetBoolean();

        var banned = element.TryGetProperty("banned", out var bProp) && bProp.GetBoolean();

        var remaining = element.TryGetProperty("remaining", out var rProp)
            ? rProp.GetInt32()
            : int.MaxValue;

        var expires = element.TryGetProperty("expires", out var eProp)
            ? eProp.GetInt64()
            : 0;

        var timeblockAllowed = !element.TryGetProperty("timeblock", out var tbProp)
            || (tbProp.TryGetProperty("allowed", out var tbAllowed) && tbAllowed.GetBoolean());

        return new ActivityResult(
            Id: id,
            Name: name,
            Allowed: actAllowed,
            Banned: banned,
            Remaining: remaining,
            Expires: expires,
            TimeblockAllowed: timeblockAllowed
        );
    }
}
