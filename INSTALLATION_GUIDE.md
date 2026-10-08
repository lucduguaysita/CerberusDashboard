# Cerberus Dashboard — Installation Guide

## Overview

Cerberus Dashboard is a SQL Server monitoring application built on ASP.NET Core (.NET 10). The supported WiX 7 MSI installs a self-contained Windows x64 application as a Windows Service and serves a real-time dashboard via SignalR. The target machine does not need a separately installed .NET runtime or NuGet access; SQL Server and browser connectivity are still required.

---

## Prerequisites

| Requirement | Requirement / version | Notes |
|:---|:---|:---|
| Windows Server (x64) | Use a Windows Server release supported by .NET 10 | Use a currently serviced OS; validate the target OS before rollout |
| Windows Desktop (x64) | Use a Windows release supported by .NET 10 | Use a supported Windows 11 release for desktop testing |
| .NET runtime on target | .NET 10 bundled | Self-contained MSI; rebuild and redeploy to update the bundled runtime |
| SQL Server | An instance validated for this deployment | DBA must verify DMV compatibility and permissions for the SQL version; SQL access is not provisioned by the MSI |
| Service / SQL identity | LocalSystem by default; SQL monitoring permissions required | Integrated security uses the service identity; see SQL permissions below |
| Dashboard endpoint | `http://localhost:5001` | Local-only by default; change Kestrel and `AllowedHosts` deliberately for remote access |
| Installation rights / storage | Administrator; space for complete self-contained payload | Allow additional space for installer staging, backups, and application data; size varies by release |

> **Air-gapped targets:** transfer an approved, signature-verified MSI through the authorized process. The runtime and browser scripts are bundled. Building the package requires the pinned SDK and available NuGet dependencies on the build machine; do not rely on internet access on the target.

---

## Build the MSI (Release Maintainers Only)

End users install the approved MSI; they do not need the SDK or Visual Studio. Build on Windows x64 using `global.json`: .NET SDK 10.0.401 or a later servicing patch in the same feature band. Restore the pinned WiX SDK / Util extension 7.0.0 and application dependencies from approved NuGet sources. The project records `AcceptEula=wix7`; the organization must meet the applicable WiX maintenance-fee/EULA requirements. A compatible HeatWave extension is needed only to load WiX in Visual Studio.

From the repository root:

```powershell
dotnet build .\CerberusInstaller\CerberusInstaller.wixproj -c Release -p:InstallerVersion=1.0.0
```

Output: `CerberusInstaller\bin\Release\CerberusDashboard-1.0.0-win-x64.msi`. Building `CerberusDashboard.sln` also builds the installer; build `CerberusDashboard.csproj` alone for application-only development.

The build publishes a fresh self-contained `win-x64` payload under `CerberusInstaller\obj\Release\payload` and supplies sanitized installer settings. Do not deploy an old root `publish` folder or legacy installer. Use exactly three numeric version fields; major/minor are limited to 255 and build to 65535. Increment the version for replacements. Builds are unsigned until release signing is applied: sign and verify the final MSI before distribution. Complete the isolated-VM release checks in `CerberusInstaller\README.md`, including install, upgrade, rollback, and uninstall.

## Step 1 — Run the Installer

1. Use the approved `CerberusDashboard-<version>-win-x64.msi`. From an elevated PowerShell window, run the following example (adjust the package path/version and choose a writable log path):

   ```powershell
   msiexec.exe /i ".\CerberusInstaller\bin\Release\CerberusDashboard-1.0.0-win-x64.msi" /qn /norestart /L*v "$env:TEMP\CerberusDashboard-install.log"
   ```

2. The MSI installs per-machine to `%ProgramFiles%\CerberusDashboard`, registers the automatic `CerberusDashboard` service (display name: Cerberus Dashboard) under LocalSystem, and starts it. This package has no authored installation wizard or destination-folder selection.

3. Wait for Windows Installer to finish and review its result and verbose log. Fresh settings contain no monitored instances, bind only to `localhost:5001`, and restrict allowed hosts to `localhost`, `127.0.0.1`, and `[::1]`. No IIS setup or firewall rule is created.

> Verify with `Get-Service -Name CerberusDashboard`, or open `services.msc` and find Cerberus Dashboard. Recovery restarts the service after five seconds for each of the three failure actions; the failure count resets after one day. Do not use the application `/Install` or `/Uninstall` flags for an MSI-managed deployment.

---

## Step 2 — Configure appsettings.json

Run `Stop-Service -Name CerberusDashboard`. Back up the installed `%ProgramFiles%\CerberusDashboard\appsettings.json` outside the installation directory, then edit it with an elevated editor. Merge settings rather than blindly replacing the file. MSI upgrades never overwrite it; reinstalling may reuse retained settings. The repository `appsettings.json.example` contains a single credential-free local estate: add the servers for the environment being monitored before use.

