using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Models;

namespace KnxMonitor.Core.Interfaces;

public interface IKnxProjectParserService
{
    Task<ParsedProjectData> ParseProjectFileAsync(Stream fileStream, int projectId);
    Task<ParsedProjectData> ParseProjectFileAsync(Stream fileStream, int projectId, ImportContext context);

    /// <summary>
    /// Decrypts a standalone keyring (.knxkeys) and maps its keys to ProjectKeyringKey rows for the
    /// given project. Used by the keyring-upload-after-import endpoint (no project re-parse).
    /// </summary>
    Task<List<ProjectKeyringKey>> ParseKeyringAsync(byte[] keyringData, string keyringPassword, int projectId);
}
