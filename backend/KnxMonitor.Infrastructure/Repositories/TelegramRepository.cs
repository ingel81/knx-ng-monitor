using Microsoft.EntityFrameworkCore;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Enums;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Infrastructure.Data;

namespace KnxMonitor.Infrastructure.Repositories;

public class TelegramRepository : Repository<KnxTelegram>, ITelegramRepository
{
    public TelegramRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<KnxTelegram>> GetByDateRangeAsync(DateTime from, DateTime to)
    {
        return await _dbSet
            .Where(t => t.Timestamp >= from && t.Timestamp <= to)
            .Include(t => t.GroupAddress)
            .OrderByDescending(t => t.Timestamp)
            .ToListAsync();
    }

    public async Task<IEnumerable<KnxTelegram>> GetByGroupAddressAsync(string address)
    {
        return await _dbSet
            .Where(t => t.DestinationAddress == address)
            .Include(t => t.GroupAddress)
            .OrderByDescending(t => t.Timestamp)
            .ToListAsync();
    }

    public async Task<IEnumerable<KnxTelegram>> GetByMessageTypeAsync(MessageType messageType)
    {
        return await _dbSet
            .Where(t => t.MessageType == messageType)
            .Include(t => t.GroupAddress)
            .OrderByDescending(t => t.Timestamp)
            .ToListAsync();
    }

    public async Task<IEnumerable<KnxTelegram>> GetPagedAsync(int page, int pageSize)
    {
        return await _dbSet
            .Include(t => t.GroupAddress)
            .OrderByDescending(t => t.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<int> GetTotalCountAsync()
    {
        return await _dbSet.CountAsync();
    }
}
