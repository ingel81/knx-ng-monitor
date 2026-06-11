using Knx.Falcon;
using Knx.Falcon.Configuration;
using Knx.Falcon.KnxnetIp;
using Knx.Falcon.Sdk;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Enums;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Core.Services;
using KnxMonitor.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using System.Net;

namespace KnxMonitor.Infrastructure.KnxConnection;

public class KnxConnectionService : IKnxConnectionService, IAsyncDisposable, IDisposable
{
    private readonly ILogger<KnxConnectionService> _logger;
    private readonly IGroupAddressCacheService _groupAddressCache;
    private readonly ITelegramQueue _telegramQueue;
    private KnxBus? _knxBus;
    private KnxConfiguration? _activeConfiguration;
    private volatile KnxLinkState _state = KnxLinkState.Disconnected;
    private volatile bool _manualDisconnect;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public event EventHandler<KnxTelegram>? TelegramReceived;
    public bool IsConnected => _state == KnxLinkState.Connected;
    public KnxLinkState State => _state;
    public bool ManualDisconnect => _manualDisconnect;

    public KnxConnectionService(
        ILogger<KnxConnectionService> logger,
        IGroupAddressCacheService groupAddressCache,
        ITelegramQueue telegramQueue)
    {
        _logger = logger;
        _groupAddressCache = groupAddressCache;
        _telegramQueue = telegramQueue;

        // Surface DPT decode failures (first few only, to avoid log spam on a busy bus).
        DptConverter.OnError = (dpt, ex) =>
        {
            if (Interlocked.Increment(ref _decodeErrorLogged) <= 5)
            {
                _logger.LogWarning(ex, "DPT decode failed for {Dpt} (falling back to hex)", dpt);
            }
        };
    }

    private int _decodeErrorLogged;

    public async Task<bool> ConnectAsync(KnxConfiguration configuration)
    {
        try
        {
            // A manual ConnectAsync clears any prior manual-disconnect latch.
            _manualDisconnect = false;
            _state = KnxLinkState.Connecting;

            // Tear down a previous bus without latching manual-disconnect.
            await CleanupBusConnection();

            _logger.LogInformation("Connecting to KNX bus at {IpAddress}:{Port} via {ConnectionType}",
                configuration.IpAddress, configuration.Port, configuration.ConnectionType);

            // Create KNX bus instance
            _knxBus = new KnxBus(BuildConnectorParameters(configuration));

            // Subscribe to events before connecting
            _knxBus.GroupMessageReceived += OnGroupMessageReceived;
            _knxBus.ConnectionStateChanged += OnConnectionStateChanged;

            // Connect to the bus
            await _knxBus.ConnectAsync(_cancellationTokenSource.Token);

            _activeConfiguration = configuration;
            _state = KnxLinkState.Connected;

            _logger.LogInformation("Successfully connected to KNX bus. InterfaceAddress: {IndividualAddress}",
                _knxBus.InterfaceConfiguration.IndividualAddress);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to KNX bus");
            _state = KnxLinkState.Disconnected;
            await CleanupBusConnection();
            return false;
        }
    }

