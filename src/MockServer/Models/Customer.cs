namespace MockServer.Models;

/// <summary>
/// A fictional customer record returned by the mock customer-lookup endpoint.
/// No real customer data is used anywhere in this project.
/// </summary>
public record Customer(
    string Id,
    string FullName,
    string Segment,
    string Status,
    string Country);
