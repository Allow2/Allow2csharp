namespace Allow2;

using System.Text.Json;
using Allow2.Models;

/// <summary>
/// Polls /api/getUpdates for changes to children, day types, quotas, bans,
/// and approved time extensions. Emits granular results so the consuming
/// daemon or application can react to each type of change independently.
///
/// The update poller uses delta timestamps so subsequent polls only return
/// changes since the last successful poll.
/// </summary>
internal class Allow2UpdatePoller
{
    private readonly Allow2Api _api;

    internal Allow2UpdatePoller(Allow2Api api)
    {
        _api = api;
    }

    /// <summary>
    /// Fetch updates from the Allow2 API.
    /// POST /api/getUpdates (API URL, not service URL).
    /// </summary>
    /// <param name="creds">Stored pairing credentials</param>
    /// <param name="deviceToken">Device token from pairing</param>
    /// <param name="lastTimestamp">Timestamp from the previous poll for delta updates, or null for full sync</param>
    /// <returns>Parsed update result with children, extensions, day type changes, etc.</returns>
    internal async Task<UpdateResult> GetUpdatesAsync(
        Allow2StoredCredentials creds,
        string deviceToken,
        long? lastTimestamp = null)
    {
        var body = new Dictionary<string, object>
        {
            ["userId"] = creds.UserId.ToString(),
            ["pairId"] = creds.PairId.ToString(),
            ["pairToken"] = creds.PairToken,
            ["deviceToken"] = deviceToken
        };

        if (lastTimestamp.HasValue)
        {
            body["timestampMillis"] = lastTimestamp.Value;
        }

        using var response = await _api.PostAsync(_api.ApiUrl, "/api/getUpdates", body);
        var root = response.RootElement;

        return ParseUpdateResult(root);
    }

    /// <summary>
    /// Parse the full getUpdates response into a strongly-typed UpdateResult.
    /// </summary>
    private static UpdateResult ParseUpdateResult(JsonElement root)
    {
        long timestamp = root.TryGetProperty("timestampMillis", out var ts)
            ? ts.GetInt64()
            : 0;

        var children = ParseChildren(root);
        var extensions = ParseExtensions(root);
        var dayTypeChanges = ParseDayTypeChanges(root);
        var quotaUpdates = ParseQuotaUpdates(root);
        var bans = ParseBans(root);

        return new UpdateResult(
            TimestampMillis: timestamp,
            Children: children,
            Extensions: extensions,
            DayTypeChanges: dayTypeChanges,
            QuotaUpdates: quotaUpdates,
            Bans: bans
        );
    }

    private static List<Child> ParseChildren(JsonElement root)
    {
        var children = new List<Child>();

        if (!root.TryGetProperty("children", out var childrenArr) ||
            childrenArr.ValueKind != JsonValueKind.Array)
        {
            return children;
        }

        foreach (var c in childrenArr.EnumerateArray())
        {
            children.Add(new Child(
                Id: c.GetProperty("id").GetUInt64(),
                Name: c.GetProperty("name").GetString() ?? "",
                PinHash: c.TryGetProperty("pin_hash", out var ph) ? ph.GetString()
                       : c.TryGetProperty("pin", out var pin) ? pin.GetString()
                       : null,
                PinSalt: c.TryGetProperty("pin_salt", out var ps) ? ps.GetString()
                       : c.TryGetProperty("pinSalt", out var psa) ? psa.GetString()
                       : null,
                AvatarUrl: c.TryGetProperty("avatar_url", out var av) ? av.GetString()
                         : c.TryGetProperty("avatarUrl", out var avu) ? avu.GetString()
                         : null,
                LinkedAccountId: c.TryGetProperty("linked_account_id", out var la)
                    ? la.ValueKind == JsonValueKind.Number ? la.GetUInt64() : 0
                    : c.TryGetProperty("linkedAccountId", out var lac)
                        ? lac.ValueKind == JsonValueKind.Number ? lac.GetUInt64() : 0
                        : 0,
                HasAccount: c.TryGetProperty("has_account", out var ha) ? ha.GetBoolean()
                          : c.TryGetProperty("hasAccount", out var hac) && hac.GetBoolean()
            ));
        }

        return children;
    }

