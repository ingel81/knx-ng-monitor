using Microsoft.EntityFrameworkCore;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Infrastructure.Data;

namespace KnxMonitor.Infrastructure.Repositories;

public class GroupAddressRepository : Repository<GroupAddress>, IGroupAddressRepository
{
    public GroupAddressRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<GroupAddress?> GetByAddressAsync(string address)
    {
        return await _dbSet
            .Include(g => g.Project)
            .FirstOrDefaultAsync(g => g.Address == address);
    }

    public async Task<IEnumerable<GroupAddress>> GetByProjectIdAsync(int projectId)
    {
        return await _dbSet
            .Where(g => g.ProjectId == projectId)
            .OrderBy(g => g.Address)
            .ToListAsync();
    }

    public async Task<IEnumerable<GroupAddress>> SearchByNameAsync(string searchTerm)
    {
        return await _dbSet
            .Where(g => g.Name.Contains(searchTerm) || g.Description!.Contains(searchTerm))
            .Include(g => g.Project)
            .OrderBy(g => g.Name)
            .ToListAsync();
    }
}
