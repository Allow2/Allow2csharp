namespace Allow2;

using System.Text.Json;
using Allow2.Models;

/// <summary>
/// Manages "Request More Time" flows.
///
/// A child can request additional time for an activity. The request is sent to
/// the Allow2 API, and the parent approves or denies from their Allow2 app.
/// This manager handles creating the request and polling for its status.
/// </summary>
internal class Allow2RequestManager
{
    private readonly Allow2Api _api;

    private const int DefaultPollIntervalMs = 5000;
    private const int DefaultTimeoutMs = 300000; // 5 minutes

    internal Allow2RequestManager(Allow2Api api)
    {
        _api = api;
    }

    /// <summary>
    /// Create a request for more time on an activity.
    /// POST /api/request/createRequest (API URL, not service URL).
    /// </summary>
    /// <param name="creds">Stored pairing credentials (userId + pairToken auth)</param>
    /// <param name="deviceToken">Device token from pairing</param>
    /// <param name="childId">Child entity ID requesting more time</param>
    /// <param name="activityId">Activity to request more time for</param>
    /// <param name="minutes">Number of additional minutes requested</param>
    /// <param name="message">Optional message from the child to the parent</param>
    /// <returns>Tuple of request ID and status secret for polling</returns>
    internal async Task<(ulong RequestId, string? StatusSecret)> CreateRequestAsync(
        Allow2StoredCredentials creds,
        string deviceToken,
        ulong childId,
        ulong activityId,
        int minutes,
        string? message)
    {
        var body = new Dictionary<string, object>
        {
            ["userId"] = creds.UserId.ToString(),
            ["pairId"] = creds.PairId.ToString(),
            ["pairToken"] = creds.PairToken,
            ["deviceToken"] = deviceToken,
            ["childId"] = childId.ToString(),
            ["duration"] = minutes,
            ["lift"] = new List<object> { activityId }
        };

        if (!string.IsNullOrEmpty(message))
        {
            body["message"] = message;
        }

        using var response = await _api.PostAsync(_api.ApiUrl, "/api/request/createRequest", body);
        var root = response.RootElement;

        ulong requestId = 0;
        if (root.TryGetProperty("requestId", out var rid))
        {
            requestId = rid.ValueKind == JsonValueKind.Number
                ? rid.GetUInt64()
                : ulong.TryParse(rid.GetString(), out var parsed) ? parsed : 0;
        }

        string? statusSecret = null;
        if (root.TryGetProperty("statusSecret", out var ss))
        {
            statusSecret = ss.GetString();
        }

        return (requestId, statusSecret);
    }

    /// <summary>
    /// Check the status of an existing request.
    /// GET /api/request/{id}/status with X-Status-Secret header.
    /// </summary>
    /// <param name="requestId">Request ID from CreateRequestAsync</param>
    /// <param name="statusSecret">Status secret from CreateRequestAsync</param>
    /// <returns>Status string: "pending", "approved", "denied", or "expired"</returns>
    internal async Task<RequestStatusResult> CheckStatusAsync(ulong requestId, string statusSecret)
    {
        var url = _api.ApiUrl + "/api/request/" + requestId + "/status";
        var headers = new Dictionary<string, string>
        {
            ["X-Status-Secret"] = statusSecret
        };

        using var response = await _api.GetAsync(url, headers);
        var root = response.RootElement;

        var status = root.TryGetProperty("status", out var st)
            ? st.GetString() ?? "pending"
            : "pending";

        int extensionMinutes = 0;
        if (root.TryGetProperty("extension", out var ext) && ext.ValueKind == JsonValueKind.Number)
        {
            extensionMinutes = ext.GetInt32();
        }

        return new RequestStatusResult(status, extensionMinutes);
    }

    /// <summary>
    /// Submit a request and poll until the parent responds or the timeout expires.
    /// Invokes callbacks as the status changes.
    /// </summary>
    /// <param name="creds">Stored pairing credentials</param>
    /// <param name="deviceToken">Device token</param>
    /// <param name="childId">Child entity ID</param>
    /// <param name="activityId">Activity ID</param>
    /// <param name="minutes">Minutes requested</param>
    /// <param name="message">Optional message</param>
    /// <param name="onStatusChanged">Callback invoked when status changes</param>
    /// <param name="pollIntervalMs">Milliseconds between polls (default 5000)</param>
    /// <param name="timeoutMs">Maximum wait time in milliseconds (default 300000)</param>
    /// <param name="cancellationToken">Cancellation token to abort polling</param>
    /// <returns>Final request status result</returns>
    internal async Task<RequestStatusResult> CreateAndPollAsync(
        Allow2StoredCredentials creds,
        string deviceToken,
        ulong childId,
        ulong activityId,
        int minutes,
        string? message,
        Action<string>? onStatusChanged = null,
        int pollIntervalMs = DefaultPollIntervalMs,
        int timeoutMs = DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var (requestId, statusSecret) = await CreateRequestAsync(
            creds, deviceToken, childId, activityId, minutes, message);

        if (requestId == 0)
        {
            throw new Allow2Exception(Allow2ErrorCode.ApiError, "Failed to create request");
        }

        if (string.IsNullOrEmpty(statusSecret))
        {
            throw new Allow2Exception(Allow2ErrorCode.ApiError,
                "No status secret returned for request " + requestId);
        }

        onStatusChanged?.Invoke("pending");

        return await PollStatusAsync(
            requestId, statusSecret, onStatusChanged,
            pollIntervalMs, timeoutMs, cancellationToken);
    }

    /// <summary>
    /// Poll an existing request until it resolves or times out.
    /// </summary>
    internal async Task<RequestStatusResult> PollStatusAsync(
        ulong requestId,
        string statusSecret,
        Action<string>? onStatusChanged = null,
        int pollIntervalMs = DefaultPollIntervalMs,
        int timeoutMs = DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Delay(pollIntervalMs, cancellationToken);

            try
            {
                var result = await CheckStatusAsync(requestId, statusSecret);

                if (result.Status != "pending")
                {
                    onStatusChanged?.Invoke(result.Status);
                    return result;
                }
            }
            catch (Allow2Exception ex) when (ex.Code == Allow2ErrorCode.NetworkError)
            {
                // Transient network error -- keep polling
            }
        }

        onStatusChanged?.Invoke("expired");
        return new RequestStatusResult("expired", 0);
    }
}

/// <summary>
/// Result of a request status check.
/// </summary>
public record RequestStatusResult(
    /// <summary>"pending", "approved", "denied", or "expired"</summary>
    string Status,
    /// <summary>Additional minutes granted (only populated when approved)</summary>
    int ExtensionMinutes
);
