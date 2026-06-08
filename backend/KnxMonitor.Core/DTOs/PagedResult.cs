namespace KnxMonitor.Core.DTOs;

/// <summary>Keyset-paginated result. <see cref="NextCursor"/> is null when the end is reached.</summary>
public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public string? NextCursor { get; set; }
    public bool HasMore { get; set; }
}
