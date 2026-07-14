using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace MockServer.OpenApi;

public sealed class PaymentIdempotencyOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!string.Equals(context.ApiDescription.RelativePath, "api/payments/validate", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(context.ApiDescription.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        operation.Parameters ??= new List<OpenApiParameter>();
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "Idempotency-Key",
            In = ParameterLocation.Header,
            Required = false,
            Description = "Optional 8-128 character replay key. Reuse with different payment data returns 409.",
            Schema = new OpenApiSchema { Type = "string", MinLength = 8, MaxLength = 128 }
        });
    }
}