### 2.1 — Single-estate Monitoring Settings

```json
{
  "MonitorRefreshSeconds": 30,
  "MonitoredInstances": "SQLMON_DEFAULT",
  "InstanceDisplayNames": "Primary SQL"
}
```

| Key | Description |
|:---|:---|
| `MonitorRefreshSeconds` | Monitoring interval in seconds. Named estates require an integer from 5 to 86400; use that range for all deployments. The default is 30 seconds. |
| `MonitoredInstances` | Comma-separated instance keys, each with a matching `ConnectionStrings` entry in the same scope. An empty list is allowed and is the fresh-install default. |
| `InstanceDisplayNames` | Comma-separated friendly names matched by position to `MonitoredInstances`. Supply one name per instance to avoid ambiguous labels. |

### 2.2 — Connection Strings

Add one connection string per monitored instance in the same scope: at the root for a single Default estate, or within each named estate. Keep key spelling consistent. The following example uses integrated security and a placeholder server; replace it with an approved SQL endpoint.

```json
"ConnectionStrings": {
  "SQLMON_DEFAULT": "Server=sql-monitor.example.invalid;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=False;Connection Timeout=10;"
}
```

#### Common Connection String Patterns

| Scenario | Connection String |
|:---|:---|
| Default instance, SQL authentication | `Server=sql-monitor.example.invalid;Database=master;User Id=<monitor-login>;Password=<secret>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=10;` |
| Named instance in JSON, SQL authentication | `Server=sql-monitor.example.invalid\\MONITOR;Database=master;User Id=<monitor-login>;Password=<secret>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=10;` |
| Explicit TCP port, SQL authentication | `Server=tcp:sql-monitor.example.invalid,1433;Database=master;User Id=<monitor-login>;Password=<secret>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=10;` |
| Windows authentication (service identity) | `Server=sql-monitor.example.invalid;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=False;Connection Timeout=10;` |

### 2.3 — Web Server Port (Optional)

The MSI supplies a Kestrel endpoint on `localhost:5001`. Edit that endpoint to change the address or port. Keep `localhost` for local-only use; do not expect the root `Urls` setting alone to override an existing Kestrel endpoint.

```json
"Kestrel": {
  "Endpoints": {
    "Http": { "Url": "http://localhost:5001" }
  }
},
"AllowedHosts": "localhost;127.0.0.1;[::1]"
```

> Remote access requires both an intentional listening endpoint and an `AllowedHosts` list containing the approved host names (without schemes or ports). `AllowedHosts` is host-header filtering, not authentication or a client-IP allowlist. See Step 4 before widening access.

### 2.4 — Complete Named-estate Example

```json
{
  "DefaultEstate": "Development",
  "Estates": {
    "Development": {
      "DisplayName": "Development",
      "MonitorRefreshSeconds": 15,
      "MonitoredInstances": "SQLMON_DEFAULT",
      "InstanceDisplayNames": "Development SQL",
      "ConnectionStrings": {
        "SQLMON_DEFAULT": "Server=sql-dev.example.invalid;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=False;Connection Timeout=10;"
      }
    },
    "Production": {
      "DisplayName": "Production",
      "MonitorRefreshSeconds": 30,
      "MonitoredInstances": "",
      "InstanceDisplayNames": "",
      "ConnectionStrings": {}
    }
  },
  "Kestrel": {
    "Endpoints": {
      "Http": { "Url": "http://localhost:5001" }
    }
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "localhost;127.0.0.1;[::1]"
}
```

Replace the placeholder SQL server before use and grant the actual service identity access. The `Production` estate is intentionally empty. Validate JSON before starting the service; escape named-instance backslashes as `\\` inside JSON strings. Do not commit secrets.

After saving, use `Start-Service -Name CerberusDashboard` if stopped, or `Restart-Service -Name CerberusDashboard` if running. Monitoring configuration is captured at startup; restart after every configuration change.

---

### 2.5 — Estate Profiles and SQL Identity

Without `Estates`, root-level monitoring settings remain a single Default estate. With named estates, each section owns its instance list, display names, and connection strings. `DisplayName` is optional and falls back to the estate key. Set `DefaultEstate` when multiple estates exist; a single estate is selected automatically if no default is supplied. An explicit default must always match a configured key.

Each named estate can set `MonitorRefreshSeconds` (5–86400). If omitted, the root interval is used, or 30 seconds. Root instance lists and connection strings are not inherited. Empty estates are allowed; missing/invalid connection strings and duplicate instance keys fail startup. Keep `Logging`, `AllowedHosts`, and `Kestrel` at the root.

