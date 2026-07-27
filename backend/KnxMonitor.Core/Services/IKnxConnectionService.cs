using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Enums;

namespace KnxMonitor.Core.Services;

public interface IKnxConnectionService
{
    event EventHandler<KnxTelegram>? TelegramReceived;
    Task<bool> ConnectAsync(KnxConfiguration configuration);

    /// <summary>
    /// Probes a configuration. If a live link is up it is briefly paused (its tunnel freed)
    /// for the duration of the probe and then restored — so gateways with a single tunnel
    /// slot can still be tested. Callers should warn the user before triggering this while live.
    /// For routing configurations the probe additionally listens for real bus traffic, because
    /// joining a multicast group succeeds even when nothing can ever arrive; see
    /// <see cref="ConnectionTestOutcome.JoinedWithoutTraffic"/>.
    /// </summary>
    Task<ConnectionTestOutcome> TestConnectionAsync(KnxConfiguration configuration);

    /// <summary>True while a test probe is running (live link paused). The auto-connect
    /// worker must not try to (re)connect in this window or it would open a second tunnel.</summary>
    bool TestInProgress { get; }

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

    /// <summary>
    /// Sends a GroupValueWrite to the bus. ⚠ Writes to the real KNX installation — callers must
    /// gate this behind an explicit user confirmation. Returns false if not connected or the value
    /// could not be encoded for the given DPT.
    /// </summary>
    Task<bool> WriteGroupValueAsync(string address, string? dptType, string value);

    /// <summary>
    /// Sends a GroupValueRead request. The response arrives as a normal received telegram.
    /// Returns false if not connected.
    /// </summary>
    Task<bool> ReadGroupValueAsync(string address);
}
