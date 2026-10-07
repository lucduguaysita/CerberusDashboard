using System;
using System.Collections.Generic;
using System.Linq;
using CerberusDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CerberusDashboard.Services
{
    public sealed class EstateManager
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, EstateProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
        private EstateContext _active;
        public string SessionId { get; } = Guid.NewGuid().ToString("N");

        public EstateManager(IConfiguration configuration)
        {
            var sections = configuration.GetSection("Estates").GetChildren().ToList();
            foreach (var section in sections)
            {
                var settings = CopySettings(section, configuration["MonitorRefreshSeconds"]);
                ValidateSettings(section.Key, settings);
                _profiles.Add(section.Key, new EstateProfile
                {
                    Key = section.Key,
                    DisplayName = section["DisplayName"] ?? section.Key,
                    Settings = settings
                });
            }

            if (_profiles.Count == 0)
            {
                _profiles.Add("Default", new EstateProfile
                {
                    Key = "Default",
                    DisplayName = "Default",
                    Settings = CopySettings(configuration, "30")
                });
            }

            string defaultKey = configuration["DefaultEstate"];
            if (string.IsNullOrWhiteSpace(defaultKey))
            {
                if (_profiles.Count != 1)
                    throw new InvalidOperationException("DefaultEstate is required when multiple estates are configured.");
                defaultKey = _profiles.Keys.Single();
            }

            if (!_profiles.TryGetValue(defaultKey, out var profile))
                throw new InvalidOperationException("DefaultEstate must match a configured estate key.");

            _active = CreateContext(profile, 1);
        }

        public EstateContext Active
        {
            get { lock (_sync) return _active; }
        }

        public EstateState GetState()
        {
            lock (_sync)
            {
                return new EstateState
                {
                    SessionId = SessionId,
                    ActiveEstate = _active.Key,
                    Revision = _active.Revision,
                    Estates = _profiles.Values.Select(p => new EstateOption
                    {
                        Key = p.Key,
                        DisplayName = p.DisplayName
                    }).ToList()
                };
            }
        }

        public void Select(string key, long expectedRevision, string sessionId)
        {
            lock (_sync)
            {
                if (sessionId != SessionId || expectedRevision != _active.Revision)
                    throw new InvalidOperationException("The active estate has changed. Reload the dashboard before selecting an estate.");
                if (key == null || !_profiles.TryGetValue(key, out var profile))
                    throw new ArgumentException("Select a configured estate.");
                if (string.Equals(_active.Key, profile.Key, StringComparison.OrdinalIgnoreCase))
                    return;

                _active = CreateContext(profile, checked(_active.Revision + 1));
            }
        }

        private static EstateContext CreateContext(EstateProfile profile, long revision)
            => new EstateContext(profile.Key, revision, new SqlMonitorService(profile.Settings));

        private static IConfiguration CopySettings(IConfiguration source, string refreshDefault)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["MonitoredInstances"] = source["MonitoredInstances"] ?? "",
                ["InstanceDisplayNames"] = source["InstanceDisplayNames"] ?? "",
                ["MonitorRefreshSeconds"] = source["MonitorRefreshSeconds"] ?? refreshDefault ?? "30"
            };
            foreach (var connection in source.GetSection("ConnectionStrings").GetChildren())
                values["ConnectionStrings:" + connection.Key] = connection.Value;
            return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        }

        private static void ValidateSettings(string key, IConfiguration settings)
        {
            if (!int.TryParse(settings["MonitorRefreshSeconds"], out int refresh) || refresh < 5 || refresh > 86400)
                throw new InvalidOperationException($"Estate '{key}' requires MonitorRefreshSeconds between 5 and 86400.");

            var instanceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in settings["MonitoredInstances"].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string instanceKey = value.Trim();
                if (instanceKey.Length == 0 || !instanceKeys.Add(instanceKey))
                    throw new InvalidOperationException($"Estate '{key}' has an empty or duplicate monitored instance key.");
                string connectionString = settings.GetConnectionString(instanceKey);
                if (string.IsNullOrWhiteSpace(connectionString))
                    throw new InvalidOperationException($"Estate '{key}' is missing a connection string for '{instanceKey}'.");
                try
                {
                    _ = new SqlConnectionStringBuilder(connectionString);
                }
                catch (ArgumentException)
                {
                    throw new InvalidOperationException($"Estate '{key}' has an invalid connection string for '{instanceKey}'.");
                }
            }
        }

        private sealed class EstateProfile
        {
            public string Key { get; init; }
            public string DisplayName { get; init; }
            public IConfiguration Settings { get; init; }
        }
    }

    public sealed class EstateContext
    {
        public EstateContext(string key, long revision, SqlMonitorService monitor)
        {
            Key = key;
            Revision = revision;
            Monitor = monitor;
        }

        public string Key { get; }
        public long Revision { get; }
        public SqlMonitorService Monitor { get; }
    }
}
