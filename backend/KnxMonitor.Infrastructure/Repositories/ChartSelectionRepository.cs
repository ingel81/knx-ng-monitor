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
        try
        {
            return await UpsertOnceAsync(name, addresses, ct);
        }
        catch (DbUpdateException)
        {
            // Two devices saved the same new name at once: the other insert won the unique index.
            // Drop the failed insert and overwrite the row that now exists instead.
            _context.ChangeTracker.Clear();
            return await UpsertOnceAsync(name, addresses, ct);
        }
    }

    private async Task<ChartSelection> UpsertOnceAsync(
        string name, IReadOnlyList<string> addresses, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Matched in C#, not in SQL: SQLite's NOCASE only folds A to Z, so "Übersicht" and
        // "übersicht" would end up as two rows. OrdinalIgnoreCase folds umlauts too. The table
        // holds a handful of rows, loading them all is cheap.
        var existing = (await _dbSet.ToListAsync(ct))
            .FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

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

        existing.Addresses = string.Join(',', addresses);
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
