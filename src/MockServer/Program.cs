using MockServer.Data;
using MockServer.Models;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Attach / propagate a correlation id on every request so an Appian process
// can trace a call end to end.
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault()
                        ?? Guid.NewGuid().ToString();
    context.Response.Headers["X-Correlation-Id"] = correlationId;
    context.Items["CorrelationId"] = correlationId;
    await next();
});

// --- Health -------------------------------------------------------------
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "appian-integration-mock-server",
    timestampUtc = DateTime.UtcNow
}));

// --- Customer lookup ----------------------------------------------------
app.MapGet("/api/customers/{id}", (string id, HttpContext ctx) =>
{
    var customer = CustomerStore.Find(id);
    return customer is null
        ? Results.NotFound(ApiError.Create(
            code: "CUSTOMER_NOT_FOUND",
            category: "client",
            message: $"No customer found for id '{id}'.",
            correlationId: Correlation(ctx),
            retryable: false))
        : Results.Ok(customer);
});

// --- Payment validation -------------------------------------------------
app.MapPost("/api/payments/validate", (PaymentValidationRequest request, HttpContext ctx) =>
{
    var errors = request.Validate();
    if (errors.Count > 0)
    {
        return Results.BadRequest(ApiError.Create(
            "PAYMENT_VALIDATION_FAILED", "client",
            "One or more payment fields are invalid.",
            Correlation(ctx), retryable: false, details: errors));
    }

    // Fictional business rule: amounts over the threshold need manual review.
    var approved = request.Amount <= 10_000m;
    var response = new PaymentValidationResponse(
        Reference: $"PAY-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(100000, 999999)}",
        Status: approved ? "APPROVED" : "REVIEW_REQUIRED",
        Currency: request.Currency!,
        Amount: request.Amount,
        EvaluatedAtUtc: DateTime.UtcNow);

    return Results.Ok(response);
});

// --- Status callback ----------------------------------------------------
app.MapPost("/api/callbacks/status", (StatusCallbackRequest request, HttpContext ctx) =>
{
    var errors = request.Validate();
    if (errors.Count > 0)
    {
        return Results.BadRequest(ApiError.Create(
            "CALLBACK_VALIDATION_FAILED", "client",
            "Invalid status callback payload.",
            Correlation(ctx), retryable: false, details: errors));
    }

    var acknowledgement = new
    {
        received = true,
        reference = request.Reference,
        acknowledgedStatus = request.Status,
        correlationId = Correlation(ctx),
        timestampUtc = DateTime.UtcNow
    };

    return Results.Accepted($"/api/callbacks/status/{request.Reference}", acknowledgement);
});

app.Run();

static string Correlation(HttpContext ctx) =>
    ctx.Items["CorrelationId"] as string ?? Guid.NewGuid().ToString();
