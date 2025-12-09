# .NET 10 Upgrade Report

## Project target framework modifications

| Project name        | Old Target Framework | New Target Framework | Commits |
|:--------------------|:--------------------:|:--------------------:|:-------:|
| SplitzBackend.csproj | net8.0 | net10.0 | - |

## NuGet Packages

| Package Name                                   | Old Version | New Version | Commit Id |
|:-----------------------------------------------|:-----------:|:-----------:|:---------:|
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 8.0.15 | 10.0.1 | - |
| Microsoft.EntityFrameworkCore                  | 9.0.4 | 10.0.1 | - |
| Microsoft.EntityFrameworkCore.Design           | 9.0.4 | 10.0.1 | - |
| Microsoft.EntityFrameworkCore.Sqlite           | 9.0.4 | 10.0.1 | - |
| Microsoft.EntityFrameworkCore.Tools            | 9.0.4 | 10.0.1 | - |
| Microsoft.VisualStudio.Web.CodeGeneration.Design | 9.0.0 | 10.0.0 | - |
| System.IO.Hashing                              | 9.0.4 | 10.0.1 | - |
| Microsoft.VisualStudio.Azure.Containers.Tools.Targets | 1.21.2 | (removed) | - |

## Project feature upgrades

- Unused logger DI parameters removed from controllers to clear compiler warnings after upgrade.
- Incompatible Azure Containers Tools package removed per .NET 10 compatibility guidance.

## All commits

No commits created yet (changes are staged in working tree).

## Next steps

- Run application smoke tests and any API/integration tests available.
- Verify container tooling is not required; if needed, replace with a .NET 10-compatible alternative.
