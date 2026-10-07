using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CerberusDashboard.Controllers;
using CerberusDashboard.Hubs;
using CerberusDashboard.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    passed++;
}

void Reject<T>(Action action, string name) where T : Exception
{
    try { action(); }
    catch (T) { Check(true, name); return; }
    throw new InvalidOperationException("FAIL: " + name);
}

IConfiguration Configuration(Dictionary<string, string?> values)
    => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

Dictionary<string, string?> Profiles() => new()
{
    ["DefaultEstate"] = "Alpha",
    ["Estates:Alpha:DisplayName"] = "Alpha estate",
    ["Estates:Alpha:MonitorRefreshSeconds"] = "10",
    ["Estates:Alpha:MonitoredInstances"] = "Shared",
    ["Estates:Alpha:ConnectionStrings:Shared"] = "Server=alpha.example.invalid;Integrated Security=True;",
    ["Estates:Beta:DisplayName"] = "Beta estate",
    ["Estates:Beta:MonitorRefreshSeconds"] = "45",
    ["Estates:Beta:MonitoredInstances"] = "Shared",
    ["Estates:Beta:ConnectionStrings:Shared"] = "Server=beta.example.invalid;Integrated Security=True;"
};

var legacy = new EstateManager(Configuration(new()
{
    ["MonitoredInstances"] = "Legacy",
    ["ConnectionStrings:Legacy"] = "Server=legacy.example.invalid;Integrated Security=True;"
}));
Check(legacy.Active.Key == "Default" && legacy.Active.Monitor.GetInstanceKeys().Single().Key == "Legacy", "Legacy settings remain the Default estate");
Check(legacy.Active.Monitor.RefreshSeconds == 30, "Missing legacy refresh interval defaults to 30");

var config = Configuration(Profiles());
var manager = new EstateManager(config);
var alpha = manager.Active;
Check(alpha.Key == "Alpha" && alpha.Monitor.RefreshSeconds == 10, "Configured startup estate and interval");
var stateJson = JsonSerializer.Serialize(manager.GetState());
Check(!stateJson.Contains("ConnectionString") && !stateJson.Contains("example.invalid") && !stateJson.Contains("Monitor"), "Public estate state contains no monitoring configuration");
manager.Select("alpha", alpha.Revision, manager.SessionId);
Check(ReferenceEquals(alpha, manager.Active), "Selecting the current estate is an idempotent case-insensitive operation");
Reject<ArgumentException>(() => manager.Select("Missing", alpha.Revision, manager.SessionId), "Unknown estate rejected");
Check(ReferenceEquals(alpha, manager.Active), "Rejected selection does not change state");
manager.Select("Beta", alpha.Revision, manager.SessionId);
var beta = manager.Active;
Check(beta.Revision == alpha.Revision + 1 && beta.Monitor.RefreshSeconds == 45, "Switch updates revision and interval");
Check(beta.Monitor.GetInstanceKeys().Single().ConnectionString.Contains("beta.example.invalid") && alpha.Monitor.GetInstanceKeys().Single().ConnectionString.Contains("alpha.example.invalid"), "Same instance key remains bound to its original estate");
Reject<InvalidOperationException>(() => manager.Select("Alpha", alpha.Revision, manager.SessionId), "Stale selection rejected");
Reject<InvalidOperationException>(() => manager.Select("Alpha", beta.Revision, "old-service-session"), "Selection from an old service session rejected");
manager.Select("Alpha", beta.Revision, manager.SessionId);
Check(!ReferenceEquals(alpha.Monitor, manager.Active.Monitor), "Switching back creates fresh monitoring caches");
config["Estates:Beta:ConnectionStrings:Shared"] = "Server=changed.example.invalid;Integrated Security=True;";
manager.Select("Beta", manager.Active.Revision, manager.SessionId);
Check(manager.Active.Monitor.GetInstanceKeys().Single().ConnectionString.Contains("beta.example.invalid"), "Configuration is captured at startup");
Check(new EstateManager(config).Active.Key == "Alpha", "Restart restores configured default");

var tracker = new ConnectionTracker();
tracker.SetActive("old", "Shared", alpha.Revision);
tracker.SetActive("new", "Shared", beta.Revision);
Check(tracker.GetAll(beta.Revision).Single().Key == "new", "Instance tracking excludes stale estate revisions");
tracker.Remove("new");
Check(!tracker.GetAll(beta.Revision).Any(), "Disconnected clients are removed");

