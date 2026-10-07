# Cerberus Dashboard

A real-time SQL Server monitoring dashboard built on ASP.NET Core (.NET 10). It runs as a Windows Service, polls one or more SQL Server instances on a timer, and pushes updates to connected browsers over SignalR.

The dashboard is self-contained: the MSI bundles the .NET runtime, and jQuery and the SignalR client ship in `wwwroot`, so no CDN or separately installed runtime is required on the target machine.

> [!IMPORTANT]
> The application has **no user authentication or role separation**. Anyone who can reach the endpoint can view monitoring data and switch the active estate for every other user. Keep it bound to `localhost` — or behind an authenticating reverse proxy — until appropriate network and authentication controls are in place.

## Monitored panels

| | |
|:---|:---|
| Server Info | Performance |
| SQL Agent Jobs | Blocking / Long Waits |
| Recent Expensive Queries | Active Queries |
| Data File I/O Statistics | Hot Tables |
| Databases | Job History |
| Execution Plan | Active Sessions |
| File I/O History | Partitions |

Most panels read server-scoped DMVs, which require `VIEW SERVER STATE` (or `VIEW SERVER PERFORMANCE STATE` on SQL Server 2022+) for the identity running the service. See [INSTALLATION_GUIDE.md](INSTALLATION_GUIDE.md) for the permission details.

## Requirements

- Windows x64
- .NET SDK 10.0.401 or a later servicing patch in the same feature band (pinned in `global.json`)
- A reachable SQL Server instance, and a login or service identity with the DMV permissions above

## Quick start

```powershell
git clone <repository-url>
cd CerberusDashboard
dotnet run
```

Then browse to `http://localhost:5001`.

The committed `appsettings.json` monitors a single local instance over Windows integrated security, so a default local SQL Server needs no further configuration. To point it elsewhere, copy `appsettings.json.example` and edit the connection string. Monitoring settings are read once at startup, so restart after any change.

> [!WARNING]
> The committed `"Urls": "http://0.0.0.0:5001"` listens on **every network interface** over plain HTTP, and `"AllowedHosts": "*"` accepts any host header. Combined with the lack of authentication, anyone who can route to the machine on port 5001 has full dashboard access. Change `Urls` to `http://localhost:5001` for local-only development. The MSI ships a different, safer default: a Kestrel endpoint on `localhost:5001` with `AllowedHosts` limited to `localhost;127.0.0.1;[::1]`.

## Configuration

Settings live in `appsettings.json`. The shape is:

```json
{
  "DefaultEstate": "Local",
  "Estates": {
    "Local": {
      "DisplayName": "Local",
      "MonitorRefreshSeconds": 15,
      "MonitoredInstances": "LocalDB",
      "InstanceDisplayNames": "LocalDB",
      "ConnectionStrings": {
        "LocalDB": "Server=(local);Database=master;Integrated Security=True;TrustServerCertificate=True;Connection Timeout=15;"
      }
    }
  },
  "Urls": "http://0.0.0.0:5001",
  "AllowedHosts": "*"
}
```

**Estates** group instances into switchable environments. Each estate owns its own instance list, display names, and connection strings — nothing is inherited from the root. One estate is active for the whole service at a time, not per user, and switching reloads every connected dashboard. Selection is runtime-only: a restart returns to `DefaultEstate`.

Validation is strict and fails at startup rather than silently degrading: every key in `MonitoredInstances` needs a matching `ConnectionStrings` entry in the same scope, `MonitorRefreshSeconds` must be 5–86400, duplicate instance keys are rejected, and an explicit `DefaultEstate` must match a configured key. Omit `Estates` entirely and the root-level settings work as a single `Default` estate.

Do not commit credentials. Supply them through protected deployment configuration, and prefer integrated security with a least-privilege service identity.

See [CONNECTION_STRING_GUIDE.md](CONNECTION_STRING_GUIDE.md) for connection-string patterns and troubleshooting.

## Documentation

| Document | Covers |
|:---|:---|
| [INSTALLATION_GUIDE.md](INSTALLATION_GUIDE.md) | Deployment, configuration, upgrades, uninstall, troubleshooting, security recommendations |
| [CONNECTION_STRING_GUIDE.md](CONNECTION_STRING_GUIDE.md) | Connection-string options and common connection errors |
| [CerberusInstaller/README.md](CerberusInstaller/README.md) | Building the MSI, installer ownership, legacy migration, release validation |
| [Tests/CerberusDashboard.RegressionTests/README.md](Tests/CerberusDashboard.RegressionTests/README.md) | What the regression checks cover |

## Project layout

```
Program.cs                     Host, DI, data protection, Windows Service wiring
MonitorBackgroundService.cs    Timer loop that polls SQL and broadcasts snapshots
Controllers/                   Dashboard view and estate selection
Hubs/MonitorHub.cs             SignalR hub: snapshots, job history, plans, sessions
Services/SqlMonitorService.cs  DMV queries and snapshot assembly
Services/EstateManager.cs      Estate profiles, validation, active-estate switching
Services/ConnectionTracker.cs  Connected-client tracking
Views/Dashboard/Index.cshtml   Single-page dashboard (self-contained CSS and JS)
CerberusInstaller/             WiX 7 MSI project
Tests/                         Dependency-free regression runner
```

## Building

```powershell
# Application only
dotnet build CerberusDashboard.csproj -c Release

# Application and MSI
dotnet build CerberusDashboard.sln -c Release -p:InstallerVersion=2.0.0
```

The MSI lands in `CerberusInstaller\bin\Release\CerberusDashboard-<version>-win-x64.msi` and installs per-machine to `%ProgramFiles%\CerberusDashboard`, registering `CerberusDashboard` as an automatic service under LocalSystem bound to `localhost:5001`. Use exactly three numeric version fields. Builds are unsigned until a release-signing process is applied.

## Tests

```powershell
dotnet run --project .\Tests\CerberusDashboard.RegressionTests\CerberusDashboard.RegressionTests.csproj -c Release
```

A dependency-free executable that exits nonzero on failure — not a Test Explorer adapter. It covers estate validation and isolation, revision/session rejection, concurrent selection, connection tracking, Razor rendering, antiforgery enforcement, and two-client SignalR behavior, using a temporary loopback server with empty estates and ephemeral keys. No production settings or SQL connections are touched.

## Notes

- Data-protection keys are persisted to `%ProgramData%\CerberusDashboard\DataProtection` and protected with machine-level DPAPI, so they cannot be moved to another machine without a key-migration procedure.
- `TrustServerCertificate=True` is acceptable for a local instance but should not be carried to a remote server; provision a trusted certificate instead.
