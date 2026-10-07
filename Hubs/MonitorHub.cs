using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using CerberusDashboard.Models;
using CerberusDashboard.Services;

namespace CerberusDashboard.Hubs
{
    public class MonitorHub : Hub
    {
        private readonly EstateManager _estates;
        private readonly ConnectionTracker _tracker;

        public MonitorHub(EstateManager estates, ConnectionTracker tracker)
        {
            _estates = estates;
            _tracker = tracker;
        }

        private long Revision => Context.Items.TryGetValue("EstateRevision", out var revision)
            ? (long)revision : 0;

        private SqlMonitorService GetMonitor()
        {
            var estate = _estates.Active;
            if (estate.Revision != Revision)
                throw new HubException("The active estate has changed. Reload the dashboard.");
            return estate.Monitor;
        }

        private Task SendEstateData(string method, params object[] values)
            => Clients.Caller.SendCoreAsync(method, values.Append((object)Revision).ToArray());

        public override async Task OnConnectedAsync()
        {
            var state = _estates.GetState();
            Context.Items["EstateRevision"] = state.Revision;
            await Clients.Caller.SendAsync("receiveEstateState", state, true);
            await base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            _tracker.Remove(Context.ConnectionId);
            return base.OnDisconnectedAsync(exception);
        }

        public Task SetActiveInstance(string instanceKey)
        {
            if (!GetMonitor().GetInstanceKeys().Any(instance => instance.Key == instanceKey))
                throw new HubException("Select an instance in the active estate.");
            _tracker.SetActive(Context.ConnectionId, instanceKey, Revision);
            return Task.CompletedTask;
        }

        public async Task RequestInitialData()
        {
            try
            {
                var service = GetMonitor();
                var instanceList = service.GetInstanceKeys();
                int total = instanceList.Count;
                int done = 0;

                await SendEstateData("receiveProgress", "Initializing...", 0);

                var snapshot = new DashboardSnapshot
                {
                    GeneratedAt = DateTime.Now,
                    RefreshIntervalSeconds = service.RefreshSeconds
                };

                foreach (var instance in instanceList)
                {
                    if (_estates.Active.Revision != Revision) return;
                    int basePct = total > 0 ? (int)((done / (float)total) * 100) : 0;

                    await SendEstateData("receiveProgress",
                        "Connecting to " + instance.DisplayName + "...", basePct);

                    var snap = service.GetInstanceSnapshotPublic(instance, msg =>
                        SendEstateData("receiveProgress", msg, basePct).GetAwaiter().GetResult());

                    snapshot.Instances.Add(snap);
                    done++;

                    await SendEstateData("receiveProgress",
                        instance.DisplayName + " \u2713",
                        (int)((done / (float)total) * 100));
                }

                await SendEstateData("receiveSnapshot", snapshot);
            }
            catch (Exception ex)
            {
                await SendEstateData("receiveError", ex.Message);
            }
        }

        public async Task RequestJobHistory(string instanceKey, string jobId)
        {
            try
            {
                var history = GetMonitor().GetJobHistory(instanceKey, Guid.Parse(jobId));
                await SendEstateData("receiveJobHistory", jobId, history);
            }
            catch (Exception ex)
            {
                await SendEstateData("receiveError", "Job history error: " + ex.Message);
            }
        }

        public async Task RequestQueryPlan(string instanceKey, string planHandle)
        {
            try
            {
                var plan = GetMonitor().GetQueryPlan(instanceKey, planHandle);
                await SendEstateData("receiveQueryPlan", plan);
            }
            catch (Exception ex)
            {
                await SendEstateData("receiveError", "Query plan error: " + ex.Message);
            }
        }

        public async Task RequestActiveSessions(string instanceKey)
        {
            try
            {
                var sessions = GetMonitor().GetActiveSessions(instanceKey);
                await SendEstateData("receiveActiveSessions", sessions);
            }
            catch (Exception ex)
            {
                await SendEstateData("receiveError", "Active sessions error: " + ex.Message);
            }
        }

        public async Task RequestPartitionInfo(string instanceKey, string databaseName,
                                               string schemaName, string tableName)
        {
            try
            {
                var info = GetMonitor().GetPartitionInfo(instanceKey, databaseName, schemaName, tableName);
                await SendEstateData("receivePartitionInfo", info);
            }
            catch (Exception ex)
            {
                await SendEstateData("receiveError", "Partition info error: " + ex.Message);
            }
        }
    }
}
