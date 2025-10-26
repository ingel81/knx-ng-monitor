using KnxMonitor.Core.Entities;

namespace KnxMonitor.Core.Interfaces;

public interface IKnxProjectParserService
{
    Task<(List<GroupAddress> GroupAddresses, List<Device> Devices)> ParseProjectFileAsync(Stream fileStream, int projectId);
}
