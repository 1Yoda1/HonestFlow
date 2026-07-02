using System.Collections.Generic;
using System.Linq;

namespace HonestFlow.Application.Installation.Planning
{
    public class InstallationPlan
    {
        public List<ComponentPlanItem> Items { get; } = new();
        public IEnumerable<ComponentPlanItem> RequiredItems => Items.Where(x => x.HasWork);
        public int RequiredCount => RequiredItems.Count();
        public bool HasWork => RequiredCount > 0;
    }
}
