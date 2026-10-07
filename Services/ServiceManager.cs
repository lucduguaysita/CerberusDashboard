using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace CerberusDashboard.Services
{
    public static class ServiceManager
    {
        private const string ServiceName = "CerberusDashboard";

        public static async Task Install()
        {
            var exePath = Environment.ProcessPath;

            // Create service
            await RunCommand("sc",
                $"create {ServiceName} binPath= \"{exePath}\" start= auto displayname= \"Cerberus Dashboard\"");

            // Set description
            await RunCommand("sc",
                $"description {ServiceName} \"SQL Server Performance Monitor\"");

            // Set recovery — restart on all 3 failures, reset count daily
            await RunCommand("sc",
                $"failure {ServiceName} reset= 86400 actions= restart/5000/restart/10000/restart/30000");

            // Trigger recovery on non-zero exit codes too
            await RunCommand("sc",
                $"failureflag {ServiceName} 1");

            // Start the service
            await RunCommand("sc",
                $"start {ServiceName}");
        }

        public static async Task Uninstall()
        {
            await RunCommand("sc", $"stop {ServiceName}");
            await RunCommand("sc", $"delete {ServiceName}");
        }

        private static async Task RunCommand(string cmd, string args)
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = cmd,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            await process!.WaitForExitAsync();
        }
    }
}
