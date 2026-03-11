namespace Allow2.Models;

/// <summary>
/// Represents a child entity within a controller's Allow2 account.
/// A child is NOT a user account -- it is a row in the controller's account.
/// The child may optionally have a linked user account (LinkedAccountId).
/// </summary>
public record Child(
    ulong Id,
    string Name,
    string? PinHash = null,
    string? PinSalt = null,
    string? AvatarUrl = null,
    ulong LinkedAccountId = 0,
    bool HasAccount = false
);