foreach (string badInterval in new[] { "0", "4", "86401", "invalid" })
{
    var values = Profiles();
    values["Estates:Alpha:MonitorRefreshSeconds"] = badInterval;
    Reject<InvalidOperationException>(() => new EstateManager(Configuration(values)), "Invalid refresh interval rejected: " + badInterval);
}
foreach (string defaultKey in new[] { "", "Missing" })
{
    var values = Profiles();
    values["DefaultEstate"] = defaultKey;
    Reject<InvalidOperationException>(() => new EstateManager(Configuration(values)), "Missing or invalid default rejected: " + defaultKey);
}
var missingConnection = Profiles();
missingConnection.Remove("Estates:Beta:ConnectionStrings:Shared");
missingConnection["ConnectionStrings:Shared"] = "Server=root.example.invalid;Integrated Security=True;";
Reject<InvalidOperationException>(() => new EstateManager(Configuration(missingConnection)), "Named estates never inherit root connection strings");
var duplicateInstance = Profiles();
duplicateInstance["Estates:Alpha:MonitoredInstances"] = "Shared,shared";
Reject<InvalidOperationException>(() => new EstateManager(Configuration(duplicateInstance)), "Duplicate instance keys rejected");
var invalidConnection = Profiles();
invalidConnection["Estates:Alpha:ConnectionStrings:Shared"] = "invalid connection string";
Reject<InvalidOperationException>(() => new EstateManager(Configuration(invalidConnection)), "Malformed connection strings rejected");
var single = new EstateManager(Configuration(new()
{
    ["MonitorRefreshSeconds"] = "20",
    ["Estates:Empty:DisplayName"] = "Empty estate"
}));
Check(single.Active.Key == "Empty" && single.Active.Monitor.RefreshSeconds == 20 && single.Active.Monitor.GetInstanceKeys().Count == 0, "Single empty estate and root refresh fallback");
var concurrent = new EstateManager(Configuration(Profiles()));
long originalRevision = concurrent.Active.Revision;
int selections = 0;
Parallel.For(0, 20, _ =>
{
    try
    {
        concurrent.Select("Beta", originalRevision, concurrent.SessionId);
        Interlocked.Increment(ref selections);
    }
    catch (InvalidOperationException) { }
});
Check(selections == 1 && concurrent.Active.Revision == originalRevision + 1, "Concurrent switches allow only one matching revision");

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    ApplicationName = typeof(DashboardController).Assembly.GetName().Name,
    EnvironmentName = "Development"
});
builder.Configuration.Sources.Clear();
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["DefaultEstate"] = "Alpha",
    ["Estates:Alpha:DisplayName"] = "Alpha <test>",
    ["Estates:Alpha:MonitorRefreshSeconds"] = "10",
    ["Estates:Beta:DisplayName"] = "Beta",
    ["Estates:Beta:MonitorRefreshSeconds"] = "45"
});
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.ClearProviders();
builder.Services.AddControllersWithViews().AddApplicationPart(typeof(DashboardController).Assembly).AddNewtonsoftJson();
builder.Services.AddSignalR().AddNewtonsoftJsonProtocol(options =>
    options.PayloadSerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver());
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddSingleton<EstateManager>();
builder.Services.AddSingleton<ConnectionTracker>();
await using var app = builder.Build();
app.UseRouting();
app.MapControllerRoute("default", "{controller=Dashboard}/{action=Index}/{id?}");
app.MapHub<MonitorHub>("/signalr/monitorHub");
await app.StartAsync();
try
{
    using var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(app.Urls.Single()) };
    string html = await client.GetStringAsync("/");
    Check(html.Contains("id=\"estate-select\"") && html.Contains("Shared by all dashboard users"), "Dashboard renders shared estate selector");
    var tokenMatch = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
    Check(tokenMatch.Success, "Dashboard emits antiforgery token");
    string token = WebUtility.HtmlDecode(tokenMatch.Groups[1].Value);
    var wsUri = new UriBuilder(client.BaseAddress) { Scheme = "ws", Path = "/signalr/monitorHub" }.Uri;
    using var first = await SignalRPeer.Connect(wsUri);
    using var second = await SignalRPeer.Connect(wsUri);
    var firstStateArguments = (await first.Receive("receiveEstateState")).GetProperty("arguments");
    Check(firstStateArguments.GetArrayLength() == 2 && firstStateArguments[1].GetBoolean(), "Connection initialization is distinguished from estate broadcasts");
    var firstState = firstStateArguments[0];
    var secondState = (await second.Receive("receiveEstateState")).GetProperty("arguments")[0];
    long revision = firstState.GetProperty("Revision").GetInt64();
    string sessionId = firstState.GetProperty("SessionId").GetString()!;
    Check(firstState.GetProperty("ActiveEstate").GetString() == "Alpha" && secondState.GetProperty("Revision").GetInt64() == revision, "Two clients receive the same initial estate");
    await first.Invoke("RequestInitialData");
    var initial = (await first.Receive("receiveSnapshot")).GetProperty("arguments");
    Check(initial[0].GetProperty("Instances").GetArrayLength() == 0 && initial[1].GetInt64() == revision, "Empty-estate snapshots carry revision without SQL calls");

    async Task<HttpStatusCode> Select(string estate, long expected, bool includeToken = true)
    {
        var form = new Dictionary<string, string>
        {
            ["estateKey"] = estate,
            ["revision"] = expected.ToString(),
            ["sessionId"] = sessionId
        };
        if (includeToken) form["__RequestVerificationToken"] = token;
        using var response = await client.PostAsync("/Dashboard/SelectEstate", new FormUrlEncodedContent(form));
        return response.StatusCode;
    }

    Check(await Select("Beta", revision, false) == HttpStatusCode.BadRequest, "Selection without antiforgery token is rejected");
    Check(await Select("Missing", revision) == HttpStatusCode.BadRequest, "Unknown estate POST is rejected");
    Check(await Select("Beta", revision) == HttpStatusCode.NoContent, "Valid estate selection succeeds");
    var changedFirst = (await first.Receive("receiveEstateState")).GetProperty("arguments")[0];
    var changedSecond = (await second.Receive("receiveEstateState")).GetProperty("arguments")[0];
    Check(changedFirst.GetProperty("ActiveEstate").GetString() == "Beta" && changedSecond.GetProperty("Revision").GetInt64() == revision + 1, "Switch is broadcast to both connected clients");
    Check(await Select("Alpha", revision) == HttpStatusCode.Conflict, "Stale page POST returns conflict");
    await first.Invoke("RequestInitialData");
    var stale = (await first.Receive("receiveError")).GetProperty("arguments");
    Check(stale[0].GetString()!.Contains("active estate has changed") && stale[1].GetInt64() == revision, "Old connection cannot read from newly selected estate");
    using var reconnected = await SignalRPeer.Connect(wsUri);
    var latest = (await reconnected.Receive("receiveEstateState")).GetProperty("arguments")[0];
    Check(latest.GetProperty("ActiveEstate").GetString() == "Beta", "New or reconnected clients see current selection");
    await reconnected.Invoke("RequestInitialData");
    var snapshot = (await reconnected.Receive("receiveSnapshot")).GetProperty("arguments");
    Check(snapshot[0].GetProperty("RefreshIntervalSeconds").GetInt32() == 45 && snapshot[1].GetInt64() == revision + 1, "New snapshots use selected estate interval and revision");
}
finally
{
    await app.StopAsync();
}
Console.WriteLine($"All {passed} estate regression checks passed. No SQL connections were opened.");

