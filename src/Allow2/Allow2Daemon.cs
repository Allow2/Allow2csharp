namespace Allow2;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Allow2.Models;

/// <summary>
/// Main orchestrator for the Allow2 Device SDK.
///
/// Manages the full device lifecycle:
///   1. Unpaired   - Sits idle, waits for pairing
///   2. Paired     - Paired but no child selected yet
///   3. Enforcing  - Child selected, check loop running
///   4. Parent     - Parent mode, no enforcement
///
/// Usage:
///   var daemon = new Allow2Daemon("my-device-token");
///   daemon.OnBlocked += (allowed, reason) => ShowBlockScreen(reason);
///   daemon.OnWarning += (level, seconds) => ShowWarning(level, seconds);
///   await daemon.StartAsync();
/// </summary>
public class Allow2Daemon
{
    private readonly string _deviceToken;
    private readonly Allow2Api _api;
    private readonly IAllow2CredentialStore _store;

    // State
    public bool IsPaired { get; private set; }
    public List<Child> Children { get; private set; } = new();
    public Child? CurrentChild { get; private set; }

    // Event callbacks
    public event Action<bool, string?>? OnBlocked;
    public event Action<WarningLevel, int>? OnWarning;
    public event Action? OnCredentialsInvalidated;
    public event Action<List<Child>>? OnChildrenUpdated;

    // Internal state
    private Allow2StoredCredentials? _credentials;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Create a new Allow2Daemon instance.
    /// </summary>
    /// <param name="deviceToken">Application-level token identifying this product.</param>
    /// <param name="environment">Server environment to target.</param>
    /// <param name="store">Credential store implementation. Uses FileCredentialStore if null.</param>
    /// <param name="httpClient">Custom HttpClient for testing. Uses a new instance if null.</param>
    public Allow2Daemon(
        string deviceToken,
        Allow2Environment environment = Allow2Environment.Production,
        IAllow2CredentialStore? store = null,
        HttpClient? httpClient = null)
    {
        _deviceToken = deviceToken ?? throw new ArgumentNullException(nameof(deviceToken));
        _api = new Allow2Api(environment, httpClient);
        _store = store ?? new FileCredentialStore();
    }

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    /// <summary>
    /// Start the daemon. Loads stored credentials and sets IsPaired state.
    /// </summary>
    public async Task StartAsync()
    {
        _credentials = await _store.LoadAsync();
        IsPaired = _credentials != null;

        if (IsPaired)
        {
            await RefreshChildrenAsync();
        }
    }

    // ------------------------------------------------------------------
    // Pairing
    // ------------------------------------------------------------------

    /// <summary>
    /// Initiate a new pairing session. Parent scans QR or enters PIN on their phone.
    /// The device NEVER handles parent credentials.
    /// </summary>
    /// <param name="deviceName">Human-readable device name (e.g. "Emma's PC").</param>
    /// <param name="method">Pairing method: QR code or PIN code.</param>
    /// <returns>A pairing session with QR data or PIN for display.</returns>
    public async Task<PairingSession> InitPairingAsync(
        string deviceName,
        PairingMethod method = PairingMethod.QRCode)
    {
        if (IsPaired)
        {
            throw new Allow2Exception(Allow2ErrorCode.AlreadyPaired, "Device is already paired");
        }

        var path = method == PairingMethod.QRCode
            ? "/api/pair/qr/init"
            : "/api/pair/pin/init";

        var body = new Dictionary<string, object>
        {
            ["uuid"] = Guid.NewGuid().ToString(),
            ["name"] = deviceName,
            ["deviceToken"] = _deviceToken,
            ["platform"] = "dotnet"
        };

        using var doc = await _api.PostAsync(_api.ApiUrl, path, body);
        var root = doc.RootElement;

        return new PairingSession(
            SessionId: GetString(root, "sessionId") ?? "",
            QrCodeData: GetString(root, "qrCodeData"),
            WebPairingUrl: GetString(root, "webPairingUrl"),
            PinCode: GetString(root, "pinCode")
        );
    }

    /// <summary>
    /// Poll pairing status. Call this while waiting for the parent to confirm.
    /// When completed, credentials are stored automatically.
    /// </summary>
    /// <param name="sessionId">Session ID from InitPairingAsync.</param>
    /// <returns>Pairing status. Check Completed to see if parent has confirmed.</returns>
    public async Task<PairingStatus> CheckPairingStatusAsync(string sessionId)
    {
        using var doc = await _api.GetAsync(_api.ApiUrl + "/api/pair/status/" + sessionId);
        var root = doc.RootElement;

        var completed = GetBool(root, "completed");
        if (!completed)
        {
            return new PairingStatus(
                Completed: false,
                UserId: 0,
                PairId: 0,
                PairToken: "",
                Children: new List<Child>()
            );
        }

        var userId = GetUInt64(root, "userId");
        var pairId = GetUInt64(root, "pairId");
        var pairToken = GetString(root, "pairToken") ?? "";

        // Store credentials
        _credentials = new Allow2StoredCredentials(userId, pairId, pairToken);
        await _store.SaveAsync(_credentials);
        IsPaired = true;

        // Parse children
        Children = ParseChildren(root);

        OnChildrenUpdated?.Invoke(Children);

        return new PairingStatus(
            Completed: true,
            UserId: userId,
            PairId: pairId,
            PairToken: pairToken,
            Children: Children
        );
    }

