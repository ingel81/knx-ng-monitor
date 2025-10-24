using KnxMonitor.Core.Entities;

namespace KnxMonitor.Core.Interfaces;

public interface IKnxConfigurationRepository : IRepository<KnxConfiguration>
{
    Task<KnxConfiguration?> GetActiveConfigurationAsync();
}
