## What and why

<!-- What changed, and the problem it solves. Link any related issue. -->

## How it was verified

<!-- Be specific. "Builds" is not verification for a UI or query change. -->

- [ ] `dotnet build CerberusDashboard.sln -c Release -p:InstallerVersion=0.0.1` is clean, with no new warnings
- [ ] `dotnet run --project .\Tests\CerberusDashboard.RegressionTests\CerberusDashboard.RegressionTests.csproj -c Release` passes
- [ ] Exercised in the running dashboard against a real SQL Server (say which panels below)

Panels checked:

## Impact

Tick anything this touches, and explain it below.

- [ ] Configuration shape (`appsettings.json`, the example, or the installer's copy)
- [ ] Required SQL permissions
- [ ] Installer layout, service registration, or upgrade behaviour
- [ ] Security posture (endpoint binding, allowed hosts, antiforgery, secrets)
- [ ] Documentation

<!--
Reminders:
- Monitoring queries must stay read-only.
- Keep the dashboard view self-contained: no CDN, bundler, or npm dependency.
- Never commit credentials or real hostnames; use example.invalid placeholders.
- Keep the WiX UpgradeCode and the permanent configuration component GUID stable.
- A configuration change means updating appsettings.json, appsettings.json.example,
  CerberusInstaller/appsettings.json, EstateManager validation, and the guides together.
-->
