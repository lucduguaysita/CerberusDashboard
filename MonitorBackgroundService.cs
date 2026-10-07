using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CerberusDashboard.Hubs;
using CerberusDashboard.Models;
using CerberusDashboard.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;

namespace CerberusDashboard
{
    public class MonitorBackgroundService : BackgroundService
    {
        private readonly IHubContext<MonitorHub> _hubContext;
        private readonly EstateManager _estates;
        private readonly ConnectionTracker _tracker;

        public MonitorBackgroundService(
            IHubContext<MonitorHub> hubContext,
            EstateManager estates,
            ConnectionTracker tracker)
        {
            _hubContext = hubContext;
            _estates = estates;
            _tracker = tracker;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(5000, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var estate = _estates.Active;
                var monitor = estate.Monitor;
                try
                {
                    // Group connections by the instance they are currently viewing
                    var byInstance = _tracker.GetAll(estate.Revision)
                        .Where(kv => !string.IsNullOrEmpty(kv.Value))
                        .GroupBy(kv => kv.Value)
                        .ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToList());

                    if (byInstance.Count == 0)
                    {
                        await Task.Delay(1000, stoppingToken);
                        continue;
                    }

                    var allInstances = monitor.GetInstanceKeys()
                        .ToDictionary(i => i.Key);

                    foreach (var (instanceKey, connectionIds) in byInstance)
                    {
                        if (_estates.Active != estate) break;
                        if (!allInstances.TryGetValue(instanceKey, out var instance))
                            continue;

                        try
                        {
                            var snap = monitor.GetInstanceSnapshotPublic(instance);
                            var partial = new DashboardSnapshot
                            {
                                GeneratedAt = DateTime.Now,
                                RefreshIntervalSeconds = monitor.RefreshSeconds,
                                Instances = new List<InstanceSnapshot> { snap }
                            };

                            foreach (var connId in connectionIds)
                                await _hubContext.Clients.Client(connId)
                                    .SendAsync("receiveSnapshot", partial, estate.Revision, stoppingToken);

                            // Broadcast file I/O to ALL clients so per-file history
                            // stays current regardless of which tab each client is on.
                            await _hubContext.Clients.All.SendAsync(
                                "receiveFileIOUpdate", instanceKey,
                                snap.DataFileIO ?? new List<DataFileIO>(), estate.Revision, stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"MonitorTick error [{instanceKey}]: {ex.Message}");
                        }
                    }

                    // For instances nobody is actively viewing, still query file I/O
                    // and broadcast so all clients keep their per-file histories current.
                    foreach (var (key, instance) in allInstances)
                    {
                        if (_estates.Active != estate) break;
                        if (byInstance.ContainsKey(key)) continue;
                        try
                        {
                            var fileIO = monitor.GetFileIOStats(instance);
                            await _hubContext.Clients.All.SendAsync(
                                "receiveFileIOUpdate", key, fileIO, estate.Revision, stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"FileIO update error [{key}]: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("MonitorTick error: " + ex.Message);
                }

                for (int second = 0; second < monitor.RefreshSeconds && _estates.Active == estate; second++)
                    await Task.Delay(1000, stoppingToken);
            }
        }
    }
}
