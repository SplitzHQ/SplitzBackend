# SplitzBackend

SplitzBackend is an ASP.NET Core 10 Web API for SplitZ, an expense splitting and group finance app. It handles user accounts, friends, groups, transactions, transaction drafts, invoices, settlement records, notifications, and avatar/receipt image uploads.

## Tech Stack

- ASP.NET Core 10 targeting `net10.0`
- SQLite with Entity Framework Core migrations
- ASP.NET Core Identity API endpoints with bearer tokens
- AutoMapper for DTO mapping and EF projection
- Swashbuckle OpenAPI 3.1 with Scalar API reference
- FluentStorage.AWS for S3-compatible object storage
- NetVips for avatar and receipt image processing

## Requirements

- .NET 10 SDK or later
- SQLite, created automatically in development
- S3-compatible object storage configuration for image upload flows

The project has been validated locally with .NET SDK `10.0.109`.

## Configuration

The default SQLite connection is defined in [appsettings.json](appsettings.json):

```json
"ConnectionStrings": {
  "Sqlite": "Data Source=app.db"
}
```

Image upload and signed image URL flows require the `Storage` section to be configured with real S3-compatible values:

```json
"Storage": {
  "Provider": "S3",
  "Endpoint": "",
  "AccessKeyId": "",
  "SecretAccessKey": "",
  "Bucket": "splitz",
  "Region": "us-east-1"
}
```

For local development, prefer user secrets or environment variables for credentials instead of committing real values.

## Development

Run commands from the repository root.

```powershell
dotnet restore
dotnet build
dotnet run --project SplitzBackend.csproj
```

The `http` launch profile binds to `http://0.0.0.0:5119`. Use `http://localhost:5119` from browsers and local clients on Windows.

In Development, startup applies pending EF migrations and seeds sample data when no users exist. Test users are created with password `TestPassword123!`:

- `alice@example.com`
- `bob@example.com`
- `charlie@example.com`
- `diana@example.com`

## API Reference

OpenAPI is available in Development:

- OpenAPI JSON: `http://localhost:5119/openapi/v1.json`
- Scalar UI: `http://localhost:5119/scalar/v1`

Custom controller endpoints require authorization unless marked otherwise. Identity API endpoints are mounted under `/account`.

Main API areas:

- `/account`: profile, friend management, avatar upload, and Identity auth endpoints
- `/group`: group list/detail, members, join links, group avatar, group transactions, and group invoices
- `/transaction`: transaction detail/create/update/delete and receipt upload
- `/transactiondraft`: draft detail/create/update/delete and receipt upload
- `/invoice`: invoice list/detail/create/update/delete and settlement records
- `/notification`: notification list, read, dismiss, and dismiss-all actions

## Validation

```powershell
dotnet build
dotnet format SplitzBackend.sln --verify-no-changes
```

There is currently no backend test project. If tests are added, use the normal `dotnet test` workflow and keep test projects under a dedicated `tests/` folder.

## Database Migrations

Migrations live under [src/Migrations](src/Migrations). Add schema migrations from the backend root with:

```powershell
dotnet ef migrations add <Name> --output-dir src/Migrations
dotnet build
```

Normal app startup runs `db.Database.MigrateAsync()`, so `dotnet ef database update` is only needed for manual database updates.

To recreate a local development database, stop the app and delete `app.db`, `app.db-shm`, and `app.db-wal`; the next Development startup will apply migrations and seed data again.

## Project Layout

- [src/Program.cs](src/Program.cs): app startup, dependency injection, auth, OpenAPI, migrations, and seed data
- [src/SplitzDbContext.cs](src/SplitzDbContext.cs): EF Core context and Identity integration
- [src/Controllers](src/Controllers): REST controllers and request authorization logic
- [src/Models](src/Models): entities, DTOs, validation attributes, and typed notification payloads
- [src/Services](src/Services): invoice debt calculation, image processing/storage, and S3-compatible storage helpers
- [src/OpenAPIGen/Filter](src/OpenAPIGen/Filter): Swashbuckle filters for bearer auth, decimal strings, and enum schemas

## Notes

- Do not hand-edit generated frontend OpenAPI files. Regenerate the frontend client from the backend OpenAPI document after API contract changes.
- `Group.TransactionCount` and `Group.LastActivityTime` are denormalized values used for search/sort hints and should stay in sync with transaction workflows.
