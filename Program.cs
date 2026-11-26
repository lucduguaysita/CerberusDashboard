using CerberusDashboard;
using CerberusDashboard.Hubs;
using CerberusDashboard.Logging;
using CerberusDashboard.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);

// Handle install/uninstall flags
if (args.Contains("/Install"))
{
    await ServiceManager.Install();
    return;
}
if (args.Contains("/Uninstall"))
{
    await ServiceManager.Uninstall();
    return;
}

// Enables running as a Windows Service (SCM-managed lifecycle).
// Safe to call when running interactively — it becomes a no-op.
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "CerberusDashboard";
});

// Error log files: C:\ProgramData\CerberusDashboard\logs\cerberus-yyyyMMdd.log
// Override with Logging:File:Path / Logging:File:RetentionDays / Logging:File:LogLevel.
builder.Logging.AddProvider(new FileLoggerProvider(
    builder.Configuration["Logging:File:Path"] ?? FileLoggerProvider.DefaultDirectory,
    LogLevel.Warning,
    int.TryParse(builder.Configuration["Logging:File:RetentionDays"], out var retentionDays) ? retentionDays : 30));

builder.Services.AddControllersWithViews()
    .AddNewtonsoftJson();

builder.Services.AddSignalR()
    .AddNewtonsoftJsonProtocol(options =>
    {
        // Use PascalCase for property names to match the original .NET Framework behavior
        options.PayloadSerializerSettings.ContractResolver =
            new Newtonsoft.Json.Serialization.DefaultContractResolver();
    });

builder.Services.AddSingleton<EstateManager>();
builder.Services.AddSingleton<ConnectionTracker>();
builder.Services.AddHostedService<MonitorBackgroundService>();
var dataProtectionBuilder = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(
        new DirectoryInfo(@"C:\ProgramData\CerberusDashboard\DataProtection"))
    .SetApplicationName("CerberusDashboard");

if (OperatingSystem.IsWindows())
{
    dataProtectionBuilder.ProtectKeysWithDpapi(protectToLocalMachine: true);
}
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.MapHub<MonitorHub>("/signalr/monitorHub");

app.Run();