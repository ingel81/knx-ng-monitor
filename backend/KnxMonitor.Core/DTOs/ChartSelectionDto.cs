namespace KnxMonitor.Core.DTOs;

/// <summary>A saved group-address selection of the charts view.</summary>
public class ChartSelectionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> Addresses { get; set; } = new();
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Body for saving a selection. An existing selection with the same name is overwritten.</summary>
public class SaveChartSelectionDto
{
    public string Name { get; set; } = string.Empty;
    public List<string> Addresses { get; set; } = new();
}
