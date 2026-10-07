using System.Collections.Generic;

namespace CerberusDashboard.Models
{
    public sealed class EstateState
    {
        public string SessionId { get; set; }
        public string ActiveEstate { get; set; }
        public long Revision { get; set; }
        public List<EstateOption> Estates { get; set; }
    }

    public sealed class EstateOption
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
    }
}
