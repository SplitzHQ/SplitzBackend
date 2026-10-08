# SplitzBackend Agent Instructions

Use these notes when working in this backend. Keep changes small, follow existing controller and EF patterns, and prefer linking to code over duplicating generated documentation.

## Project Shape

- ASP.NET Core Web API targeting `net10.0`; main startup, DI, middleware, seeding, auth, OpenAPI, and migration-on-startup logic live in [src/Program.cs](src/Program.cs).
- EF Core uses SQLite through `ConnectionStrings:Sqlite` in [appsettings.json](appsettings.json), defaulting to `Data Source=app.db`.
- `SplitzDbContext` extends `IdentityDbContext<SplitzUser>` and exposes the app aggregate sets in [src/SplitzDbContext.cs](src/SplitzDbContext.cs).
- Controllers in [src/Controllers](src/Controllers) contain most request workflow and authorization logic. Services in [src/Services](src/Services) are focused helpers for invoice debt calculation, image processing, and S3-compatible storage.
- Entities and DTOs are colocated in [src/Models](src/Models). Validation attributes on DTOs/entities are part of the API contract.

## Commands

Run from the `SplitzBackend` folder unless noted otherwise.

```powershell
dotnet restore
dotnet build
dotnet run --project SplitzBackend.csproj
dotnet format SplitzBackend.sln --verify-no-changes
dotnet test SplitzBackend.Tests/SplitzBackend.Tests.csproj
```

Backend tests live in [SplitzBackend.Tests](SplitzBackend.Tests) (xUnit). Rate limiting tests boot the real app through `RateLimitingWebApplicationFactory` with a throwaway SQLite file, so they exercise the full middleware pipeline.

## Local Runtime

- The `http` launch profile binds to `http://0.0.0.0:5119`; local clients on Windows should call `http://localhost:5119`.
- Development startup applies pending EF migrations automatically and seeds sample users/groups only when no users exist.
- OpenAPI is served only in Development. Swashbuckle is the active generator at `/openapi/{documentName}.json`; Scalar UI is mapped by `MapScalarApiReference()`.
- `appsettings.json` contains placeholder S3 values. Image upload and signed photo URLs require real `Storage` config.
- Rate limits are configured under `RateLimiting`; the defaults and every tunable live in [src/Services/RateLimiting/RateLimitOptions.cs](src/Services/RateLimiting/RateLimitOptions.cs), so `appsettings.json` only lists keys that differ. In Production, startup fails unless `TrustedProxies` or `TrustedNetworks` is set or `Enabled` is false, because an unconfigured proxy would make every client share one IP partition.

## API And Data Conventions

- Custom endpoints use `[Authorize]`, `[ApiController]`, and `[Route("[controller]")]`; Identity minimal APIs are mounted under `/account` in [src/Program.cs](src/Program.cs).
- Access control is usually based on the current Identity user plus group membership/ownership checks, not roles. Preserve required `Include(...)` calls before membership or balance mutations.
- Use AutoMapper mappings in [src/MapperProfile.cs](src/MapperProfile.cs). Photo fields may store owned object keys such as `users/...`, `groups/...`, `transactions/...`, or `drafts/...`; `SignedPhotoUrlResolver` converts them to signed relative URLs.
- Swashbuckle filters in [src/OpenAPIGen/Filter](src/OpenAPIGen/Filter) customize the frontend contract: authorized operations get Bearer security, decimals are emitted as string format `decimal`, and enums are emitted as strings.
- Do not hand-edit generated frontend OpenAPI files in `SplitzFrontend/src/backend/openapi`; regenerate them from the backend OpenAPI contract after API changes.

## Migrations

- Migrations live in [src/Migrations](src/Migrations).
- Add migrations from the backend root with:

```powershell
dotnet ef migrations add <Name> --output-dir src/Migrations
dotnet build
```

- Normal app startup runs `db.Database.MigrateAsync()`, so use `dotnet ef database update` only when a manual database update is specifically needed.

## Known Pitfalls

- Development seeding does not refresh an existing `app.db` once any user exists.
- Use `DateTime.UtcNow` for server-generated timestamps. `Group.LastActivityTime` is persisted and used for sorting in clients, so local-time values can produce incorrect ordering across time zones.
- `Group.TransactionCount` and `Group.LastActivityTime` are denormalized search/sort hints; keep them in sync when changing transaction or group workflows.
