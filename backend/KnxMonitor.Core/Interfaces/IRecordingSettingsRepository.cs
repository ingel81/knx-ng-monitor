using KnxMonitor.Core.Entities;

namespace KnxMonitor.Core.Interfaces;

public interface IRecordingSettingsRepository : IRepository<RecordingSettings>
{
    /// <summary>Returns the single settings row, seeding a default row on first access.</summary>
    Task<RecordingSettings> GetOrCreateAsync();
}