    /// <summary>
    /// Probes a configuration with a throwaway bus. Does NOT touch the live connection,
    /// link state or the manual-disconnect latch — so a "test connection" in Settings can
    /// never interrupt the ongoing recording or stop the auto-connect worker.
    /// </summary>
    public async Task<bool> TestConnectionAsync(KnxConfiguration configuration)
    {
        KnxBus? probe = null;
        try
        {
            probe = new KnxBus(BuildConnectorParameters(configuration));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await probe.ConnectAsync(cts.Token);
            var ok = probe.ConnectionState == BusConnectionState.Connected;
            _logger.LogInformation("Test connection to {IpAddress}:{Port} => {Result}",
                configuration.IpAddress, configuration.Port, ok ? "success" : "failed");
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Test connection to {IpAddress}:{Port} failed",
                configuration.IpAddress, configuration.Port);
            return false;
        }
        finally
        {
            if (probe != null)
            {
                try { await probe.DisposeAsync(); } catch { /* best effort */ }
            }
        }
    }

    private static ConnectorParameters BuildConnectorParameters(KnxConfiguration configuration)
    {
        if (configuration.ConnectionType == Core.Enums.ConnectionType.Tunneling)
        {
            return new IpTunnelingConnectorParameters(
                configuration.IpAddress,
                configuration.Port,
                IpProtocol.Udp,
                true /* NAT mode */);
        }

        return new IpRoutingConnectorParameters(IPAddress.Parse(configuration.IpAddress));
    }

    public async Task DisconnectAsync()
    {
        try
        {
            // User-initiated disconnect: latch so the auto-connect worker stays away.
            _manualDisconnect = true;
            await CleanupBusConnection();
            _state = KnxLinkState.Disconnected;
            _activeConfiguration = null;

            _logger.LogInformation("Disconnected from KNX bus");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting from KNX bus");
        }
    }

    private async Task CleanupBusConnection()
    {
        if (_knxBus != null)
        {
            try
            {
                _knxBus.GroupMessageReceived -= OnGroupMessageReceived;
                _knxBus.ConnectionStateChanged -= OnConnectionStateChanged;

                await _knxBus.DisposeAsync();
                _knxBus = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up bus connection");
            }
        }
    }

    public Task<KnxConfiguration?> GetActiveConfigurationAsync()
    {
        return Task.FromResult(_activeConfiguration);
    }

    private void OnGroupMessageReceived(object? sender, GroupEventArgs e)
    {
        try
        {
            // Map EventType to our MessageType enum (typed enum, not string)
            var messageType = e.EventType switch
            {
                GroupEventType.ValueWrite => MessageType.Write,
                GroupEventType.ValueRead => MessageType.Read,
                GroupEventType.ValueResponse => MessageType.Response,
                _ => MessageType.Read // Default to Read if unknown
            };

            // Get raw value bytes (for storage / hex display)
            var valueBytes = GetValueBytes(e.Value);

            // Convert Falcon telegram to our domain model
            var telegram = new KnxTelegram
            {
                Timestamp = DateTime.UtcNow,
                SourceAddress = e.SourceAddress.ToString(),
                DestinationAddress = e.DestinationAddress.ToString(),
                MessageType = messageType,
                Value = valueBytes,
                ValueDecoded = null, // Will be set after resolving DPT
                Priority = 0, // TODO: Map priority from telegram if available
                Flags = "00"
            };

            // Resolve GroupAddressId and DPT from cache
            string? datapointType = null;
            var groupAddress = _groupAddressCache.GetByAddress(telegram.DestinationAddress);
            if (groupAddress != null)
            {
                telegram.GroupAddressId = groupAddress.Id;
                datapointType = groupAddress.DatapointType;
            }

            // Decode using the original GroupValue (keeps correct bit-size for short DPTs)
            telegram.ValueDecoded = DptConverter.Decode(datapointType, e.Value);

            // Raise event for SignalR broadcasting (now with GroupAddressId set!)
            TelegramReceived?.Invoke(this, telegram);

            // Enqueue for batched persistence. Channel drops oldest on overflow,
            // so a stalled DB can never block the bus thread or blow memory.
            if (!_telegramQueue.TryEnqueue(telegram))
            {
                _logger.LogWarning("Telegram persistence queue full, dropping telegram for {Address}", telegram.DestinationAddress);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing KNX telegram");
        }
    }

    private void OnConnectionStateChanged(object? sender, EventArgs e)
    {
        if (sender is KnxBus bus)
        {
            var state = bus.ConnectionState;
            _logger.LogInformation("KNX connection state changed to: {ConnectionState}", state);

            // Map Falcon's bus state onto our link state. A drop here (Closed/Broken/
            // MediumFailure, not user-initiated) leaves State=Disconnected, which the
            // auto-connect worker picks up to reconnect.
            _state = state == BusConnectionState.Connected
                ? KnxLinkState.Connected
                : KnxLinkState.Disconnected;
        }
    }

    private byte[] GetValueBytes(GroupValue? value)
    {
        return value?.Value ?? Array.Empty<byte>();
    }


    public async ValueTask DisposeAsync()
    {
        _cancellationTokenSource.Cancel();
        await DisconnectAsync();
        _cancellationTokenSource.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        // Prefer DisposeAsync. The host normally calls that on shutdown
        // (IServiceProvider awaits IAsyncDisposable). This sync path is a
        // best-effort fallback and intentionally non-blocking.
        _cancellationTokenSource.Cancel();
        _ = DisposeAsync().AsTask();
        GC.SuppressFinalize(this);
    }
}
