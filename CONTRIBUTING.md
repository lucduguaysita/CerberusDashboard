# Contributing

Thanks for helping improve Cerberus Dashboard. This document covers how to get set up, what the code expects, and what a reviewable change looks like.

## Getting set up

You need Windows x64, the .NET SDK pinned in `global.json` (10.0.401 or a later servicing patch in the same feature band), and a reachable SQL Server instance.

```powershell
git clone https://github.com/lucduguaysita/CerberusDashboard.git
cd CerberusDashboard
dotnet build CerberusDashboard.csproj -c Release
dotnet run
```

The committed `appsettings.json` points at a local instance over integrated security, so a default local SQL Server needs no further setup. See [FAQ.md](FAQ.md) if it does not come up.

## Before you open a pull request

1. **Build the solution**, not just the application — the WiX installer is part of it:

   ```powershell
   dotnet build CerberusDashboard.sln -c Release -p:InstallerVersion=1.0.0
   ```

   The build must be clean. Do not add warnings.

2. **Run the regression checks:**

   ```powershell
   dotnet run --project .\Tests\CerberusDashboard.RegressionTests\CerberusDashboard.RegressionTests.csproj -c Release
   ```

   It exits nonzero on failure. It must pass.

3. **Exercise the change in the running app.** The dashboard is a single self-contained Razor view with hand-written JavaScript and no component tests, so UI changes are only really verified by loading the page and watching the panel update over SignalR.

## Code expectations

- **Match the surrounding style.** The project uses tabs in `.csproj`/`.wxs` files, four spaces in C#, and deliberately disables `Nullable` and `ImplicitUsings` — do not switch those on as a drive-by change.
- **Keep the view self-contained.** `Views/Dashboard/Index.cshtml` inlines its own CSS and JavaScript on purpose so the dashboard works air-gapped. Do not introduce a CDN reference, a bundler, or an npm dependency. Browser libraries are committed under `wwwroot/Content/scripts/`.
- **Monitoring queries stay read-only.** Never add a write, a DDL statement, or a configuration change against a monitored instance.
- **Prefer the least SQL permission that works.** If a new panel needs a broader grant, say so explicitly in the pull request and document it in the installation guide — operators have to request those grants from a DBA.
- **Fail fast on bad configuration.** `Services/EstateManager.cs` validates at startup and throws rather than silently monitoring the wrong thing. New settings should follow that pattern.
- **Never commit credentials**, real server names, or internal hostnames. Use `example.invalid` placeholders. This repository's history was deliberately flattened to purge previously committed secrets; do not reintroduce any.

## Changing configuration shape

If you add or rename a setting, update all of these together:

- `appsettings.json` and `appsettings.json.example`
- `CerberusInstaller/appsettings.json` (the credential-free file the MSI ships)
- [INSTALLATION_GUIDE.md](INSTALLATION_GUIDE.md), and [FAQ.md](FAQ.md) if it changes behaviour people ask about
- The validation in `Services/EstateManager.cs`

## Changing the installer

`CerberusInstaller/Package.wxs` owns the service registration and install layout. Keep the `UpgradeCode` and the permanent configuration component GUID stable across releases — changing either breaks upgrades for existing installs. Read `CerberusInstaller/README.md` first; it documents the release gate, which includes install, upgrade, rollback, and uninstall checks on an isolated VM. Building an MSI does not prove any of that works.

## Pull requests

Keep them focused on one thing. In the description, say what changed, why, and how you verified it — including whether you ran it against a real SQL Server and which panels you looked at. Call out anything that affects security posture, SQL permissions, the install layout, or configuration compatibility.

## Reporting bugs and vulnerabilities

Open an issue using the bug report template for ordinary bugs. For anything security-sensitive, follow [SECURITY.md](SECURITY.md) instead — do not open a public issue.
