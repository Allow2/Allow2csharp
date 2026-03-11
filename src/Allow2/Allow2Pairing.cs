namespace Allow2;

using System.Text.Json;
using Allow2.Models;

/// <summary>
/// Manages one-time device pairing with the Allow2 platform.
/// Parents NEVER enter credentials on the child's device.
///
/// Flow:
/// 1. InitPairingAsync registers a pairing session (PIN or QR)
/// 2. API returns a server-assigned PIN/QR and session ID
/// 3. Device displays PIN/QR to the user
/// 4. Parent opens Allow2 app on their phone, enters PIN or scans QR
/// 5. CheckStatusAsync polls until parent confirms
/// 6. On confirmation, returns PairingStatus with credentials
/// </summary>
internal class Allow2PairingManager
{
    private readonly Allow2Api _api;
    private readonly string _deviceToken;

    internal Allow2PairingManager(Allow2Api api, string deviceToken)
    {
        _api = api;
        _deviceToken = deviceToken;
    }

    /// <summary>
    /// Initiate a pairing session. The device displays the returned PIN or QR data
    /// and the parent confirms from their Allow2 app.
    /// </summary>
    /// <param name="deviceName">Human-readable device name (e.g., "Emma's PC")</param>
    /// <param name="method">QR code or PIN code pairing</param>
    /// <param name="uuid">Persistent unique device identifier</param>
    /// <returns>A PairingSession containing session ID and PIN/QR data</returns>
    internal async Task<PairingSession> InitPairingAsync(
        string deviceName,
        PairingMethod method,
        string uuid)
    {
        var endpoint = method == PairingMethod.QRCode
            ? "/api/pair/qr/init"
            : "/api/pair/pin/init";

        var body = new Dictionary<string, object>
        {
            ["deviceToken"] = _deviceToken,
            ["name"] = deviceName,
            ["uuid"] = uuid,
            ["method"] = method == PairingMethod.QRCode ? "qr" : "pin"
        };

        using var response = await _api.PostAsync(_api.ApiUrl, endpoint, body);
        var root = response.RootElement;

        return new PairingSession(
            SessionId: root.GetProperty("sessionId").GetString()!,
            QrCodeData: root.TryGetProperty("qrCodeData", out var qr) ? qr.GetString() : null,
            WebPairingUrl: root.TryGetProperty("webPairingUrl", out var url) ? url.GetString() : null,
            PinCode: root.TryGetProperty("pinCode", out var pin) ? pin.GetString()
                   : root.TryGetProperty("pin", out var pinAlt) ? pinAlt.GetString()
                   : null
        );
    }

    /// <summary>
    /// Poll the Allow2 API for pairing confirmation. Returns the pairing result
    /// once the parent has confirmed on their device.
    /// </summary>
    /// <param name="sessionId">Session ID from InitPairingAsync</param>
    /// <returns>PairingStatus with credentials and children list</returns>
    internal async Task<PairingStatus> CheckStatusAsync(string sessionId)
    {
        using var response = await _api.GetAsync(
            _api.ApiUrl + "/api/pair/status/" + sessionId);

        var root = response.RootElement;

        var completed = root.TryGetProperty("paired", out var pairedProp) && pairedProp.GetBoolean();
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

        var userId = root.GetProperty("userId").GetUInt64();
        var pairId = root.GetProperty("pairId").GetUInt64();
        var pairToken = root.GetProperty("pairToken").GetString()!;

        var children = new List<Child>();
        if (root.TryGetProperty("children", out var childArray) &&
            childArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in childArray.EnumerateArray())
            {
                children.Add(new Child(
                    Id: child.GetProperty("id").GetUInt64(),
                    Name: child.GetProperty("name").GetString()!,
                    PinHash: child.TryGetProperty("pin", out var ph) ? ph.GetString() : null,
                    PinSalt: child.TryGetProperty("pinSalt", out var ps) ? ps.GetString() : null,
                    AvatarUrl: child.TryGetProperty("avatarUrl", out var av) ? av.GetString() : null,
                    LinkedAccountId: child.TryGetProperty("linkedAccountId", out var la)
                        ? la.GetUInt64() : 0,
                    HasAccount: child.TryGetProperty("hasAccount", out var ha) && ha.GetBoolean()
                ));
            }
        }

        return new PairingStatus(
            Completed: true,
            UserId: userId,
            PairId: pairId,
            PairToken: pairToken,
            Children: children
        );
    }

    /// <summary>
    /// Poll repeatedly until pairing completes or the timeout expires.
    /// </summary>
    /// <param name="sessionId">Session ID from InitPairingAsync</param>
    /// <param name="pollIntervalMs">Milliseconds between polls (default 5000)</param>
    /// <param name="timeoutMs">Maximum wait time in milliseconds (default 1800000 = 30 min)</param>
    /// <param name="cancellationToken">Cancellation token to abort waiting</param>
    /// <returns>PairingStatus with credentials once confirmed</returns>
    internal async Task<PairingStatus> WaitForPairingAsync(
        string sessionId,
        int pollIntervalMs = 5000,
        int timeoutMs = 1800000,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var status = await CheckStatusAsync(sessionId);
            if (status.Completed)
            {
                return status;
            }

            await Task.Delay(pollIntervalMs, cancellationToken);
        }

        throw new Allow2Exception(
            Allow2ErrorCode.Timeout,
            "Pairing timed out after " + (timeoutMs / 1000) + " seconds");
    }
}
