# Assessment Summary

- Target framework change recommended: `net8.0` -> `net10.0`.
- NuGet updates suggested to align with .NET 10:
  - Microsoft.AspNetCore.Identity.EntityFrameworkCore 8.0.15 -> 10.0.1
  - Microsoft.EntityFrameworkCore* 9.0.4 -> 10.0.1 (Core, Design, Sqlite, Tools)
  - Microsoft.VisualStudio.Web.CodeGeneration.Design 9.0.0 -> 10.0.0
  - System.Formats.Asn1 / System.IO.Hashing / System.Text.Json 9.0.4 -> 10.0.1
- NuGet incompatibility: Microsoft.VisualStudio.Azure.Containers.Tools.Targets 1.21.2 has no supported version for .NET 10; consider removal/replacement.
- API compatibility notes:
  - `AddAutoMapper` configuration in `src/Program.cs` flagged for binary incompatibility; verify APIs against .NET 10 / AutoMapper version.
  - Identity endpoints and EF stores in `src/Program.cs` flagged as source incompatible; ensure updated package versions and APIs.
  - `System.IO.Hashing.XxHash3` usage in `src/Models/Group.cs` flagged; ensure correct package/version for .NET 10 (prefer stable 10.0.x).
- Project analyzed: `SplitzBackend.csproj`; estimated effort: 16 story points; issues: 5 (incidents: 16).

Generated from `.github/upgrades/assessment.json` on 2025-12-09.
