namespace KnxMonitor.Core.Interfaces;

public interface IKnxSecureService
{
    Task<Dictionary<string, string>> ParseKeyringAsync(Stream keyringStream, string password);
}
