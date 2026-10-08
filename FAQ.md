# Frequently Asked Questions

- [General](#general)
- [Setup and configuration](#setup-and-configuration)
- [Estates](#estates)
- [Security](#security)
- [Deployment](#deployment)
- [Troubleshooting](#troubleshooting)

## General

### What does Cerberus Dashboard do?

It polls one or more SQL Server instances on a timer, reads server- and database-scoped DMVs, and pushes the results to connected browsers over SignalR. You get a live view of server health, agent jobs, blocking, expensive and active queries, file I/O, hot tables, databases, execution plans, active sessions, and partitions.

### Is it a replacement for a full monitoring product?

No. It is a focused, self-hosted dashboard with no alerting, no historical warehouse, and no user management. Metrics are held in memory for the running process, so restarting the service clears charts and snapshot history. If you need retention, paging, or per-user access control, pair it with a proper monitoring stack.

### Does it write to the monitored servers?

No. The monitoring queries are read-only DMV and catalog reads. The application does not create objects, change settings, or run jobs on the instances it monitors.

### What does it store?

Nothing on disk except its own configuration and ASP.NET Core data-protection keys. There is no database of its own. Snapshots live in process memory only.

## Setup and configuration

### What do I need to run it?

Windows x64, the .NET SDK pinned in `global.json` (10.0.401 or a later patch in the same band) for development, and a reachable SQL Server instance. Deployed via the MSI, the .NET runtime is bundled, so the target machine needs no separate install.

### How do I get started quickly?

```powershell
dotnet run
```

Then browse to `http://localhost:5001`. The committed `appsettings.json` monitors a single local instance over Windows integrated security, so a default local SQL Server works with no changes.

### Where does configuration live?

`appsettings.json`. In a deployed install that is `%ProgramFiles%\CerberusDashboard\appsettings.json`, which MSI upgrades deliberately never overwrite. See [CONNECTION_STRING_GUIDE.md](CONNECTION_STRING_GUIDE.md) for connection-string shapes and [INSTALLATION_GUIDE.md](INSTALLATION_GUIDE.md) for the full reference.

### Do I have to restart after changing settings?

Yes. Monitoring configuration is captured at startup. Restart the service — or the `dotnet run` process — after any change.

### How do I change the port?

Edit `Urls` for development, or the `Kestrel:Endpoints:Http:Url` endpoint for an MSI install. Note that in a deployed install the Kestrel endpoint wins: changing root-level `Urls` alone will not override it.

### Why do connection strings need `TrustServerCertificate=True` locally?

Microsoft.Data.SqlClient 7.x defaults to `Encrypt=True`, and a local SQL Server typically presents a self-signed certificate that the client will not trust. `TrustServerCertificate=True` skips that validation, which is fine on loopback. Do not carry it to a remote server — provision a trusted certificate with a matching server name instead.

### Can I use SQL authentication instead of integrated security?

Yes, but prefer integrated security with a least-privilege service identity. If you must use a login and password, supply them through protected deployment configuration rather than committing them. DPAPI protects the data-protection keys, not secrets sitting in `appsettings.json`.

## Estates

### What is an estate?

A named group of monitored instances — for example `Development` and `Production`. Each estate owns its own instance list, display names, and connection strings. Nothing is inherited from the root.

### Is the estate selection per user?

No, and this surprises people. One estate is active for the **entire service**. Switching it reloads every connected dashboard and clears charts, snapshots, tabs, and open modals for everyone.

### Does the selection survive a restart?

No. Selection is runtime-only; a restart returns to `DefaultEstate`. There is no UI for creating or editing estates — they come from configuration.

### Do I need `Estates` at all?

No. Omit it and the root-level settings work as a single estate named `Default`.

### Why does the service refuse to start after I edit estates?

Validation is deliberately strict and fails fast rather than silently monitoring the wrong thing. It rejects: an instance key with no matching `ConnectionStrings` entry in the same scope, a `MonitorRefreshSeconds` outside 5–86400, duplicate instance keys, an unparseable connection string, and a `DefaultEstate` that does not match a configured key. `DefaultEstate` is required once more than one estate exists.

### Can two estates monitor the same instance?

Yes. The same instance key can be reused across estates without sharing monitoring caches.

## Security

### Does it have login or user accounts?

**No.** There is no authentication and no role separation. Anyone who can reach the endpoint can view every monitored instance and switch the active estate for all users. This is the single most important thing to understand before exposing it anywhere.

### Isn't the estate switch protected?

It requires an antiforgery-protected POST, which prevents cross-site request forgery. That is not authorization — it does not establish *who* is allowed to switch.

### Is the default configuration safe to expose?

No. The committed development `appsettings.json` uses `"Urls": "http://0.0.0.0:5001"` and `"AllowedHosts": "*"`, which listens on every interface over plain HTTP and accepts any host header. The MSI ships a safer default: `localhost:5001` with `AllowedHosts` limited to `localhost;127.0.0.1;[::1]`.

### How should I expose it to other people?

Put it behind a reverse proxy that performs authentication and terminates HTTPS, forwarding to the local Kestrel endpoint and allowing SignalR traffic (WebSockets). Keep the backend endpoint on localhost, open only the proxy port in the firewall, and restrict source addresses. See Step 4 of [INSTALLATION_GUIDE.md](INSTALLATION_GUIDE.md).

### Does `AllowedHosts` restrict who can connect?

No. It filters the HTTP `Host` header. It is not authentication and not a client-IP allowlist.

### What SQL permissions does the monitoring identity need?

Server-scoped DMVs require `VIEW SERVER STATE`, or `VIEW SERVER PERFORMANCE STATE` on SQL Server 2022 and later. Some database-scoped reads need additional grants. Avoid `sa` or sysadmin — have a DBA scope permissions to the panels you actually use, then validate each one.

### Where do the data-protection keys live?

`%ProgramData%\CerberusDashboard\DataProtection`, protected with machine-level DPAPI. Because the protection is machine-scoped, the keys cannot simply be copied to another machine; moving them requires a deliberate key-migration procedure. The MSI does not own or delete that directory, so uninstalling leaves it in place.

## Deployment

### How do I build the installer?

```powershell
dotnet build CerberusDashboard.sln -c Release -p:InstallerVersion=1.0.0
```

The MSI lands in `CerberusInstaller\bin\Release\`. Use exactly three numeric version fields; major and minor are capped at 255 and build at 65535.

### What does the MSI actually do?

Installs per-machine to `%ProgramFiles%\CerberusDashboard`, registers `CerberusDashboard` as an automatic Windows service under LocalSystem, and starts it. There is no wizard and no destination-folder prompt. It creates no firewall rule and does not configure IIS.

### Is the MSI signed?

No. Builds are unsigned until a release-signing process is applied. Sign and verify the package with your own certificate before distributing it.

### Will an upgrade overwrite my configuration?

No. The `appsettings.json` component is permanent and never overwritten, so your settings survive upgrades and uninstalls. New sample estates are not merged in automatically either. Back it up before any upgrade regardless.

### Does uninstalling remove everything?

No, by design. It removes the service and packaged binaries but retains `appsettings.json` and the data-protection directory. Delete those only as a deliberate decommissioning step, after backup.

### Can I run it without installing a .NET runtime?

Yes. The MSI publishes a self-contained `win-x64` payload with the runtime bundled. The trade-off is that runtime security updates require rebuilding and redeploying.

### Can I run it as something other than LocalSystem?

Yes, but it is a deliberate reconfiguration. A custom account needs service logon rights, read and execute access to the installed files, write access to the data-protection directory, and its own SQL permissions. Re-verify all of it after upgrades.

## Troubleshooting

### The dashboard is empty and no instances appear.

A fresh MSI install has an empty instance list by design. Check `MonitoredInstances` in the selected estate — or at the root if `Estates` is absent — and confirm each key has a connection string in the same scope. Restart after editing.

### A tab shows "INSTANCE OFFLINE".

Confirm SQL Server is running, then test connectivity **as the service identity**, not just from your interactive SSMS session. Check authentication, DNS, TCP and named-instance reachability, firewall rules, and SQL permissions. For slow links, raise `Connection Timeout` to 30.

### A panel says no data is available.

Usually DMV permissions rather than a fault. See the SQL permissions answer above, and have a DBA confirm the version-specific grants for that panel.

### The service will not start.

Check Windows Event Viewer (Application, and System / Service Control Manager) plus the verbose MSI log. Then validate the JSON, confirm the estate validation rules above, and verify file and data-protection directory permissions for the service account.

### The port is already in use.

Change the Kestrel endpoint (see section 2.3 of the installation guide), update any proxy or firewall configuration, and restart. To check what holds the port:

```powershell
Get-NetTCPConnection -LocalPort 5001 -State Listen
```

### How do I run the tests?

```powershell
dotnet run --project .\Tests\CerberusDashboard.RegressionTests\CerberusDashboard.RegressionTests.csproj -c Release
```

It is a dependency-free executable that exits nonzero on failure, not a Test Explorer adapter. It uses a temporary loopback server with empty estates and ephemeral keys, and touches no production settings or SQL instances.
