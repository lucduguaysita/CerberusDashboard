# Projects and dependencies analysis

This document provides a comprehensive overview of the projects and their dependencies in the context of upgrading to .NETCoreApp,Version=v10.0.

## Table of Contents

- [Executive Summary](#executive-Summary)
  - [Highlevel Metrics](#highlevel-metrics)
  - [Projects Compatibility](#projects-compatibility)
  - [Package Compatibility](#package-compatibility)
  - [API Compatibility](#api-compatibility)
  - [Binding Redirect Configuration](#binding-redirect-configuration)
- [Aggregate NuGet packages details](#aggregate-nuget-packages-details)
- [Top API Migration Challenges](#top-api-migration-challenges)
  - [Technologies and Features](#technologies-and-features)
  - [Most Frequent API Issues](#most-frequent-api-issues)
- [Projects Relationship Graph](#projects-relationship-graph)
- [Project Details](#project-details)

  - [CerberusDashboard.csproj](#cerberusdashboardcsproj)


## Executive Summary

### Highlevel Metrics

| Metric | Count | Status |
| :--- | :---: | :--- |
| Total Projects | 2 | 0 require upgrade |
| Total NuGet Packages | 23 | All compatible |
| Total Code Files | 11 |  |
| Total Code Files with Incidents | 0 |  |
| Total Lines of Code | 4316 |  |
| Total Number of Issues | 0 |  |
| Estimated LOC to modify | 0+ | at least 0.0% of codebase |

### Projects Compatibility

| Project | Target Framework | Difficulty | Package Issues | API Issues | Binding Issues | Est. LOC Impact | Description |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :--- |
| [CerberusDashboard.csproj](#cerberusdashboardcsproj) | net10.0 | ✅ None | 0 | 0 | 0 |  | AspNetCore, Sdk Style = True |

### Package Compatibility

| Status | Count | Percentage |
| :--- | :---: | :---: |
| ✅ Compatible | 23 | 100.0% |
| ⚠️ Incompatible | 0 | 0.0% |
| 🔄 Upgrade Recommended | 0 | 0.0% |
| ***Total NuGet Packages*** | ***23*** | ***100%*** |

### API Compatibility

| Category | Count | Impact |
| :--- | :---: | :--- |
| 🔴 Binary Incompatible | 0 | High - Require code changes |
| 🟡 Source Incompatible | 0 | Medium - Needs re-compilation and potential conflicting API error fixing |
| 🔵 Behavioral change | 0 | Low - Behavioral changes that may require testing at runtime |
| ✅ Compatible | 0 |  |
| ***Total APIs Analyzed*** | ***0*** |  |

## Aggregate NuGet packages details

| Package | Current Version | Suggested Version | Projects | Description |
| :--- | :---: | :---: | :--- | :--- |
| Microsoft.AspNetCore.JsonPatch | 10.0.0 |  | [CerberusDashboard.csproj](#cerberusdashboardcsproj) | ✅Compatible |
| Microsoft.AspNetCore.Mvc.NewtonsoftJson | 10.0.0 |  | [CerberusDashboard.csproj](#cerberusdashboardcsproj) | ✅Compatible |
| Microsoft.AspNetCore.SignalR.Protocols.NewtonsoftJson | 10.0.0 |  | [CerberusDashboard.csproj](#cerberusdashboardcsproj) | ✅Compatible |
| Microsoft.Bcl.Cryptography | 9.0.13 |  | [CerberusDashboard.csproj](#cerberusdashboardcsproj) | ✅Compatible |
| Microsoft.Data.SqlClient | 7.0.1 |  | [CerberusDashboard.csproj](#cerberusdashboardcsproj) | ✅Compatible |
| Microsoft.Data.SqlClient.Extensions.Abstractions | 1.0.0 |  | [CerberusDashboard.csproj](#cerberusdashboardcsproj) | ✅Compatible |
| Microsoft.Data.SqlClient.Internal.Logging | 1.0.0 |  | [CerberusDashboard.csproj](#cerberusdashboardcsproj) | ✅Compatible |
| Microsoft.Data.SqlClient.SNI.runtime | 6.0.2 |  | [CerberusDashboard.csproj](#cerberusdashboardcsproj) | ✅Compatible |
| Microsoft.Extensions.Hosting.WindowsServices | 10.0.0 |  | [CerberusDashboard.csproj](#cerberusdashboardcsproj) | ✅Compatible |
| Microsoft.IdentityModel.Abstractions | 8.16.0 |  | [CerberusDashboard.csproj](#cerberusdashboardcsproj) | ✅Compatible |
| Microsoft.IdentityModel.JsonWebTokens | 8.16.0 |  | [CerberusDashboard.csproj](#cerberusdashboardcsproj) | ✅Compatible |
| Microsoft.IdentityModel.Logging | 8.16.0 |  | [CerberusDashboard.csproj](#cerberusdashboardcsproj) | ✅Compatible |
| Microsoft.IdentityModel.Protocols | 8.16.0 |  | [CerberusDashboard.csproj](#cerberusdashboardcsproj) | ✅Compatible |
| Microsoft.IdentityModel.Protocols.OpenIdConnect | 8.16.0 |  | [CerberusDashboard.csproj](#cerberusdashboardcsproj) | ✅Compatible |
| Microsoft.IdentityModel.Tokens | 8.16.0 |  | [CerberusDashboard.csproj](#cerberusdashboardcsproj) | ✅Compatible |
| Microsoft.SqlServer.Server | 1.0.0 |  | [CerberusDashboard.csproj](#cerberusdashboardcsproj) | ✅Compatible |
| Newtonsoft.Json | 13.0.4 |  | [CerberusDashboard.csproj](#cerberusdashboardcsproj) | ✅Compatible |
| Newtonsoft.Json.Bson | 1.0.2 |  | [CerberusDashboard.csproj](#cerberusdashboardcsproj) | ✅Compatible |
| System.Configuration.ConfigurationManager | 9.0.13 |  | [CerberusDashboard.csproj](#cerberusdashboardcsproj) | ✅Compatible |
| System.IdentityModel.Tokens.Jwt | 8.16.0 |  | [CerberusDashboard.csproj](#cerberusdashboardcsproj) | ✅Compatible |
| System.Security.Cryptography.Pkcs | 9.0.13 |  | [CerberusDashboard.csproj](#cerberusdashboardcsproj) | ✅Compatible |
| System.Security.Cryptography.ProtectedData | 9.0.13 |  | [CerberusDashboard.csproj](#cerberusdashboardcsproj) | ✅Compatible |
| System.ServiceProcess.ServiceController | 10.0.0 |  | [CerberusDashboard.csproj](#cerberusdashboardcsproj) | ✅Compatible |

## Top API Migration Challenges

### Technologies and Features

| Technology | Issues | Percentage | Migration Path |
| :--- | :---: | :---: | :--- |

### Most Frequent API Issues

| API | Count | Percentage | Category |
| :--- | :---: | :---: | :--- |

## Projects Relationship Graph

Legend:
📦 SDK-style project
⚙️ Classic project

```mermaid
flowchart LR
    P1["<b>📦&nbsp;CerberusDashboard.csproj</b><br/><small>net10.0</small>"]
    click P1 "#cerberusdashboardcsproj"

```

## Project Details

<a id="cerberusdashboardcsproj"></a>
### CerberusDashboard.csproj

#### Project Info

- **Current Target Framework:** net10.0✅
- **SDK-style**: True
- **Project Kind:** AspNetCore
- **Dependencies**: 0
- **Dependants**: 0
- **Number of Files**: 17
- **Lines of Code**: 4316
- **Estimated LOC to modify**: 0+ (at least 0.0% of the project)

#### Dependency Graph

Legend:
📦 SDK-style project
⚙️ Classic project

```mermaid
flowchart TB
    subgraph current["CerberusDashboard.csproj"]
        MAIN["<b>📦&nbsp;CerberusDashboard.csproj</b><br/><small>net10.0</small>"]
        click MAIN "#cerberusdashboardcsproj"
    end

```

### API Compatibility

| Category | Count | Impact |
| :--- | :---: | :--- |
| 🔴 Binary Incompatible | 0 | High - Require code changes |
| 🟡 Source Incompatible | 0 | Medium - Needs re-compilation and potential conflicting API error fixing |
| 🔵 Behavioral change | 0 | Low - Behavioral changes that may require testing at runtime |
| ✅ Compatible | 0 |  |
| ***Total APIs Analyzed*** | ***0*** |  |