The Estate dropdown chooses one active estate for the entire service, not one per user. Switching reloads all connected dashboards and clears charts, snapshots, tabs, and modal data. The same instance key can be reused across estates without sharing monitoring caches. Selection is runtime-only; restart returns to `DefaultEstate`. The dropdown is disabled for one estate or while disconnected. There is no estate editing UI.

For Integrated Security, grant SQL access to the identity actually running CerberusDashboard, not your interactive account. Any custom service identity requires service logon rights, file access, and data-protection directory permissions; record and verify it after upgrades. For SQL authentication, replace placeholders with an approved least-privilege login and protect the secret outside source control. SQL Server 2022+ changes several DMV permission requirements; have the DBA validate all monitored panels.

## Step 3 — Open the Dashboard

On the server, open a current Edge or Chromium browser and navigate to the configured endpoint. With fresh MSI defaults, use:

```
http://localhost:5001
```

Confirm the dashboard and bundled scripts load, SignalR connects, and configured instances return data. No SQL instances are monitored until configured. For multiple estates, verify switching in one browser updates all connected dashboards; restarting the service must return to `DefaultEstate`.

---

## Step 4 — Remote Access (Optional)

The application has no built-in user authentication or role separation. Anyone with dashboard access can view monitoring data and switch the active estate for all users. Keep the endpoint local until approved authentication and network controls are in place; do not expose it directly to the internet.

1. Prefer a secured reverse proxy with authentication and HTTPS, forwarding to the local Kestrel endpoint and allowing SignalR traffic. Configure the correct host-header allowlist. Proxy, TLS, and firewall configuration are operational tasks; the MSI does not provide them.

2. If direct remote Kestrel access is approved, set the endpoint to the intended interface and configure HTTPS before exposure. A wildcard such as `http://0.0.0.0:5001` listens on all IPv4 interfaces and is unencrypted; it is not a secure production default.

3. Create an inbound TCP firewall rule only for the chosen listening port, restricted to approved source addresses and required network profiles. For a reverse proxy, expose only the proxy port and keep the backend endpoint local.

4. Restart the service after changes. Test from an approved remote client using the configured host name, and verify unauthorized networks/users cannot reach the dashboard. Check dashboard loading, SignalR updates, and estate switching through any proxy.

---

## Upgrades and Legacy Installations

Before any upgrade, back up installed configuration, environment-specific settings/certificates, and `%ProgramData%\CerberusDashboard\DataProtection` to a secure location outside the install directory. Record the service identity, endpoint, recovery policy, and custom ACLs. Data-protection keys use machine-level DPAPI and cannot simply be moved to another machine.

For an existing WiX deployment, install an approved higher-version MSI and keep a verbose log. The MSI stops/waits for the service during replacement and starts it after installation. Existing `appsettings.json` is never overwritten; new sample estates are not merged automatically. Downgrades are blocked and same-version replacement is not supported. Verify settings, service identity/recovery, access controls, SQL connectivity, and estate behavior after upgrade.

Legacy MSI or manually registered service: do not install over it. The new MSI detects both historical installer identities and blocks an unrelated existing `CerberusDashboard` service. Back up and record the old deployment, stop it, uninstall the identified legacy product through Installed Apps, and verify `Get-Service -Name CerberusDashboard -ErrorAction SilentlyContinue` returns no service. If removal is incomplete, diagnose it and close service handles or reboot as needed; do not bypass the installer guards.

After legacy removal, securely back up and explicitly remove obsolete binaries left in the installation directory before deploying the new package; the new MSI cannot manage those files. Install the WiX MSI, stop the new service, and carefully merge the backed-up configuration. Preserve intended endpoint/security controls and same-machine data-protection keys. Reapply and verify any approved non-LocalSystem service account and ACLs, then start and validate monitoring. See `CerberusInstaller\README.md` for the full migration and release checklist.

## Uninstalling

1. Back up configuration and required application data, then use Windows Installed Apps / Programs and Features to uninstall the registered product.

2. Select CerberusDashboard and choose Uninstall with administrator rights. Retain the original MSI and installation logs for support or recovery.

3. The MSI stops and removes the managed Windows service and packaged binaries. It deliberately retains `appsettings.json` and does not own or delete `%ProgramData%\CerberusDashboard\DataProtection`.

Reinstallation can reuse retained configuration and its network/SQL settings. Delete retained configuration, secrets, and application data only as an explicit decommissioning action after backup. Do not run the application `/Uninstall` flag for an MSI-managed installation.

---

## Troubleshooting

### No monitored instances / empty estate

