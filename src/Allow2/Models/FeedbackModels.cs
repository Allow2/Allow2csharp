namespace Allow2.Models;

/// <summary>
/// Feedback categories accepted by the Allow2 server.
/// Maps to: bypass, missing_feature, not_working, question, other.
/// </summary>
public enum FeedbackCategory
{
    Bug,
    Feature,
    Concern,
    Praise,
    Question
}

/// <summary>
/// A feedback discussion thread between a parent/child and the developer.
/// </summary>
public record FeedbackThread(
    ulong Id,
    string Category,
    string Status,
    long CreatedAt,
    string? ChildName,
    int MessageCount
);

/// <summary>
/// A single message within a feedback discussion thread.
/// </summary>
public record FeedbackMessage(
    ulong Id,
    string Text,
    string SenderType,
    long CreatedAt
);

/// <summary>
/// Optional device context sent with feedback submissions.
/// </summary>
public record DeviceContext(
    string? DeviceName,
    string? OsVersion,
    string? AppVersion,
    string? Locale
);
