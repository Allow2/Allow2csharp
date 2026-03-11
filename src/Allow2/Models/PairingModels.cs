namespace Allow2.Models;

/// <summary>
/// Method used for device pairing. Parent scans QR or enters PIN on their phone.
/// The device NEVER handles parent credentials.
/// </summary>
public enum PairingMethod
{
    QRCode,
    PINCode
}

/// <summary>
/// Represents an active pairing session awaiting parent confirmation.
/// </summary>
public record PairingSession(
    string SessionId,
    string? QrCodeData,
    string? WebPairingUrl,
    string? PinCode
);

/// <summary>
/// Result returned when pairing completes (parent confirmed on their device).
/// </summary>
public record PairingStatus(
    bool Completed,
    ulong UserId,
    ulong PairId,
    string PairToken,
    List<Child> Children
);
