using KnxMonitor.Core.Entities;

namespace KnxMonitor.Core.Interfaces;

public interface IGroupAddressRepository : IRepository<GroupAddress>
{
    Task<GroupAddress?> GetByAddressAsync(string address);
    Task<IEnumerable<GroupAddress>> GetByProjectIdAsync(int projectId);
    Task<IEnumerable<GroupAddress>> SearchByNameAsync(string searchTerm);
}