    // ------------------------------------------------------------------
    // Child Selection
    // ------------------------------------------------------------------

    /// <summary>
    /// Select a child for enforcement. Verifies PIN using SHA-256 hash comparison.
    /// </summary>
    /// <param name="childId">ID of the child to select.</param>
    /// <param name="pin">The child's 6-digit PIN.</param>
    /// <returns>True if the PIN was correct and child was selected.</returns>
    public bool SelectChild(ulong childId, string pin)
    {
        EnsurePaired();

        var child = Children.Find(c => c.Id == childId);
        if (child == null)
        {
            throw new Allow2Exception(Allow2ErrorCode.MissingChild,
                "Child not found: " + childId);
        }

        // If no PIN is set on the child, accept any input
        if (string.IsNullOrEmpty(child.PinHash) || string.IsNullOrEmpty(child.PinSalt))
        {
            CurrentChild = child;
            return true;
        }

        // Verify PIN: SHA-256(pin + salt) must match stored hash
        var hash = HashPin(pin, child.PinSalt);
        if (!string.Equals(hash, child.PinHash, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        CurrentChild = child;
        return true;
    }

    /// <summary>
    /// Clear the current child selection. Stops enforcement.
    /// </summary>
    public void ClearCurrentChild()
    {
        CurrentChild = null;
    }

    // ------------------------------------------------------------------
    // Check
    // ------------------------------------------------------------------

    /// <summary>
    /// Check permissions for the current child and specified activities.
    /// Returns per-activity allowed/remaining status.
    /// </summary>
    /// <param name="activities">Activities to check.</param>
    /// <param name="log">Whether to log usage (default true).</param>
    /// <returns>Check result with per-activity status.</returns>
    public async Task<CheckResult> CheckAsync(List<ActivityCheck> activities, bool log = true)
    {
        EnsurePaired();
        EnsureChildSelected();

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
            ["userId"] = _credentials!.UserId,
            ["pairId"] = _credentials.PairId,
            ["pairToken"] = _credentials.PairToken,
            ["deviceToken"] = _deviceToken,
            ["tz"] = TimeZoneInfo.Local.Id,
            ["childId"] = CurrentChild!.Id,
            ["activities"] = activityList,
            ["log"] = log
        };

        using var doc = await _api.PostAsync(_api.ApiUrl, "/serviceapi/check", body);
        var root = doc.RootElement;

        var allowed = GetBool(root, "allowed");
        var dayTypeToday = GetString(root, "dayTypeToday") ?? "";
        var dayTypeTomorrow = GetString(root, "dayTypeTomorrow") ?? "";
        var timestamp = GetInt64(root, "timestamp");

        var activityResults = new List<ActivityResult>();
        if (root.TryGetProperty("activities", out var activitiesEl))
        {
            foreach (var prop in activitiesEl.EnumerateObject())
            {
                var actEl = prop.Value;
                activityResults.Add(new ActivityResult(
                    Id: GetUInt64(actEl, "id"),
                    Name: GetString(actEl, "name") ?? prop.Name,
                    Allowed: GetBool(actEl, "allowed"),
                    Banned: GetBool(actEl, "banned"),
                    Remaining: GetInt32(actEl, "remaining"),
                    Expires: GetInt64(actEl, "expires"),
                    TimeblockAllowed: GetBool(actEl, "timeblockAllowed")
                ));
            }
        }

        var result = new CheckResult(
            Allowed: allowed,
            Activities: activityResults,
            DayTypeToday: dayTypeToday,
            DayTypeTomorrow: dayTypeTomorrow,
            Timestamp: timestamp
        );

        // Raise blocked event if overall status is not allowed
        if (!allowed)
        {
            OnBlocked?.Invoke(false, "Activities blocked by Allow2");
        }

        return result;
    }

    // ------------------------------------------------------------------
    // Request More Time
    // ------------------------------------------------------------------

    /// <summary>
    /// Create a "Request More Time" request on behalf of the current child.
    /// </summary>
    /// <param name="activityId">Activity to request more time for.</param>
    /// <param name="minutes">Number of minutes requested.</param>
    /// <param name="message">Optional message to the parent.</param>
    /// <returns>The request ID for polling status.</returns>
    public async Task<ulong> RequestMoreTimeAsync(ulong activityId, int minutes, string? message = null)
    {
        EnsurePaired();
        EnsureChildSelected();

        var body = new Dictionary<string, object>
        {
            ["userId"] = _credentials!.UserId,
            ["pairId"] = _credentials.PairId,
            ["pairToken"] = _credentials.PairToken,
            ["childId"] = CurrentChild!.Id,
            ["duration"] = minutes,
            ["activity"] = activityId
        };

        if (message != null)
        {
            body["message"] = message;
        }

        using var doc = await _api.PostAsync(_api.ApiUrl, "/api/request/createRequest", body);
        return GetUInt64(doc.RootElement, "requestId");
    }

    /// <summary>
    /// Poll the status of a pending "Request More Time" request.
    /// </summary>
    /// <param name="requestId">Request ID from RequestMoreTimeAsync.</param>
    /// <param name="statusSecret">Status secret from the create response.</param>
    /// <returns>The parsed JSON document with status fields.</returns>
    public async Task<JsonDocument> GetRequestStatusAsync(ulong requestId, string statusSecret)
    {
        var headers = new Dictionary<string, string>
        {
            ["X-Status-Secret"] = statusSecret
        };

        return await _api.GetAsync(
            _api.ApiUrl + "/api/request/" + requestId + "/status",
            headers);
    }

    // ------------------------------------------------------------------
    // Feedback
    // ------------------------------------------------------------------

    /// <summary>
    /// Submit feedback from the device to the Allow2 server.
    /// </summary>
    /// <param name="category">Feedback category.</param>
    /// <param name="message">Feedback message text.</param>
    /// <param name="context">Optional device context.</param>
    /// <returns>The discussion ID.</returns>
    public async Task<ulong> SubmitFeedbackAsync(
        FeedbackCategory category,
        string message,
        DeviceContext? context = null)
    {
        EnsurePaired();

        var categoryStr = category switch
        {
            FeedbackCategory.Bug => "not_working",
            FeedbackCategory.Feature => "missing_feature",
            FeedbackCategory.Concern => "bypass",
            FeedbackCategory.Praise => "other",
            FeedbackCategory.Question => "question",
            _ => "other"
        };

        var body = new Dictionary<string, object>
        {
            ["userId"] = _credentials!.UserId,
            ["pairId"] = _credentials.PairId,
            ["pairToken"] = _credentials.PairToken,
            ["category"] = categoryStr,
            ["message"] = message
        };

        if (CurrentChild != null)
        {
            body["childId"] = CurrentChild.Id;
        }

        if (context != null)
        {
            var ctxDict = new Dictionary<string, object>();
            if (context.DeviceName != null) ctxDict["deviceName"] = context.DeviceName;
            if (context.OsVersion != null) ctxDict["osVersion"] = context.OsVersion;
            if (context.AppVersion != null) ctxDict["appVersion"] = context.AppVersion;
            if (context.Locale != null) ctxDict["locale"] = context.Locale;
            body["deviceContext"] = ctxDict;
        }

        using var doc = await _api.PostAsync(_api.ApiUrl, "/api/feedback/submit", body);
        return GetUInt64(doc.RootElement, "discussionId");
    }

    /// <summary>
    /// Load all feedback discussions for this device.
    /// </summary>
    public async Task<List<FeedbackThread>> LoadFeedbackAsync()
    {
        EnsurePaired();

        var body = new Dictionary<string, object>
        {
            ["userId"] = _credentials!.UserId,
            ["pairId"] = _credentials.PairId,
            ["pairToken"] = _credentials.PairToken
        };

        using var doc = await _api.PostAsync(_api.ApiUrl, "/api/feedback/load", body);
        var root = doc.RootElement;

        var threads = new List<FeedbackThread>();
        if (root.TryGetProperty("discussions", out var discussionsEl))
        {
            foreach (var el in discussionsEl.EnumerateArray())
            {
                threads.Add(new FeedbackThread(
                    Id: GetUInt64(el, "id"),
                    Category: GetString(el, "category") ?? "other",
                    Status: GetString(el, "status") ?? "open",
                    CreatedAt: GetInt64(el, "createdAt"),
                    ChildName: GetString(el, "childName"),
                    MessageCount: GetInt32(el, "messageCount")
                ));
            }
        }

        return threads;
    }

    /// <summary>
    /// Reply to an existing feedback discussion.
    /// </summary>
    /// <param name="discussionId">The discussion to reply to.</param>
    /// <param name="message">The reply message.</param>
    /// <returns>The message ID.</returns>
    public async Task<ulong> FeedbackReplyAsync(ulong discussionId, string message)
    {
        EnsurePaired();

        var body = new Dictionary<string, object>
        {
            ["userId"] = _credentials!.UserId,
            ["pairId"] = _credentials.PairId,
            ["pairToken"] = _credentials.PairToken,
            ["discussionId"] = discussionId,
            ["message"] = message
        };

        using var doc = await _api.PostAsync(_api.ApiUrl, "/api/feedback/reply", body);
        return GetUInt64(doc.RootElement, "messageId");
    }

    // ------------------------------------------------------------------
    // Updates
    // ------------------------------------------------------------------

    /// <summary>
    /// Poll for updates (children data, day type changes, etc.).
    /// </summary>
    public async Task<List<Child>> GetUpdatesAsync()
    {
        EnsurePaired();

        var query = $"?userId={_credentials!.UserId}" +
                    $"&pairId={_credentials.PairId}" +
                    $"&pairToken={Uri.EscapeDataString(_credentials.PairToken)}" +
                    $"&deviceToken={Uri.EscapeDataString(_deviceToken)}";

        using var doc = await _api.GetAsync(_api.ApiUrl + "/api/getUpdates" + query);
        Children = ParseChildren(doc.RootElement);

        OnChildrenUpdated?.Invoke(Children);

        return Children;
    }

    // ------------------------------------------------------------------
    // Unpair
    // ------------------------------------------------------------------

    /// <summary>
    /// Clear stored credentials and reset to unpaired state.
    /// </summary>
    public async Task UnpairAsync()
    {
        await _store.ClearAsync();
        _credentials = null;
        IsPaired = false;
        CurrentChild = null;
        Children = new List<Child>();
    }

    // ------------------------------------------------------------------
    // Private helpers
    // ------------------------------------------------------------------

    private void EnsurePaired()
    {
        if (!IsPaired || _credentials == null)
        {
            throw new Allow2Exception(Allow2ErrorCode.NotPaired,
                "Device is not paired. Complete pairing first.");
        }
    }

    private void EnsureChildSelected()
    {
        if (CurrentChild == null)
        {
            throw new Allow2Exception(Allow2ErrorCode.MissingChild,
                "No child selected.");
        }
    }

    private async Task RefreshChildrenAsync()
    {
        try
        {
            await GetUpdatesAsync();
        }
        catch (Allow2Exception ex) when (ex.Code == Allow2ErrorCode.Http401)
        {
            // Credentials invalidated -- device was released
            await UnpairAsync();
            OnCredentialsInvalidated?.Invoke();
        }
        catch
        {
            // Network errors on startup are non-fatal -- we are still paired
        }
    }

    /// <summary>
    /// Hash a PIN with the given salt using SHA-256.
    /// Matches the Brave/Node SDK pattern: SHA256(pin + salt) as lowercase hex.
    /// </summary>
    internal static string HashPin(string pin, string salt)
    {
        var input = Encoding.UTF8.GetBytes(pin + salt);
        var hashBytes = SHA256.HashData(input);

        var sb = new StringBuilder(hashBytes.Length * 2);
        foreach (var b in hashBytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Parse children array from a JSON response element.
    /// </summary>
    private static List<Child> ParseChildren(JsonElement root)
    {
        var children = new List<Child>();
        if (!root.TryGetProperty("children", out var childrenEl))
        {
            return children;
        }

        foreach (var el in childrenEl.EnumerateArray())
        {
            children.Add(new Child(
                Id: GetUInt64(el, "id"),
                Name: GetString(el, "name") ?? "",
                PinHash: GetString(el, "pin_hash"),
                PinSalt: GetString(el, "pin_salt"),
                AvatarUrl: GetString(el, "avatar_url"),
                LinkedAccountId: GetUInt64(el, "LinkedUserId"),
                HasAccount: GetBool(el, "has_account")
            ));
        }

        return children;
    }

    // ------------------------------------------------------------------
    // JSON helpers (safe accessors for JsonElement)
    // ------------------------------------------------------------------

    private static string? GetString(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.String)
        {
            return val.GetString();
        }
        return null;
    }

    private static bool GetBool(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var val))
        {
            if (val.ValueKind == JsonValueKind.True) return true;
            if (val.ValueKind == JsonValueKind.False) return false;
        }
        return false;
    }

    private static ulong GetUInt64(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var val))
        {
            if (val.ValueKind == JsonValueKind.Number && val.TryGetUInt64(out var n)) return n;
            // Some endpoints return numeric IDs as strings
            if (val.ValueKind == JsonValueKind.String && ulong.TryParse(val.GetString(), out var s)) return s;
        }
        return 0;
    }

    private static int GetInt32(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.Number)
        {
            if (val.TryGetInt32(out var n)) return n;
        }
        return 0;
    }

    private static long GetInt64(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.Number)
        {
            if (val.TryGetInt64(out var n)) return n;
        }
        return 0;
    }
}
