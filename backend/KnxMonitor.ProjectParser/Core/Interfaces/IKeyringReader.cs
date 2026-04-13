using KnxMonitor.ProjectParser.Core.Models;

namespace KnxMonitor.ProjectParser.Core.Interfaces;

public interface IKeyringReader
{
    Task<KeyringData> ReadAsync(
        Stream keyringStream,
        string password,
        CancellationToken cancellationToken = default);
}
