# CerberusDashboard Windows installer

The installer is `CerberusInstaller.wixproj`, built with WiX 7. The legacy Visual Studio Installer Project (`.vdproj`) has been removed from the repository; it is replaced by this WiX project.

For step-by-step deployment, configuration, and troubleshooting, see [INSTALLATION_GUIDE.md](../INSTALLATION_GUIDE.md). This README covers installer builds, ownership, migration, and release validation.

## Prerequisites

- Windows x64 with the .NET SDK specified in the repository's `global.json` (10.0.401 or a later servicing patch in the same feature band).
- NuGet access for the pinned `WixToolset.Sdk` and `WixToolset.Util.wixext` 7.0.0 packages and the application's dependencies.
- The organization must meet WiX's applicable maintenance-fee/EULA requirements. Explicit acceptance (`AcceptEula=wix7`) is recorded in the project with the project owner's authorization. See https://docs.firegiant.com/wix/osmf/.
- Visual Studio is not required for command-line builds. To load the WiX project in Visual Studio, install a compatible HeatWave extension; the Visual Studio Installer Projects extension is no longer used.

## Build

Run from the repository root:

```powershell
dotnet build .\CerberusInstaller\CerberusInstaller.wixproj -c Release -p:InstallerVersion=2.0.0
```

Alternatively, `dotnet build .\CerberusDashboard.sln -c Release` builds the application and installer together. For application-only development, build `CerberusDashboard.csproj`.

Output: `CerberusInstaller\bin\Release\CerberusDashboard-2.0.0-win-x64.msi`.

The installer build deletes its private `CerberusInstaller\obj\Release\payload` staging directory, publishes a self-contained `win-x64` application, supplies the installer-specific configuration, and harvests the complete published file tree. It does not use the old root `publish` directory, the legacy FolderProfile, or a Visual Studio post-build event. Normal application publishing retains its existing configuration behavior.

Use exactly three numeric version fields and increment them for releases (for example, `-p:InstallerVersion=2.0.1`). The MSI version limits apply: major and minor must be at most 255, and build must be at most 65535. Replacement packages must have a higher version; same-version replacement is not supported, and downgrades to a lower version are blocked. Keep the WiX UpgradeCode and the permanent configuration component GUID stable across releases.

Builds are unsigned until a release-signing process is provided. Sign and verify the final MSI with the organization's code-signing certificate before distribution. Do not store certificates, passwords, or production connection strings in the repository or build parameters. Self-contained releases include the .NET runtime, so runtime security fixes require rebuilding and redeploying the application.

## Installation behavior

- Per-machine x64 installation to `%ProgramFiles%\CerberusDashboard` with elevation.
- Registers `CerberusDashboard` as an automatic Windows service, running as LocalSystem to match the legacy default. No application `/Install` or `/Uninstall` custom action is invoked.
- Stops and waits for the service during replacement/removal and starts it after installation.
- WiX's service-recovery extension configures restart after five seconds for all three recovery actions and resets the failure count after one day. This replaces the legacy 5/10/30-second sequence. The package does not explicitly enable recovery after an orderly nonzero service exit; verify recovery policy against operational requirements.
- Installs credential-free `appsettings.json`: no monitored SQL instances, a Kestrel HTTP endpoint bound to `localhost:5001`, and `AllowedHosts` restricted to `localhost;127.0.0.1;[::1]`. Remote access requires intentional changes to the Kestrel endpoint and allowed host names, plus appropriate authentication, TLS, and network protection. Changing root-level `Urls` alone does not override the installed Kestrel endpoint; the installer does not create firewall rules or configure IIS.
- Existing `appsettings.json` is never overwritten. Its MSI component is permanent, so configuration remains on upgrade and uninstall. Administrator access is required to edit it under Program Files; restrict access appropriately if it contains credentials.
- The application keeps its data-protection keys in `%ProgramData%\CerberusDashboard\DataProtection`. The MSI does not own or delete that directory. DPAPI machine-protected keys must stay on the same machine unless a separate key-migration procedure is used. Deployments that predate the de-branding change stored keys under `%ProgramData%\SITA\CerberusDashboard\DataProtection`; copy that directory to the new location (or let the application generate a fresh key ring) before starting the upgraded service.
- Uninstall removes the managed service and packaged binaries, but deliberately retains configuration and application data. Delete retained data only as an explicit decommissioning action after backup.

For a fresh deployment, install the MSI, stop the service, configure the installed `appsettings.json`, and restart the service. The default configuration can start safely without SQL access. Restrict network exposure until authentication/network controls suitable for the environment are in place.

## Estate profiles

The dashboard's **Estate** dropdown selects one active estate for the entire service. All connected dashboards reload after a switch, clearing charts, snapshots, tabs, and modal data. Selection is runtime-only: restarting the service returns to `DefaultEstate`. There is no profile creation/editing UI.

Use the sanitized named-estate example in [INSTALLATION_GUIDE.md](../INSTALLATION_GUIDE.md) as a starting point. The repository's `appsettings.json.example` illustrates the schema with a single credential-free LocalDB estate. Add the servers for the environment being monitored, and preserve the installed local-only endpoint and host restrictions unless remote access has been approved.

