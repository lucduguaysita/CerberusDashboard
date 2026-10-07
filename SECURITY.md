# Security Policy

## Reporting a vulnerability

Please do **not** open a public issue for a security problem.

Use GitHub's private vulnerability reporting on this repository: go to the **Security** tab and choose **Report a vulnerability**. That opens a private advisory visible only to the maintainers. If private reporting is unavailable to you, contact the repository owner directly through GitHub rather than filing an issue.

Include what you can: affected version or commit, configuration relevant to the issue, reproduction steps, and the impact you believe it has. Please do not include real credentials, production hostnames, or customer data in a report — redact them.

There is no formal response-time commitment for this project. Reports are triaged on a best-effort basis.

## Supported versions

Only the current `master` branch receives fixes. There are no maintained release branches, and no backports to older MSI versions.

Because releases are **self-contained**, the .NET runtime is bundled into the MSI. A runtime security update therefore requires rebuilding and redeploying the application — patching the machine's .NET installation does not update a deployed Cerberus Dashboard.

## Known design limitations

These are deliberate properties of the current design, not vulnerabilities. Please do not report them as such; they are documented so operators can plan around them.

- **No authentication or authorization.** The application has no user accounts and no role separation. Anyone who can reach the endpoint can view all monitored instances and switch the active estate for every connected user. Access control must come from the network and from a proxy in front of it.
- **The estate switch is CSRF-protected, not authorized.** It requires an antiforgery-protected POST. That prevents cross-site forgery; it does not decide who is permitted to switch.
- **`AllowedHosts` is not an access control.** It filters the HTTP `Host` header. It is neither authentication nor a client-IP allowlist.
- **The committed development configuration is intentionally permissive.** `appsettings.json` uses `"Urls": "http://0.0.0.0:5001"` and `"AllowedHosts": "*"` — all interfaces, plain HTTP, any host header. The MSI ships a different default: Kestrel on `localhost:5001` with `AllowedHosts` limited to `localhost;127.0.0.1;[::1]`.
- **No HTTPS by default.** Terminate TLS at a reverse proxy, or configure a certificate on the Kestrel endpoint, before any non-loopback exposure.
- **Secrets in `appsettings.json` are not encrypted.** DPAPI protects the ASP.NET Core data-protection keys, not connection-string passwords. Use protected deployment configuration and restrict file and backup ACLs.
- **MSI packages are unsigned** until a release-signing process is applied. Verify provenance and sign before distributing.

## Deployment hardening

Before exposing an instance beyond loopback:

1. Put an authenticating reverse proxy in front of it, terminating HTTPS and permitting SignalR WebSocket traffic. Keep the backend endpoint bound to `localhost`.
2. Open only the proxy port in the firewall, restricted to approved source addresses and network profiles.
3. Run the service under a least-privilege identity with only the SQL permissions the panels you use require. `VIEW SERVER STATE` — or `VIEW SERVER PERFORMANCE STATE` on SQL Server 2022+ — is typically sufficient. Avoid `sa` and sysadmin.
4. Use encrypted, certificate-validated SQL connections. `TrustServerCertificate=True` is acceptable on loopback only; provision a trusted certificate for remote servers.
5. Restrict ACLs on the installation directory and on `%ProgramData%\CerberusDashboard\DataProtection`.

Monitoring queries are read-only DMV and catalog reads; the application does not write to the instances it monitors.
