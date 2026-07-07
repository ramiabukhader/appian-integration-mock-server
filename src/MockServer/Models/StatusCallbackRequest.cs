namespace MockServer.Models;

/// <summary>
/// Request body for POST /api/callbacks/status — the shape an external system
/// might use to notify an Appian process that work has progressed.
/// </summary>
public record StatusCallbackRequest(
    string? Reference,
    string? Status,
    string? Detail)
{
    private static readonly HashSet<string> AllowedStatuses =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "RECEIVED", "PROCESSING", "COMPLETED", "FAILED"
        };

    public List<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Reference))
            errors.Add("Reference is required.");

        if (string.IsNullOrWhiteSpace(Status) || !AllowedStatuses.Contains(Status!))
            errors.Add($"Status must be one of: {string.Join(", ", AllowedStatuses)}.");

        return errors;
    }
}
