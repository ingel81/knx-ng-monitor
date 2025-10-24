using KnxMonitor.Core.Entities;

namespace KnxMonitor.Core.Interfaces;

public interface IProjectRepository : IRepository<Project>
{
    Task<Project?> GetWithDetailsAsync(int id);
}
