using MockServer.Models;

namespace MockServer.Data;

/// <summary>
/// Bounded, process-local replay state for deterministic integration tests.
/// This is intentionally not a durable production idempotency implementation.
/// </summary>
public sealed class PaymentIdempotencyStore
{
    public const int DefaultCapacity = 1_000;

    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly Dictionary<string, StoredPayment> _entries = new(StringComparer.Ordinal);
    private readonly Queue<string> _insertionOrder = new();

    public PaymentIdempotencyStore() : this(DefaultCapacity)
    {
    }

    public PaymentIdempotencyStore(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        _capacity = capacity;
    }

    public PaymentIdempotencyResult Execute(
        string key,
        string requestFingerprint,
        Func<PaymentValidationResponse> createResponse)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var stored))
            {
                return string.Equals(stored.RequestFingerprint, requestFingerprint, StringComparison.Ordinal)
                    ? new PaymentIdempotencyResult(stored.Response, Replayed: true, Conflict: false)
                    : new PaymentIdempotencyResult(null, Replayed: false, Conflict: true);
            }

            var response = createResponse();
            _entries.Add(key, new StoredPayment(requestFingerprint, response));
            _insertionOrder.Enqueue(key);
            while (_entries.Count > _capacity)
            {
                var oldest = _insertionOrder.Dequeue();
                _entries.Remove(oldest);
            }
            return new PaymentIdempotencyResult(response, Replayed: false, Conflict: false);
        }
    }

    private sealed record StoredPayment(string RequestFingerprint, PaymentValidationResponse Response);
}

public sealed record PaymentIdempotencyResult(
    PaymentValidationResponse? Response,
    bool Replayed,
    bool Conflict);
