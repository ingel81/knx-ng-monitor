using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Models;

namespace KnxMonitor.Core.Interfaces;

public interface IKnxProjectParserService
{
    Task<(List<GroupAddress> GroupAddresses, List<Device> Devices)> ParseProjectFileAsync(Stream fileStream, int projectId);
    Task<(List<GroupAddress> GroupAddresses, List<Device> Devices)> ParseProjectFileAsync(Stream fileStream, int projectId, ImportContext context);
}
