using Microsoft.EntityFrameworkCore;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Infrastructure.Data;

namespace KnxMonitor.Infrastructure.Repositories;

public class RecordingSettingsRepository : Repository<RecordingSettings>, IRecordingSettingsRepository
{
    public RecordingSettingsRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<RecordingSettings> GetOrCreateAsync()
    {
        var existing = await _dbSet.OrderBy(s => s.Id).FirstOrDefaultAsync();
        if (existing != null)
        {
            return existing;
        }

        var created = new RecordingSettings { UpdatedAt = DateTime.UtcNow };
        await _dbSet.AddAsync(created);
        await _context.SaveChangesAsync();
        return created;
    }
}
