using Microsoft.OpenApi.Models;
using MockServer.Data;
using MockServer.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Appian Integration Mock Server",
        Version = "v1",
        Description = "Fictional endpoints for local Appian integration development and testing."
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Appian Integration Mock Server v1");
    options.RoutePrefix = "swagger";
    options.DocumentTitle = "Appian Integration Mock Server API";
});

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

// Minimal API request binding happens before endpoint handlers. Translate only
// expected client binding failures so every documented client error uses the
// same envelope without masking unexpected application exceptions.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (BadHttpRequestException exception) when (
        exception.StatusCode is StatusCodes.Status400BadRequest or StatusCodes.Status415UnsupportedMediaType)
    {
        await WriteRequestError(context, exception.StatusCode);
        return;
    }

    if (!context.Response.HasStarted &&
        context.Response.StatusCode is StatusCodes.Status400BadRequest or StatusCodes.Status415UnsupportedMediaType)
    {
        await WriteRequestError(context, context.Response.StatusCode);
    }
});

// --- Health -------------------------------------------------------------
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "appian-integration-mock-server",
    timestampUtc = DateTime.UtcNow
}))
.WithName("GetHealth")
.WithSummary("Check service health")
.Produces(StatusCodes.Status200OK);

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
})
.WithName("GetCustomer")
.WithSummary("Look up a fictional customer")
.Produces<Customer>(StatusCodes.Status200OK)
.Produces<ApiError>(StatusCodes.Status404NotFound);

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
})
.WithName("ValidatePayment")
.WithSummary("Validate a fictional payment request")
.Accepts<PaymentValidationRequest>("application/json")
.Produces<PaymentValidationResponse>(StatusCodes.Status200OK)
.Produces<ApiError>(StatusCodes.Status400BadRequest)
.Produces<ApiError>(StatusCodes.Status415UnsupportedMediaType);

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
})
.WithName("AcceptStatusCallback")
.WithSummary("Accept a fictional asynchronous status callback")
.Accepts<StatusCallbackRequest>("application/json")
.Produces(StatusCodes.Status202Accepted)
.Produces<ApiError>(StatusCodes.Status400BadRequest)
.Produces<ApiError>(StatusCodes.Status415UnsupportedMediaType);

app.Run();

static string Correlation(HttpContext ctx) =>
    ctx.Items["CorrelationId"] as string ?? Guid.NewGuid().ToString();

static async Task WriteRequestError(HttpContext context, int statusCode)
{
    var unsupportedMediaType = statusCode == StatusCodes.Status415UnsupportedMediaType;
    var correlationId = Correlation(context);
    var error = ApiError.Create(
        code: unsupportedMediaType ? "UNSUPPORTED_MEDIA_TYPE" : "REQUEST_BODY_INVALID",
        category: "client",
        message: unsupportedMediaType
            ? "Content-Type must be application/json."
            : "Request body is missing or contains malformed JSON.",
        correlationId: correlationId,
        retryable: false);

    context.Response.Clear();
    context.Response.StatusCode = statusCode;
    context.Response.Headers["X-Correlation-Id"] = correlationId;
    await context.Response.WriteAsJsonAsync(error);
}

// Expose the generated entry point to WebApplicationFactory without changing
// the production startup path.
public partial class Program;