- A fresh MSI has an empty instance list by design. Check `MonitoredInstances` in the selected estate (or at the root when `Estates` is absent).
- Each key needs a connection string in the same scope. Named estates do not inherit root-level instance lists or connection strings. `DefaultEstate` must match a configured estate key.
- Restart the service after any `appsettings.json` change.

### Instance tab shows "INSTANCE OFFLINE"

- Confirm the SQL Server service is running on the target machine.
- Test SQL connectivity under the service identity, not just your interactive SSMS account. Verify authentication, DNS, SQL TCP/named-instance connectivity, firewall rules, and required SQL permissions.
- Check `Connection Timeout` — increase to 30 for slow networks.
- For certificate errors, provision a trusted SQL Server certificate with a matching server name. `TrustServerCertificate=True` bypasses certificate validation; use it only as an explicit, documented exception, not a blanket fix.

### "No file I/O data available" in the Data File I/O panel

- Have the DBA check the actual SQL identity and version-specific DMV permissions. Many monitored server DMVs require `VIEW SERVER STATE` before SQL Server 2022, and `VIEW SERVER PERFORMANCE STATE` on SQL Server 2022 and later.

A DBA should grant the applicable permission to the monitoring login, for example `GRANT VIEW SERVER STATE TO [monitor_login];` or, on SQL Server 2022+, `GRANT VIEW SERVER PERFORMANCE STATE TO [monitor_login];`. Database-scoped DMV and metadata access can require additional permissions; validate every required panel.

### "No expensive queries" / "No active queries"

- These panels depend on DMV access (including `sys.dm_exec_query_stats` and `sys.dm_exec_requests`) and available workload/cache data. Check version-appropriate permissions and the service identity before interpreting an empty panel as a fault.

### Service fails to start

- Check Windows Event Viewer: Application and System / Service Control Manager, plus the verbose MSI log for installation failures. Named-estate validation rejects missing/default keys, missing or invalid connection strings, duplicate instance keys, and intervals outside 5–86400 seconds.
- Check valid JSON, service logon configuration, Read & Execute access to installed files, and write access to `%ProgramData%\CerberusDashboard\DataProtection`. A custom service account needs deliberate permission configuration.
- Check the configured port (5001 by default) for conflicts, for example `Get-NetTCPConnection -LocalPort 5001 -State Listen`. A missing listener can indicate startup failure; inspect logs before changing settings.

### Port already in use

Edit `Kestrel:Endpoints:Http:Url` (see section 2.3), adjust any proxy/firewall configuration, and restart the service. A rejected host name can also require updating `AllowedHosts`; do not disable access controls just to make the request succeed.

## Security Recommendations

| Recommendation | Detail |
|:---|:---|
| Least-privilege service / SQL identity | LocalSystem is the MSI default, not a least-privilege recommendation. Use an approved dedicated identity when required. For remote integrated authentication in a domain, LocalSystem typically uses the machine account (`DOMAIN\HOST$`); grant and test the intended identity. Avoid `sa`/sysadmin; have the DBA scope permissions to the monitored features. |
| Restrict dashboard access | Keep `localhost` binding unless a secured remote-access design is approved. The estate switch uses antiforgery protection, but that is not authorization. Restrict both dashboard and service endpoints through authentication and network controls. |
| Protect credentials and retained data | Use protected deployment configuration or securely managed environment variables; restrict file and backup ACLs. The application does not automatically decrypt arbitrary encrypted connection-string values. DPAPI protects data-protection keys, not passwords stored in `appsettings.json`. |
| HTTPS | Configure and validate HTTPS at the approved proxy or Kestrel endpoint and ensure service access to the certificate private key. Use encrypted, certificate-validated SQL connections. Rebuild/redeploy self-contained releases for .NET runtime security updates. |

Data-protection keys: `%ProgramData%\CerberusDashboard\DataProtection`. The application persists machine-protected keys here; the MSI does not own or delete the directory. Back up securely and retain on the same machine unless an approved key-migration process is used.

## File Reference

| File | Purpose |
|:---|:---|
| `CerberusDashboard.exe` | Application entry point and Windows Service host; keep the complete published payload, not just the executable |
| `appsettings.json` | Preserved runtime configuration: estate profiles, SQL connections, logging, Kestrel endpoints, and `AllowedHosts` |
| `CerberusDashboard.runtimeconfig.json` / `.deps.json` | Runtime and dependency metadata shipped with the self-contained payload; IIS is not required for the Windows Service deployment |
| `wwwroot\Content\scripts\signalr.min.js` | Bundled SignalR client — no CDN required |
| `wwwroot\Content\scripts\jquery-3.6.0.min.js` | Bundled jQuery — no CDN required |