- Add an `Estates` object to the installed `appsettings.json`, with a uniquely named section for each estate (for example, `Development` and `Production`).
- Each estate contains `DisplayName`, `MonitorRefreshSeconds`, `MonitoredInstances`, `InstanceDisplayNames`, and its own `ConnectionStrings` object. Comma-separated instance keys must match connection-string keys; display names are matched by position. The same instance key may be reused in different estates without sharing monitoring caches.
- Set root-level `DefaultEstate` to a configured estate key. It is required when multiple estates are configured; a single estate is selected automatically.
- Refresh intervals must be between 5 and 86400 seconds. An omitted estate interval uses root-level `MonitorRefreshSeconds`, or 30 seconds. Root connection strings and instance lists are not inherited by named estates.
- An empty instance list is allowed. Invalid default keys, duplicate instance keys, missing connection strings, and invalid intervals fail startup instead of silently selecting another estate.
- Without `Estates`, existing root-level settings continue to work as a single **Default** estate. The dropdown is disabled when only one estate exists or the dashboard is disconnected.
- Restart the service after editing profile configuration. Monitoring settings are captured at startup, and switching does not rewrite the configuration file.

Back up the installed configuration before changing it. MSI upgrades deliberately retain existing `appsettings.json`; they do not add sample estates. Keep machine-wide settings such as `Logging`, `AllowedHosts`, and `Kestrel` at the root. Integrated authentication uses the service account; grant that identity appropriate SQL permissions or supply credentials through protected deployment configuration. Do not commit real credentials.

The application currently has no user authentication or role separation. Anyone with dashboard access can switch the active estate for everyone. Selection requires an antiforgery-protected POST, but this is not authorization: restrict dashboard access through appropriate network/authentication controls. In-flight read-only SQL work may finish after a switch; its revision-tagged results cannot populate the new estate's dashboard.

Before release, test two browser sessions with two estates that reuse an instance key. Switching in either browser should reload both, change the refresh interval, and clear file-I/O history and open modals. Also test switching during a slow query, reconnecting after another user switches, an empty estate, rejection of unknown/stale selections, and service restart returning to the configured default.

## Manual migration from legacy MSI

Automatic legacy removal is intentionally disabled. The new installer detects both historical UpgradeCodes and refuses to install while either legacy product is detected. It also blocks an existing service when no related WiX installation is detected, protecting against incomplete legacy uninstalls and manually registered services.

1. Identify the actual installed product and installation directory in Windows Installed Apps. Do not assume an old MSI in this repository matches the deployed version.
2. Stop `CerberusDashboard`. Back up its installed `appsettings.json`, any environment-specific settings or certificates, and the data-protection directory to a secure location outside the installation directory. Record the service account, endpoint, recovery settings, and any custom permissions. Never copy those backups into the build payload.
3. Uninstall the legacy MSI through Installed Apps or its verified MSI product identity. Keep an uninstall log and the original package for recovery.
4. Verify `Get-Service -Name CerberusDashboard -ErrorAction SilentlyContinue` returns no service. If removal failed or is pending, diagnose the legacy uninstall, close applications holding service handles, and reboot if needed. Do not bypass the new installer's guard or simply install over a running/orphaned service.
5. Install the new WiX MSI. If it reports another legacy product, resolve that product's removal as well. Back up and explicitly remove obsolete binaries left in the installation directory by the legacy uninstaller; the new MSI cannot own arbitrary legacy files.
6. Stop the new service before restoring the backed-up configuration. Restore needed settings carefully, retaining the intended endpoint and security controls. Keep the existing data-protection keys on the same machine; restore them only if necessary. Confirm file permissions.
7. Start the service and verify dashboard access and expected SQL monitoring. A custom non-LocalSystem account requires deliberate reconfiguration and permissions; the installer otherwise uses LocalSystem.

Do not run the application's `/Install` or `/Uninstall` flags for a WiX-managed deployment: doing so bypasses MSI ownership and repair/upgrade behavior.

## Validation before releasing

Use an isolated Windows VM and snapshots. Do not install test packages on a development workstation or a production server.

- Build from a clean checkout on an agent without Visual Studio or existing publish output; repeat the build to catch stale-file dependence.
- Confirm x64 MSI metadata, embedded cabinets, the service install/control records, and detection-only entries for both legacy identities.
- Compare MSI files against the clean publish payload, including the .NET runtime, SQL client native libraries, runtime/dependency JSON, and `wwwroot` assets. Confirm only the sanitized installer settings are shipped, not local or environment-specific settings.
- Fresh install without a separately installed .NET runtime; confirm service startup, localhost HTTP response, and dashboard assets.
- Attempt installation with each legacy MSI identity and with an orphaned service; confirm installation is blocked without changing the existing deployment.
- Configure the dashboard, upgrade from a lower WiX version while the service is running, and verify configuration, keys, recovery settings, service restart, and SQL connectivity.
- Test higher-version replacement, same-version replacement rejection, repair using the original MSI, lower-version rejection, and rollback after a controlled installation failure. Verify rollback restores the previous service/binaries and leaves configuration intact.
- Uninstall and verify service/binary removal while configuration and data-protection keys remain. Test reinstall with retained settings.
- Review verbose MSI logs and signing verification before promotion. For example, use `msiexec /i <package.msi> /L*v <install.log>` from an elevated terminal on the test VM.

Building and inspecting the MSI does not prove runtime install, upgrade, or rollback behavior. These VM checks are a release gate.
