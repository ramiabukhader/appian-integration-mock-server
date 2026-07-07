using MockServer.Models;

namespace MockServer.Data;

/// <summary>
/// In-memory, read-only set of fictional customers. Names are famous computer
/// scientists purely so the sample data is obviously not real.
/// </summary>
public static class CustomerStore
{
    private static readonly IReadOnlyList<Customer> Customers = new List<Customer>
    {
        new("CUST-1001", "Ada Lovelace", "RETAIL",  "ACTIVE",  "GB"),
        new("CUST-1002", "Alan Turing",  "PREMIER", "ACTIVE",  "GB"),
        new("CUST-1003", "Grace Hopper", "RETAIL",  "DORMANT", "US"),
        new("CUST-1004", "Katherine Johnson", "PREMIER", "ACTIVE", "US"),
    };

    public static Customer? Find(string id) =>
        Customers.FirstOrDefault(c =>
            string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
}
