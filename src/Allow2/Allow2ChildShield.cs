namespace Allow2;

using System.Security.Cryptography;
using System.Text;
using Allow2.Models;

/// <summary>
/// Child identification, PIN verification, and lockout management.
/// Port of the Brave browser's ChildShield pattern for .NET.
///
/// Manages which child is currently using the device with PIN-based
/// verification, progressive lockout on failed attempts, and constant-time
/// comparison to prevent timing attacks.
/// </summary>
public class Allow2ChildShield
{
    private readonly Dictionary<ulong, int> _failedAttempts = new();
    private readonly Dictionary<ulong, DateTime> _lockoutUntil = new();

    /// <summary>
    /// Current child ID, or null if no child is selected.
    /// </summary>
    public ulong? CurrentChildId { get; private set; }

    /// <summary>
    /// True if the device is in parent (unrestricted) mode.
    /// </summary>
    public bool IsParentMode { get; private set; }

    /// <summary>
    /// SHA-256(pin + salt) as lowercase hex string.
    /// Must produce identical output to the Node SDK and Brave browser implementations.
    /// </summary>
    /// <param name="pin">The raw PIN string</param>
    /// <param name="salt">The hex-encoded salt</param>
    /// <returns>Lowercase hex-encoded SHA-256 hash</returns>
    public static string HashPin(string pin, string salt)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(pin + salt));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Verify a PIN against its stored hash and salt using constant-time comparison.
    /// Returns true if no PIN is configured (empty hash).
    /// </summary>
    /// <param name="pin">The raw PIN to verify</param>
    /// <param name="hash">The stored hex-encoded hash</param>
    /// <param name="salt">The hex-encoded salt</param>
    /// <returns>True if the PIN matches or no PIN is set</returns>
    public static bool VerifyPin(string pin, string hash, string salt)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return true; // No PIN configured — honour system
        }

        var computed = HashPin(pin, salt);
        return ConstantTimeEquals(computed, hash);
    }

    /// <summary>
    /// Constant-time string comparison to prevent timing side-channel attacks.
    /// </summary>
    private static bool ConstantTimeEquals(string a, string b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        int result = 0;
        for (int i = 0; i < a.Length; i++)
        {
            result |= a[i] ^ b[i];
        }

        return result == 0;
    }

    /// <summary>
    /// Check whether a child is currently locked out due to failed PIN attempts.
    /// </summary>
    /// <param name="childId">The child's ID</param>
    /// <returns>Tuple of (isLocked, remainingSeconds)</returns>
    public (bool Locked, int RemainingSeconds) IsLockedOut(ulong childId)
    {
        if (_lockoutUntil.TryGetValue(childId, out var until) && until > DateTime.UtcNow)
        {
            return (true, (int)(until - DateTime.UtcNow).TotalSeconds);
        }

        // Lockout expired -- clear state
        if (_lockoutUntil.ContainsKey(childId))
        {
            _lockoutUntil.Remove(childId);
            _failedAttempts.Remove(childId);
        }

        return (false, 0);
    }

    /// <summary>
    /// Record a failed PIN attempt and apply progressive lockout.
    /// Thresholds match the Node SDK: 3->30s, 6->60s, 9->300s, 12+->900s.
    /// </summary>
    /// <param name="childId">The child's ID</param>
    /// <returns>Lockout duration in seconds (0 if not yet locked)</returns>
    public int RecordFailedAttempt(ulong childId)
    {
        _failedAttempts.TryGetValue(childId, out var count);
        count++;
        _failedAttempts[childId] = count;

        int lockoutSeconds = count switch
        {
            >= 12 => 900,
            >= 9 => 300,
            >= 6 => 60,
            >= 3 => 30,
            _ => 0
        };

        if (lockoutSeconds > 0)
        {
            _lockoutUntil[childId] = DateTime.UtcNow.AddSeconds(lockoutSeconds);
        }

        return lockoutSeconds;
    }

    /// <summary>
    /// Clear lockout state for a child after a successful PIN entry.
    /// </summary>
    /// <param name="childId">The child's ID</param>
    public void ClearLockout(ulong childId)
    {
        _failedAttempts.Remove(childId);
        _lockoutUntil.Remove(childId);
    }

    /// <summary>
    /// Select a child by verifying their PIN. Handles lockout checking,
    /// PIN verification, and lockout recording on failure.
    /// </summary>
    /// <param name="childId">The child's ID to select</param>
    /// <param name="pin">The raw PIN entered by the child</param>
    /// <param name="children">The current list of children on this account</param>
    /// <returns>True if the child was successfully selected</returns>
    /// <exception cref="Allow2Exception">Thrown if child not found or locked out</exception>
    public bool SelectChild(ulong childId, string pin, IReadOnlyList<Child> children)
    {
        var child = children.FirstOrDefault(c => c.Id == childId);
        if (child == null)
        {
            throw new Allow2Exception(
                Allow2ErrorCode.MissingChild,
                "Child not found: " + childId);
        }

        var (locked, remaining) = IsLockedOut(childId);
        if (locked)
        {
            throw new Allow2Exception(
                Allow2ErrorCode.PinLocked,
                "Locked for " + remaining + " seconds due to too many failed attempts");
        }

        if (!VerifyPin(pin, child.PinHash ?? "", child.PinSalt ?? ""))
        {
            var lockoutSecs = RecordFailedAttempt(childId);
            return false;
        }

        ClearLockout(childId);
        CurrentChildId = childId;
        IsParentMode = false;
        return true;
    }

    /// <summary>
    /// Enter parent mode (unrestricted access). Clears any child selection.
    /// </summary>
    public void EnterParentMode()
    {
        CurrentChildId = null;
        IsParentMode = true;
    }

    /// <summary>
    /// Clear the current child selection and parent mode.
    /// Device should return to child selector screen.
    /// </summary>
    public void ClearSelection()
    {
        CurrentChildId = null;
        IsParentMode = false;
    }

    /// <summary>
    /// Return a filtered list of children suitable for display in a child selector.
    /// PIN hashes and salts are excluded for safety.
    /// </summary>
    /// <param name="children">The full children list</param>
    /// <returns>Display-safe child records with PIN data removed</returns>
    public static IReadOnlyList<Child> GetDisplayChildren(IReadOnlyList<Child> children)
    {
        return children.Select(c => c with { PinHash = null, PinSalt = null }).ToList();
    }
}
