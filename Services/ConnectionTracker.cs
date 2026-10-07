using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace CerberusDashboard.Services
{
    public class ConnectionTracker
    {
        private readonly ConcurrentDictionary<string, (long Revision, string InstanceKey)> _active = new();

        public void SetActive(string connectionId, string instanceKey, long revision)
            => _active[connectionId] = (revision, instanceKey);

        public void Remove(string connectionId)
            => _active.TryRemove(connectionId, out _);

        public IEnumerable<KeyValuePair<string, string>> GetAll(long revision)
            => _active.Where(pair => pair.Value.Revision == revision)
                .Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value.InstanceKey));
    }
}
