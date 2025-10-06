# Copilot Instructions for SplitzBackend

## Repository Overview

**SplitzBackend** is an ASP.NET Core 8.0 Web API for expense splitting and group financial management. It provides RESTful endpoints for user authentication, friend management, group creation, and transaction tracking with support for draft transactions.

**Technology Stack:**
- **Framework:** ASP.NET Core 8.0 (targeting .NET 8.0)
- **Language:** C# with nullable reference types and implicit usings enabled
- **Database:** SQLite with Entity Framework Core 9.0.4
- **Authentication:** ASP.NET Core Identity with JWT Bearer tokens
- **API Documentation:** Swagger/OpenAPI with XML documentation generation
- **Mapping:** AutoMapper 14.0.0 with EF Core integration
- **Runtime:** .NET 9.0.305 SDK installed (targets .NET 8.0)

**Repository Size:** Small (~15 source files, single project solution)

## Build and Validation Instructions

### Prerequisites
- .NET 8.0 SDK or later (tested with .NET 9.0.305)
- SQLite (automatically created in development)

### Essential Commands (in order)

#### 1. Restore Dependencies
**ALWAYS run this first after cloning or when dependencies change:**
```bash
cd /path/to/SplitzBackend
dotnet restore
```
- **Duration:** ~20-25 seconds
- **Purpose:** Downloads NuGet packages
- **Note:** Restores to solution level (SplitzBackend.sln)

#### 2. Build the Project
```bash
dotnet build
```
- **Duration:** ~10-12 seconds (after restore)
- **Output:** `bin/Debug/net8.0/SplitzBackend.dll`
- **Expected Warnings:** 2 warnings about unused 'logger' parameters in AccountController.cs and GroupController.cs - these are benign and can be ignored
- **Success Indicator:** "Build succeeded" message

#### 3. Format Check (Linting)
```bash
dotnet format SplitzBackend.sln --verify-no-changes
```
- **Duration:** ~5 seconds
- **Purpose:** Validates code formatting against .editorconfig standards
- **IMPORTANT:** Must specify solution file explicitly (not just `dotnet format`)
- **Exit Code 0:** No formatting issues

#### 4. Run the Application
```bash
dotnet run
```
- **Duration:** ~5 seconds to start (includes build)
- **Default Port:** http://localhost:5119 (from Properties/launchSettings.json)
- **Behavior in Development:**
  - Creates `app.db` SQLite database file if it doesn't exist
  - Seeds test data with 4 users and 3 groups
  - Test credentials: alice@example.com, bob@example.com, charlie@example.com, diana@example.com (all use password: TestPassword123!)
  - Swagger UI available at http://localhost:5119/swagger
- **Success Indicators:**
  - "Now listening on: http://localhost:5119"
  - "Application started. Press Ctrl+C to shut down."
  - "Test data seeded successfully!" (in Development environment)

#### 5. Clean Build Artifacts
```bash
dotnet clean
```
- **Duration:** <1 second
- **Purpose:** Removes bin/ and obj/ directories

#### 6. Publish Release Build
```bash
dotnet publish -c Release -o /path/to/output
```
- **Duration:** ~15 seconds
- **Output:** Self-contained deployment in specified directory
- **Note:** Generates warning about solution-level output path (can be ignored)

### Testing
**No unit tests currently exist in this repository.** When adding tests:
- Use `dotnet test` command
- Follow ASP.NET Core testing conventions with xUnit/NUnit
- Place test projects in separate folder (recommended: `tests/`)

### Common Build Issues and Workarounds

1. **Database Locked Error:** If `app.db` is locked, stop all running instances and delete `app.db-shm` and `app.db-wal` files
2. **Format Command Fails:** Always use `dotnet format SplitzBackend.sln` (not just `dotnet format`)
3. **Port Already in Use:** Change port in `Properties/launchSettings.json` or set `ASPNETCORE_URLS` environment variable

## Project Layout and Architecture

### Directory Structure
```
/
├── .github/                 # GitHub configuration (workflows go here)
├── src/                     # All source code
│   ├── Controllers/         # API Controllers (4 files)
│   │   ├── AccountController.cs       # User account and friends management
│   │   ├── GroupController.cs         # Group CRUD and member management
│   │   ├── TransactionController.cs   # Transaction operations
│   │   └── TransactionDraftController.cs # Draft transaction handling
│   ├── Models/              # Domain models and DTOs (10 files)
│   │   ├── SplitzUser.cs    # User entity extending IdentityUser
│   │   ├── Friend.cs        # Friend relationships
│   │   ├── Group.cs         # Group entity with member hashing
│   │   ├── GroupBalance.cs  # Balance tracking within groups
│   │   ├── GroupJoinLink.cs # Invite link system
│   │   ├── Transaction.cs   # Completed transactions
│   │   ├── TransactionBalance.cs      # Individual transaction splits
│   │   ├── TransactionDraft.cs        # Draft transactions
│   │   ├── TransactionDraftBalance.cs # Draft transaction splits
│   │   └── Tag.cs           # Transaction tags/categories
│   ├── Program.cs           # Application entry point and configuration
│   ├── SplitzDbContext.cs   # EF Core database context
│   ├── MapperProfile.cs     # AutoMapper configuration
│   └── SwaggerFilter.cs     # Swagger security configuration
├── Properties/
│   └── launchSettings.json  # Development server configuration
├── SplitzBackend.csproj     # Project file
├── SplitzBackend.sln        # Solution file
├── SplitzBackend.sln.DotSettings # ReSharper settings
├── appsettings.json         # Production configuration
├── appsettings.Development.json # Development configuration (has SQLite connection)
├── Dockerfile               # Multi-stage Docker build
├── .dockerignore            # Docker build exclusions
├── .gitignore               # Git exclusions (includes *.db files)
├── README.md                # Project readme (minimal)
└── LICENSE.txt              # GNU AGPL v3 license
```

