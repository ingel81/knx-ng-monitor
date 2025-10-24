using KnxMonitor.Core.Entities;

namespace KnxMonitor.Core.Services;

public interface IKnxConnectionService
{
    event EventHandler<KnxTelegram>? TelegramReceived;
    Task<bool> ConnectAsync(KnxConfiguration configuration);
    Task DisconnectAsync();
    bool IsConnected { get; }
    Task<KnxConfiguration?> GetActiveConfigurationAsync();
}
