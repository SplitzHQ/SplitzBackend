# .NET 10.0 Upgrade Plan

## Execution Steps

Execute steps below sequentially one by one in the order they are listed.

1. Validate that an .NET 10.0 SDK required for this upgrade is installed on the machine and if not, help to get it installed.
2. Ensure that the SDK version specified in global.json files is compatible with the .NET 10.0 upgrade.
3. Upgrade SplitzBackend.csproj

## Settings

This section contains settings and data used by execution steps.

### Excluded projects

Table below contains projects that do belong to the dependency graph for selected projects and should not be included in the upgrade.

| Project name                                   | Description                 |
|:-----------------------------------------------|:---------------------------:|
| None                                           | Not excluded                |

### Aggregate NuGet packages modifications across all projects

NuGet packages used across all selected projects or their dependencies that need version update in projects that reference them.

| Package Name                                   | Current Version | New Version | Description                                   |
|:-----------------------------------------------|:---------------:|:-----------:|:----------------------------------------------|
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 8.0.15          | 10.0.1      | Align Identity APIs with .NET 10              |
| Microsoft.EntityFrameworkCore                  | 9.0.4           | 10.0.1      | Recommended for .NET 10                       |
| Microsoft.EntityFrameworkCore.Design           | 9.0.4           | 10.0.1      | Recommended for .NET 10                       |
| Microsoft.EntityFrameworkCore.Sqlite           | 9.0.4           | 10.0.1      | Recommended for .NET 10                       |
| Microsoft.EntityFrameworkCore.Tools            | 9.0.4           | 10.0.1      | Recommended for .NET 10                       |
| Microsoft.VisualStudio.Azure.Containers.Tools.Targets | 1.21.2         |             | Incompatible; remove or replace for .NET 10   |
| Microsoft.VisualStudio.Web.CodeGeneration.Design | 9.0.0           | 10.0.0      | Recommended for .NET 10                       |
| System.Formats.Asn1                            | 9.0.4           | 10.0.1      | Recommended for .NET 10                       |
| System.IO.Hashing                              | 9.0.4           | 10.0.1      | Recommended for .NET 10; satisfies XxHash3    |
| System.Text.Json                               | 9.0.4           | 10.0.1      | Recommended for .NET 10                       |

### Project upgrade details

#### SplitzBackend.csproj modifications

Project properties changes:
  - Target framework should be changed from `net8.0` to `net10.0`.

NuGet packages changes:
  - Microsoft.AspNetCore.Identity.EntityFrameworkCore should be updated from `8.0.15` to `10.0.1` (align Identity APIs with .NET 10).
  - Microsoft.EntityFrameworkCore should be updated from `9.0.4` to `10.0.1` (recommended for .NET 10).
  - Microsoft.EntityFrameworkCore.Design should be updated from `9.0.4` to `10.0.1` (recommended for .NET 10).
  - Microsoft.EntityFrameworkCore.Sqlite should be updated from `9.0.4` to `10.0.1` (recommended for .NET 10).
  - Microsoft.EntityFrameworkCore.Tools should be updated from `9.0.4` to `10.0.1` (recommended for .NET 10).
  - Microsoft.VisualStudio.Web.CodeGeneration.Design should be updated from `9.0.0` to `10.0.0` (recommended for .NET 10).
  - System.Formats.Asn1 should be updated from `9.0.4` to `10.0.1` (recommended for .NET 10).
  - System.IO.Hashing should be updated from `9.0.4` to `10.0.1` (recommended for .NET 10; satisfies XxHash3 usage).
  - System.Text.Json should be updated from `9.0.4` to `10.0.1` (recommended for .NET 10).
  - Microsoft.VisualStudio.Azure.Containers.Tools.Targets (1.21.2) has no supported .NET 10 version; remove or replace.

Other changes:
  - Reverify `builder.Services.AddAutoMapper(...)` in `src/Program.cs` against the updated AutoMapper package version for .NET 10 and adjust APIs if needed.
  - Reverify Identity endpoint configuration and EF stores in `src/Program.cs` after package upgrades to ensure compatibility with .NET 10.
  - Ensure `System.IO.Hashing.XxHash3` usage in `src/Models/Group.cs` is satisfied by the updated System.IO.Hashing package (10.0.1).
