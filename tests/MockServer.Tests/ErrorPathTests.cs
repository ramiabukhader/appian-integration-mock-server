using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MockServer.Tests;

public sealed class ErrorPathTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ErrorPathTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task MissingCustomerReturnsStructuredNonRetryableError()
    {
        const string correlationId = "test-customer-not-found";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/customers/UNKNOWN");
        request.Headers.Add("X-Correlation-Id", correlationId);

        using var response = await _client.SendAsync(request);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(correlationId, response.Headers.GetValues("X-Correlation-Id").Single());
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        var error = body.RootElement.GetProperty("error");
        Assert.Equal("CUSTOMER_NOT_FOUND", error.GetProperty("code").GetString());
        Assert.Equal("client", error.GetProperty("category").GetString());
        Assert.Equal(correlationId, error.GetProperty("correlationId").GetString());
        Assert.False(error.GetProperty("retryable").GetBoolean());
    }

    [Fact]
    public async Task InvalidPaymentReturnsEveryValidationDetail()
    {
        const string correlationId = "test-invalid-payment";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/validate")
        {
            Content = JsonContent.Create(new
            {
                currency = "POUND",
                amount = -5,
                debtorAccount = "",
                creditorAccount = ""
            })
        };
        request.Headers.Add("X-Correlation-Id", correlationId);

        using var response = await _client.SendAsync(request);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(correlationId, response.Headers.GetValues("X-Correlation-Id").Single());
        var error = body.RootElement.GetProperty("error");
        Assert.Equal("PAYMENT_VALIDATION_FAILED", error.GetProperty("code").GetString());
        Assert.Equal(correlationId, error.GetProperty("correlationId").GetString());
        Assert.Equal(4, error.GetProperty("details").GetArrayLength());
    }

    [Fact]
    public async Task InvalidCallbackReturnsTheSharedErrorContract()
    {
        using var response = await _client.PostAsJsonAsync("/api/callbacks/status", new
        {
            reference = "",
            status = "UNKNOWN",
            detail = "synthetic test fixture"
        });
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = body.RootElement.GetProperty("error");
        Assert.Equal("CALLBACK_VALIDATION_FAILED", error.GetProperty("code").GetString());
        Assert.False(error.GetProperty("retryable").GetBoolean());
        Assert.Equal(2, error.GetProperty("details").GetArrayLength());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("correlationId").GetString()));
    }
}
