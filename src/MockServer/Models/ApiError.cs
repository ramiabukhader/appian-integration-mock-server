namespace MockServer.Models;

/// <summary>
/// A single, uniform error envelope used by every endpoint so that an Appian
/// integration can parse failures the same way regardless of which call failed.
/// </summary>
public record ApiError(bool Success, ApiErrorDetail Error)
{
    public static ApiError Create(
        string code,
        string category,
        string message,
        string correlationId,
        bool retryable,
        IReadOnlyList<string>? details = null) =>
        new(false, new ApiErrorDetail(
            code,
            category,
            message,
            correlationId,
            retryable,
            DateTime.UtcNow,
            details ?? Array.Empty<string>()));
}

public record ApiErrorDetail(
    string Code,
    string Category,
    string Message,
    string CorrelationId,
    bool Retryable,
    DateTime TimestampUtc,
    IReadOnlyList<string> Details);
