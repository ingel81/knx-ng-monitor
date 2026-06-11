using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Enums;

namespace KnxMonitor.Core.Services;

public interface IKnxConnectionService
{
    event EventHandler<KnxTelegram>? TelegramReceived;
    Task<bool> ConnectAsync(KnxConfiguration configuration);

    /// <summary>
    /// Probes a configuration without touching the live connection (non-destructive).
    /// </summary>
    Task<bool> TestConnectionAsync(KnxConfiguration configuration);

    Task DisconnectAsync();
    bool IsConnected { get; }

    /// <summary>Detailed link state for the UI (Disconnected / Connecting / Connected).</summary>
    KnxLinkState State { get; }

    /// <summary>
    /// When true, manual disconnect has been requested; the auto-connect worker
    /// must not immediately reconnect. Cleared on the next manual connect.
    /// </summary>
    bool ManualDisconnect { get; }

    Task<KnxConfiguration?> GetActiveConfigurationAsync();
}
