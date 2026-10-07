# Estate regression checks

Run from the repository root:

`dotnet run --project .\Tests\CerberusDashboard.RegressionTests\CerberusDashboard.RegressionTests.csproj -c Release`

This dependency-free executable exits with a nonzero status on failure. It is not a Test Explorer test adapter and does not require additional NuGet test packages.

Checks include legacy configuration, profile validation, shared instance-key isolation, fresh monitor caches, revision/session rejection, concurrent selection, connection tracking, Razor rendering, antiforgery enforcement, and two-client SignalR notification/reconnection behavior. The HTTP checks start a temporary loopback server on an ephemeral port with empty estates and ephemeral data-protection keys. No production settings or SQL connections are used.

Browser release checks (including chart/modal resets and switching during real SQL work) are listed in `CerberusInstaller/README.md`.
