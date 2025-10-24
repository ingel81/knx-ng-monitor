using Microsoft.EntityFrameworkCore;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Infrastructure.Data;

namespace KnxMonitor.Infrastructure.Repositories;

public class KnxConfigurationRepository : Repository<KnxConfiguration>, IKnxConfigurationRepository
{
    public KnxConfigurationRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<KnxConfiguration?> GetActiveConfigurationAsync()
    {
        return await _dbSet
            .FirstOrDefaultAsync(c => c.IsActive);
    }
}