    private static List<ExtensionUpdate> ParseExtensions(JsonElement root)
    {
        var extensions = new List<ExtensionUpdate>();

        if (!root.TryGetProperty("extensions", out var arr) ||
            arr.ValueKind != JsonValueKind.Array)
        {
            return extensions;
        }

        foreach (var e in arr.EnumerateArray())
        {
            extensions.Add(new ExtensionUpdate(
                ChildId: e.TryGetProperty("childId", out var cid)
                    ? cid.GetUInt64() : 0,
                Activity: e.TryGetProperty("activity", out var act)
                    ? act.GetUInt64() : 0,
                AdditionalMinutes: e.TryGetProperty("additionalMinutes", out var am)
                    ? am.GetInt32() : 0
            ));
        }

        return extensions;
    }

    private static List<DayTypeChange> ParseDayTypeChanges(JsonElement root)
    {
        var changes = new List<DayTypeChange>();

        if (!root.TryGetProperty("dayTypeChanges", out var arr) ||
            arr.ValueKind != JsonValueKind.Array)
        {
            return changes;
        }

        foreach (var d in arr.EnumerateArray())
        {
            changes.Add(new DayTypeChange(
                ChildId: d.TryGetProperty("childId", out var cid)
                    ? cid.GetUInt64() : 0,
                DayType: d.TryGetProperty("dayType", out var dt)
                    ? dt.GetString() ?? "" : ""
            ));
        }

        return changes;
    }

    private static List<QuotaUpdate> ParseQuotaUpdates(JsonElement root)
    {
        var updates = new List<QuotaUpdate>();

        if (!root.TryGetProperty("quotaUpdates", out var arr) ||
            arr.ValueKind != JsonValueKind.Array)
        {
            return updates;
        }

        foreach (var q in arr.EnumerateArray())
        {
            updates.Add(new QuotaUpdate(
                ChildId: q.TryGetProperty("childId", out var cid)
                    ? cid.GetUInt64() : 0,
                Activity: q.TryGetProperty("activity", out var act)
                    ? act.GetUInt64() : 0,
                NewQuota: q.TryGetProperty("newQuota", out var nq)
                    ? nq.GetInt32() : 0
            ));
        }

        return updates;
    }

    private static List<BanUpdate> ParseBans(JsonElement root)
    {
        var bans = new List<BanUpdate>();

        if (!root.TryGetProperty("bans", out var arr) ||
            arr.ValueKind != JsonValueKind.Array)
        {
            return bans;
        }

        foreach (var b in arr.EnumerateArray())
        {
            bans.Add(new BanUpdate(
                ChildId: b.TryGetProperty("childId", out var cid)
                    ? cid.GetUInt64() : 0,
                Activity: b.TryGetProperty("activity", out var act)
                    ? act.GetUInt64() : 0,
                Banned: b.TryGetProperty("banned", out var ban) && ban.GetBoolean()
            ));
        }

        return bans;
    }
}

// ── Update Result Models ──────────────────────────────────────────────

/// <summary>
/// Complete result from a getUpdates API call.
/// Contains all changes since the last poll timestamp.
/// </summary>
public record UpdateResult(
    /// <summary>Server timestamp for use in subsequent delta polls</summary>
    long TimestampMillis,
    /// <summary>Updated children list (names, PINs, added/removed)</summary>
    List<Child> Children,
    /// <summary>Approved time extensions from parent</summary>
    List<ExtensionUpdate> Extensions,
    /// <summary>Day type changes (e.g., school day switched to holiday)</summary>
    List<DayTypeChange> DayTypeChanges,
    /// <summary>Quota changes (daily limits updated by parent)</summary>
    List<QuotaUpdate> QuotaUpdates,
    /// <summary>Activity bans/unbans</summary>
    List<BanUpdate> Bans
);

/// <summary>
/// Parent approved extra time for a child's activity.
/// </summary>
public record ExtensionUpdate(
    ulong ChildId,
    ulong Activity,
    int AdditionalMinutes
);

/// <summary>
/// Day type changed for a child (e.g., "School Day" to "Holiday").
/// </summary>
public record DayTypeChange(
    ulong ChildId,
    string DayType
);

/// <summary>
/// Daily quota updated for a child's activity.
/// </summary>
public record QuotaUpdate(
    ulong ChildId,
    ulong Activity,
    int NewQuota
);

/// <summary>
/// Activity banned or unbanned for a child.
/// </summary>
public record BanUpdate(
    ulong ChildId,
    ulong Activity,
    bool Banned
);