sealed class SignalRPeer : IDisposable
{
    private readonly ClientWebSocket _socket = new();
    private string _pending = "";

    public static async Task<SignalRPeer> Connect(Uri uri)
    {
        var peer = new SignalRPeer();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await peer._socket.ConnectAsync(uri, timeout.Token);
        await peer.Send("{\"protocol\":\"json\",\"version\":1}");
        return peer;
    }

    public Task Invoke(string method)
        => Send(JsonSerializer.Serialize(new { type = 1, invocationId = Guid.NewGuid().ToString(), target = method, arguments = Array.Empty<object>() }));

    private async Task Send(string message)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await _socket.SendAsync(Encoding.UTF8.GetBytes(message + '\u001e').AsMemory(), WebSocketMessageType.Text, true, timeout.Token);
    }

    public async Task<JsonElement> Receive(string target)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var buffer = new byte[8192];
        while (true)
        {
            int separator = _pending.IndexOf('\u001e');
            if (separator >= 0)
            {
                string json = _pending[..separator];
                _pending = _pending[(separator + 1)..];
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("target", out var name) && name.GetString() == target)
                    return document.RootElement.Clone();
                continue;
            }
            var received = await _socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
            if (received.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException("SignalR connection closed before " + target);
            _pending += Encoding.UTF8.GetString(buffer, 0, received.Count);
        }
    }

    public void Dispose() => _socket.Dispose();
}
