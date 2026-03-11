namespace Allow2;

using System.Text.Json;
using Allow2.Models;

/// <summary>
/// Manages feedback submissions between device users and developers.
///
/// Feedback uses "feedbackDeviceAuth" on the server: userId + pairToken in the
/// POST body (NOT deviceToken). This allows both parents and children with
/// linked accounts to submit feedback.
/// </summary>
internal class Allow2FeedbackManager
{
    private readonly Allow2Api _api;

    internal Allow2FeedbackManager(Allow2Api api)
    {
        _api = api;
    }

    /// <summary>
    /// Convert a FeedbackCategory enum to the server-expected string value.
    /// </summary>
    private static string CategoryToString(FeedbackCategory category)
    {
        return category switch
        {
            FeedbackCategory.Bug => "not_working",
            FeedbackCategory.Feature => "missing_feature",
            FeedbackCategory.Concern => "bypass",
            FeedbackCategory.Praise => "other",
            FeedbackCategory.Question => "question",
            _ => "other"
        };
    }

    /// <summary>
    /// Submit feedback to the Allow2 server.
    /// POST /api/feedback/submit
    /// Auth: feedbackDeviceAuth (userId + pairToken in body).
    /// </summary>
    /// <param name="creds">Stored pairing credentials (userId + pairToken)</param>
    /// <param name="vid">Version ID for this integration</param>
    /// <param name="childId">Optional child ID if submitted on behalf of a child</param>
    /// <param name="category">Feedback category</param>
    /// <param name="message">Feedback message text</param>
    /// <param name="deviceContext">Optional device context metadata</param>
    /// <returns>Discussion ID for the created feedback thread</returns>
    internal async Task<ulong> SubmitAsync(
        Allow2StoredCredentials creds,
        ulong vid,
        ulong? childId,
        FeedbackCategory category,
        string message,
        DeviceContext? deviceContext)
    {
        var body = new Dictionary<string, object>
        {
            ["userId"] = creds.UserId.ToString(),
            ["pairToken"] = creds.PairToken,
            ["vid"] = vid.ToString(),
            ["category"] = CategoryToString(category),
            ["message"] = message
        };

        if (childId.HasValue)
        {
            body["childId"] = childId.Value.ToString();
        }

        if (deviceContext != null)
        {
            var ctx = new Dictionary<string, object?>();
            if (deviceContext.DeviceName != null) ctx["deviceName"] = deviceContext.DeviceName;
            if (deviceContext.OsVersion != null) ctx["osVersion"] = deviceContext.OsVersion;
            if (deviceContext.AppVersion != null) ctx["appVersion"] = deviceContext.AppVersion;
            if (deviceContext.Locale != null) ctx["locale"] = deviceContext.Locale;
            body["deviceContext"] = ctx;
        }

        using var response = await _api.PostAsync(_api.ApiUrl, "/api/feedback/submit", body);
        var root = response.RootElement;

        if (root.TryGetProperty("discussionId", out var did))
        {
            return did.ValueKind == JsonValueKind.Number
                ? did.GetUInt64()
                : ulong.TryParse(did.GetString(), out var parsed) ? parsed : 0;
        }

        return 0;
    }

    /// <summary>
    /// Load all feedback discussion threads for this device/account.
    /// POST /api/feedback/load
    /// Auth: feedbackDeviceAuth (userId + pairToken in body).
    /// </summary>
    /// <param name="creds">Stored pairing credentials</param>
    /// <returns>List of feedback discussion threads</returns>
    internal async Task<List<FeedbackThread>> LoadAsync(Allow2StoredCredentials creds)
    {
        var body = new Dictionary<string, object>
        {
            ["userId"] = creds.UserId.ToString(),
            ["pairToken"] = creds.PairToken
        };

        using var response = await _api.PostAsync(_api.ApiUrl, "/api/feedback/load", body);
        var root = response.RootElement;
        var threads = new List<FeedbackThread>();

        if (root.TryGetProperty("discussions", out var discussions) &&
            discussions.ValueKind == JsonValueKind.Array)
        {
            foreach (var d in discussions.EnumerateArray())
            {
                threads.Add(ParseThread(d));
            }
        }

        return threads;
    }

    /// <summary>
    /// Reply to an existing feedback discussion thread.
    /// POST /api/feedback/reply
    /// Auth: feedbackDeviceAuth (userId + pairToken in body).
    /// </summary>
    /// <param name="creds">Stored pairing credentials</param>
    /// <param name="discussionId">Discussion thread ID to reply to</param>
    /// <param name="message">Reply message text</param>
    /// <returns>Message ID of the created reply</returns>
    internal async Task<ulong> ReplyAsync(
        Allow2StoredCredentials creds,
        ulong discussionId,
        string message)
    {
        var body = new Dictionary<string, object>
        {
            ["userId"] = creds.UserId.ToString(),
            ["pairToken"] = creds.PairToken,
            ["discussionId"] = discussionId.ToString(),
            ["message"] = message
        };

        using var response = await _api.PostAsync(_api.ApiUrl, "/api/feedback/reply", body);
        var root = response.RootElement;

        if (root.TryGetProperty("messageId", out var mid))
        {
            return mid.ValueKind == JsonValueKind.Number
                ? mid.GetUInt64()
                : ulong.TryParse(mid.GetString(), out var parsed) ? parsed : 0;
        }

        return 0;
    }

    /// <summary>
    /// Parse a JSON element into a FeedbackThread record.
    /// </summary>
    private static FeedbackThread ParseThread(JsonElement element)
    {
        return new FeedbackThread(
            Id: element.GetProperty("id").GetUInt64(),
            Category: element.TryGetProperty("category", out var cat)
                ? cat.GetString() ?? ""
                : "",
            Status: element.TryGetProperty("status", out var st)
                ? st.GetString() ?? "open"
                : "open",
            CreatedAt: element.TryGetProperty("createdAt", out var ca)
                ? ca.ValueKind == JsonValueKind.Number ? ca.GetInt64() : 0
                : 0,
            ChildName: element.TryGetProperty("childName", out var cn)
                ? cn.GetString()
                : null,
            MessageCount: element.TryGetProperty("messageCount", out var mc)
                ? mc.ValueKind == JsonValueKind.Number ? mc.GetInt32() : 0
                : 0
        );
    }
}
