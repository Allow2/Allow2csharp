namespace Allow2.Models;

/// <summary>
/// Error codes for Allow2 SDK operations.
/// </summary>
public enum Allow2ErrorCode
{
    NotPaired,
    AlreadyPaired,
    MissingChild,
    NotAuthorised,
    InvalidResponse,
    NetworkError,
    ApiError,
    PinLocked,
    Timeout,
    Http401
}

/// <summary>
/// Exception thrown by Allow2 SDK operations.
/// </summary>
public class Allow2Exception : Exception
{
    public Allow2ErrorCode Code { get; }

    public Allow2Exception(Allow2ErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public Allow2Exception(Allow2ErrorCode code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }
}
