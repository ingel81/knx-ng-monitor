namespace KnxMonitor.Core.Entities;

/// <summary>
/// A named set of group addresses for the charts view, so a recurring comparison (pump + status,
/// all room temperatures, ...) is one click instead of re-picking every address.
/// </summary>
/// <remarks>
/// Stored server-side rather than in the browser so the same selections are available on every
/// device. Deliberately not tied to a project: the addresses are plain strings, and the client
/// drops any that the active project does not know.
/// </remarks>
public class ChartSelection
{
    public int Id { get; set; }

    /// <summary>User-facing label; unique, compared case-insensitively.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Group addresses in selection order, comma-separated ("7/0/90,7/0/91").</summary>
    public string Addresses { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