### Key Architectural Patterns

1. **Clean API Architecture:**
   - Controllers use dependency injection for DbContext, UserManager, IMapper
   - DTOs separate from domain models (e.g., Group vs GroupDto vs GroupInputDto)
   - All controllers require authorization via `[Authorize]` attribute
   - XML documentation comments for Swagger

2. **Database Context (`SplitzDbContext`):**
   - Inherits from `IdentityDbContext<SplitzUser>`
   - Configures friend relationships with one-to-many mapping
   - Database auto-created via `EnsureCreatedAsync()` in Program.cs
   - Connection string in `appsettings.Development.json`: "Data Source=app.db"

3. **Identity Configuration:**
   - Requires unique email
   - Password requirements: 12+ chars, digit, lowercase (no uppercase or special chars required)
   - Endpoints mapped at `/account` route group
   - Bearer token authentication for all API endpoints

4. **AutoMapper Integration:**
   - Profile in `MapperProfile.cs` defines all mappings
   - Uses EF Core model integration for projection queries
   - Collection mappers enabled for list mappings

5. **Special Business Logic:**
   - Groups use XxHash3 for member ID hashing (prevents duplicate groups with same members)
   - Groups track transaction count and last activity time for optimization
   - Friend relationships must exist before adding members to groups

### Configuration Files

- **SplitzBackend.csproj:**
  - Generates XML documentation (enabled with `<GenerateDocumentationFile>true</GenerateDocumentationFile>`)
  - Suppresses warning CS1591 (missing XML comments)
  - Docker support enabled

- **.gitignore:**
  - Standard Visual Studio ignore patterns
  - Excludes: bin/, obj/, *.db, *.db-shm, *.db-wal files
  - Node modules, build artifacts, coverage reports

- **No editorconfig at root:** Formatting rules come from default .NET conventions

### Validation Pipeline

**Currently no CI/CD workflows exist.** When adding GitHub Actions:
- Recommended workflow steps: restore → format check → build → test
- Use .NET 8.0 or later runtime
- Consider caching NuGet packages
- Run on: ubuntu-latest (project uses SQLite, fully cross-platform)

### API Endpoints Structure

All controllers follow REST conventions:
- **AccountController** (`/account`): GET (user info), PATCH (update profile), POST/PATCH/DELETE (`/friend/{id}`)
- **GroupController** (`/group`): GET (list/detail), POST (create), PUT (update), POST (`/{groupId}/members`, `/{groupId}/join-link`, `/join/{joinLinkId}`)
- **TransactionController** (`/transaction`): GET, POST, DELETE
- **TransactionDraftController** (`/transactiondraft`): GET, POST, PUT, DELETE

Plus Identity API endpoints at `/account` (login, register, etc.)

### Important Dependencies and Relationships

- **Models use required properties:** All DTOs and entities leverage C# nullable reference types
- **AutoMapper ProjectTo:** Controllers use `.ProjectTo<T>()` for efficient queries
- **Entity Framework Include:** Eager loading via `.Include()` commonly used for related entities
- **Identity Integration:** UserManager<SplitzUser> injected for authentication/authorization
- **Validation Attributes:** Models use DataAnnotations (Required, MinLength, MaxLength, Range, Url, etc.)

## Common Patterns and Best Practices

### When Adding New Controllers:
1. Inherit from `ControllerBase` (not `Controller`)
2. Use `[Authorize]`, `[ApiController]`, `[Route("[controller]")]` attributes
3. Add constructor parameters for dependencies (use primary constructor syntax)
4. Add XML documentation comments for Swagger
5. Use `[Produces("application/json")]` and `[ProducesResponseType]` attributes
6. Always check user authorization with `await userManager.GetUserAsync(User)`

### When Adding New Models:
1. Create entity class first (in Models/)
2. Add corresponding DTO classes (ModelDto, ModelInputDto, ModelReducedDto as needed)
3. Register mappings in `MapperProfile.cs`
4. Add DbSet to `SplitzDbContext.cs` if needed
5. Use required properties and nullable reference types appropriately

### When Modifying Database Schema:
- This project uses `EnsureCreatedAsync()` not migrations
- Database is recreated on each run in development
- Changes to models automatically reflected in database schema
- Test data seeding logic in `Program.cs` `SeedTestData()` method

## Files to Trust These Instructions For

- Building: Always use `dotnet restore` before `dotnet build`
- Formatting: Always specify solution file: `dotnet format SplitzBackend.sln`
- Running: App runs on port 5119 by default and creates/seeds database automatically
- Controllers: All require `[Authorize]` attribute and inject UserManager for auth checks
- Database: SQLite file-based (app.db), auto-created, uses Identity schema

**Trust these instructions unless you observe different behavior. If the instructions are incomplete or incorrect for your specific task, supplement them with additional research but report findings to improve these instructions.**
