# Appian Integration Mock Server

A small **.NET 8 minimal API** that stands in for the external systems an
[Appian](https://appian.com) application talks to during development and
testing. Point your Appian integrations at this server to exercise happy-path
and failure-path behaviour without depending on real downstream systems.

> **Disclaimer**
> This project uses **only fictional data**. It contains no real financial
> data, no real bank names, no customer information, and no proprietary code.
> It is intended for local development and testing only.

## Problem

Integration development against real enterprise systems is slow and risky: test
environments are shared, rate-limited, or simply unavailable, and you often
can't reproduce the failure modes you most need to handle. A lightweight,
predictable mock lets you build and test integration error handling before the
real endpoint is ready.

## Scope

Three representative endpoints plus a health probe, all returning a **uniform,
structured response contract**:

| Method | Route                     | Purpose                                   |
|--------|---------------------------|-------------------------------------------|
| GET    | `/health`                 | Liveness/readiness probe                  |
| GET    | `/api/customers/{id}`     | Customer lookup (fictional data)          |
| POST   | `/api/payments/validate`  | Payment validation with a sample rule     |
| POST   | `/api/callbacks/status`   | Accept an async status callback           |

Every request is tagged with an `X-Correlation-Id` (generated if absent and
echoed back) so calls can be traced end to end.

Malformed or missing JSON bodies and unsupported request content types use the
same `ApiError` envelope as business validation failures. These framework-level
failures return stable `REQUEST_BODY_INVALID` (400) or `UNSUPPORTED_MEDIA_TYPE`
(415) codes without echoing the submitted body or parser details.

## Architecture

```mermaid
flowchart LR
    A[Appian Integration] -->|REST + X-Correlation-Id| M[Mock Server]
    subgraph Mock Server (.NET 8 Minimal API)
        M --> H[/health/]
        M --> C[/api/customers/{id}/]
        M --> P[/api/payments/validate/]
        M --> S[/api/callbacks/status/]
        C --> DS[(In-memory fictional data)]
    end
    M -->|Uniform JSON success or ApiError| A
```

## Quick start

Requires the **.NET 8 SDK**.

```bash
dotnet run --project src/MockServer
# now listening on http://localhost:5080
```

Check it is up:

```bash
curl http://localhost:5080/health
```

Explore and execute every endpoint in the interactive Swagger UI at
[`http://localhost:5080/swagger`](http://localhost:5080/swagger). The generated
OpenAPI document is available at
[`http://localhost:5080/swagger/v1/swagger.json`](http://localhost:5080/swagger/v1/swagger.json)
for client generation and contract checks.

Sample requests for every endpoint live in [`requests.http`](requests.http)
(usable from VS Code REST Client, Rider, or Visual Studio).

## API examples

**Customer lookup**

```bash
curl http://localhost:5080/api/customers/CUST-1001
```

```json
{
  "id": "CUST-1001",
  "fullName": "Ada Lovelace",
  "segment": "RETAIL",
  "status": "ACTIVE",
  "country": "GB"
}
```

**Payment validation (invalid input)**

```bash
curl -X POST http://localhost:5080/api/payments/validate \
  -H "Content-Type: application/json" \
  -d '{ "currency": "POUND", "amount": -5, "debtorAccount": "", "creditorAccount": "" }'
```

```json
{
  "success": false,
  "error": {
    "code": "PAYMENT_VALIDATION_FAILED",
    "category": "client",
    "message": "One or more payment fields are invalid.",
    "correlationId": "…",
    "retryable": false,
    "timestampUtc": "2026-07-08T10:15:30Z",
    "details": [
      "Currency must be a 3-letter ISO 4217 code.",
      "Amount must be greater than zero.",
      "DebtorAccount is required.",
      "CreditorAccount is required."
    ]
  }
}
```

## Using it from Appian

1. Create an HTTP **Connected System** pointing at `http://localhost:5080`
   (or wherever you host the mock).
2. Add **Integrations** for each route above.
3. Forward the Appian process instance id as `X-Correlation-Id` so the mock's
   response can be tied back to the process for tracing.
4. Build your error handling against the `ApiError` contract — test the 400/404
   paths deliberately using the invalid samples in `requests.http`.

See the companion repository *appian-enterprise-patterns* for the integration
error-handling patterns this contract is designed around.

## Folder structure

```
appian-integration-mock-server/
├── README.md
├── LICENSE
├── .gitignore
├── requests.http
├── AppianIntegrationMockServer.sln
├── tests/MockServer.Tests/
└── src/
    └── MockServer/
        ├── MockServer.csproj
        ├── Program.cs
        ├── appsettings.json
        ├── Properties/launchSettings.json
        ├── Data/CustomerStore.cs
        └── Models/
            ├── ApiError.cs
            ├── Customer.cs
            ├── PaymentValidation.cs
            └── StatusCallbackRequest.cs
```

## Limitations

- Data is in-memory and read-only; nothing is persisted between runs.
- No authentication — it is a local development tool, not a production service.
- Business rules (e.g. the payment threshold) are deliberately trivial.

## Development and tests

Restore, build, and run the endpoint-level test suite with:

```bash
dotnet restore AppianIntegrationMockServer.sln
dotnet build AppianIntegrationMockServer.sln --configuration Release --no-restore --warnaserror
dotnet test AppianIntegrationMockServer.sln --configuration Release --no-build
```

The tests exercise the 400/404 contracts, correlation-id propagation, and validation details using fictional request data. GitHub Actions runs the same build and tests for pull requests and pushes to `main`.

## Roadmap

- [ ] Optional configurable latency/failure injection for resilience testing
- [ ] Dockerfile for one-command startup
- [x] OpenAPI document and interactive Swagger UI for the endpoints

## License

Released under the [MIT License](LICENSE).
