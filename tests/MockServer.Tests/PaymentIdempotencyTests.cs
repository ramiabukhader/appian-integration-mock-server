using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using MockServer.Data;
using MockServer.Models;
using Xunit;

namespace MockServer.Tests;

public sealed class PaymentIdempotencyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public PaymentIdempotencyTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task IdenticalReplayReturnsTheOriginalResponse()
    {
        const string key = "payment-replay-0001";
        using var first = await SendPayment(key, amount: 250m);
        using var second = await SendPayment(key, amount: 250m);
        var firstBody = await first.Content.ReadFromJsonAsync<PaymentValidationResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<PaymentValidationResponse>();

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("false", first.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal("true", second.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(firstBody, secondBody);
    }

    [Fact]
    public async Task NormalizedEquivalentRequestReplaysButDifferentRequestConflicts()
    {
        const string key = "payment-conflict-0001";
        using var first = await SendPayment(key, 250m, " gbp ", " account-a ", "account-b");
        using var equivalent = await SendPayment(key, 250m, "GBP", "account-a", "account-b");
        using var conflict = await SendPayment(key, 251m, "GBP", "account-a", "account-b", "conflict-correlation");
        using var conflictBody = await JsonDocument.ParseAsync(await conflict.Content.ReadAsStreamAsync());

        Assert.Equal("true", equivalent.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var error = conflictBody.RootElement.GetProperty("error");
        Assert.Equal("IDEMPOTENCY_KEY_CONFLICT", error.GetProperty("code").GetString());
        Assert.Equal("conflict-correlation", error.GetProperty("correlationId").GetString());
        Assert.DoesNotContain("account", error.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("contains space")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task InvalidKeysUseTheSharedBadRequestContract(string key)
    {
        using var response = await SendPayment(key, 250m);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_INVALID", body.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task MissingKeyPreservesUnkeyedBehavior()
    {
        using var first = await SendPayment(null, 250m);
        using var second = await SendPayment(null, 250m);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.False(first.Headers.Contains("Idempotency-Replayed"));
        Assert.False(second.Headers.Contains("Idempotency-Replayed"));
    }

    [Fact]
    public async Task ConcurrentIdenticalRequestsCreateExactlyOneResponse()
    {
        const string key = "payment-concurrent-0001";
        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => SendPayment(key, 500m)));
        try
        {
            var bodies = await Task.WhenAll(responses.Select(response => response.Content.ReadFromJsonAsync<PaymentValidationResponse>()));
            Assert.Single(bodies.Distinct());
            Assert.Equal(1, responses.Count(response => response.Headers.GetValues("Idempotency-Replayed").Single() == "false"));
            Assert.Equal(19, responses.Count(response => response.Headers.GetValues("Idempotency-Replayed").Single() == "true"));
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    [Fact]
    public void StoreEvictsOldestEntryAtCapacity()
    {
        var store = new PaymentIdempotencyStore(capacity: 2);
        var created = 0;
        PaymentValidationResponse Create() => new($"P-{++created}", "APPROVED", "GBP", 1m, DateTime.UnixEpoch);

        store.Execute("key-0001", "fingerprint-1", Create);
        store.Execute("key-0002", "fingerprint-2", Create);
        store.Execute("key-0003", "fingerprint-3", Create);
        var reusedEvictedKey = store.Execute("key-0001", "new-fingerprint", Create);

        Assert.False(reusedEvictedKey.Conflict);
        Assert.False(reusedEvictedKey.Replayed);
        Assert.Equal(4, created);
    }

    [Fact]
    public async Task OpenApiDocumentsIdempotencyHeaderAndConflict()
    {
        using var response = await _client.GetAsync("/swagger/v1/swagger.json");
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        response.EnsureSuccessStatusCode();

        var operation = document.RootElement.GetProperty("paths")
            .GetProperty("/api/payments/validate").GetProperty("post");
        Assert.Contains(operation.GetProperty("parameters").EnumerateArray(), parameter =>
            parameter.GetProperty("name").GetString() == "Idempotency-Key" &&
            parameter.GetProperty("in").GetString() == "header");
        Assert.True(operation.GetProperty("responses").TryGetProperty("409", out _));
    }

    private async Task<HttpResponseMessage> SendPayment(
        string? key,
        decimal amount,
        string currency = "GBP",
        string debtorAccount = "GB00TEST00000000000001",
        string creditorAccount = "GB00TEST00000000000002",
        string? correlationId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/validate")
        {
            Content = JsonContent.Create(new { currency, amount, debtorAccount, creditorAccount })
        };
        if (key is not null)
            request.Headers.Add("Idempotency-Key", key);
        if (correlationId is not null)
            request.Headers.Add("X-Correlation-Id", correlationId);
        return await _client.SendAsync(request);
    }
}
