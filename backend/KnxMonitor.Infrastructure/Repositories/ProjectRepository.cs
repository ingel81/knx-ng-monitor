using Microsoft.EntityFrameworkCore;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Infrastructure.Data;

namespace KnxMonitor.Infrastructure.Repositories;

public class ProjectRepository : Repository<Project>, IProjectRepository
{
    public ProjectRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<Project?> GetWithDetailsAsync(int id)
    {
        return await _dbSet
            .Include(p => p.GroupAddresses)
            .Include(p => p.Devices)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<List<Project>> GetAllWithCountsAsync()
    {
        return await _dbSet
            .Include(p => p.GroupAddresses)
            .Include(p => p.Devices)
            .ToListAsync();
    }

    public async Task<Project?> GetActiveProjectAsync()
    {
        return await _dbSet
            .Include(p => p.GroupAddresses)
            .Include(p => p.Devices)
            .FirstOrDefaultAsync(p => p.IsActive);
    }
}
