namespace MockServer.Models;

/// <summary>
/// Request body for POST /api/payments/validate. All account values are
/// fictional; do not send real financial data to this mock.
/// </summary>
public record PaymentValidationRequest(
    string? Currency,
    decimal Amount,
    string? DebtorAccount,
    string? CreditorAccount)
{
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Currency) || Currency!.Trim().Length != 3)
            errors.Add("Currency must be a 3-letter ISO 4217 code.");

        if (Amount <= 0)
            errors.Add("Amount must be greater than zero.");

        if (string.IsNullOrWhiteSpace(DebtorAccount))
            errors.Add("DebtorAccount is required.");

        if (string.IsNullOrWhiteSpace(CreditorAccount))
            errors.Add("CreditorAccount is required.");

        return errors;
    }
}

public record PaymentValidationResponse(
    string Reference,
    string Status,
    string Currency,
    decimal Amount,
    DateTime EvaluatedAtUtc);
