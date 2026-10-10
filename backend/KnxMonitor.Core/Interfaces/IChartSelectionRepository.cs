using KnxMonitor.Core.Entities;

namespace KnxMonitor.Core.Interfaces;

public interface IChartSelectionRepository : IRepository<ChartSelection>
{
    /// <summary>All selections, ordered by name.</summary>
    Task<IReadOnlyList<ChartSelection>> GetAllOrderedAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates the selection, or overwrites the addresses of the one with the same name
    /// (case-insensitive). Returns the stored row.
    /// </summary>
    Task<ChartSelection> UpsertByNameAsync(string name, IReadOnlyList<string> addresses, CancellationToken ct = default);
}
