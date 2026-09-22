using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KnxMonitor.Infrastructure.Repositories;

public class ChartSelectionRepository : Repository<ChartSelection>, IChartSelectionRepository
{
    public ChartSelectionRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<ChartSelection>> GetAllOrderedAsync(CancellationToken ct = default)
    {
        var rows = await _dbSet.AsNoTracking().ToListAsync(ct);
        foreach (var row in rows)
        {
            TagUtc(row);
        }
        // Sorted here, not in SQL: SQLite's default collation orders "Zimmer" before "bad".
        return rows.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<ChartSelection> UpsertByNameAsync(
        string name, IReadOnlyList<string> addresses, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var joined = string.Join(',', addresses);

        // The unique index uses NOCASE, so the lookup has to match the same way or a save of
        // "pumpe" next to "Pumpe" would hit the index instead of updating the row.
        var existing = await _dbSet.FirstOrDefaultAsync(
            s => EF.Functions.Collate(s.Name, "NOCASE") == name, ct);

        if (existing is null)
        {
            existing = new ChartSelection { Name = name, CreatedAt = now };
            await _dbSet.AddAsync(existing, ct);
        }
        else
        {
            // Keep the spelling of the latest save, so "pumpe" can be corrected to "Pumpe".
            existing.Name = name;
        }

        existing.Addresses = joined;
        existing.UpdatedAt = now;
        await _context.SaveChangesAsync(ct);
        return TagUtc(existing);
    }

    /// <summary>SQLite hands back Kind=Unspecified; tag as UTC so the JSON carries a trailing 'Z'.</summary>
    private static ChartSelection TagUtc(ChartSelection row)
    {
        row.CreatedAt = DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc);
        row.UpdatedAt = DateTime.SpecifyKind(row.UpdatedAt, DateTimeKind.Utc);
        return row;
    }
}
