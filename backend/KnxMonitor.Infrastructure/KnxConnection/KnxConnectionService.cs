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
    private bool _isConnected;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public event EventHandler<KnxTelegram>? TelegramReceived;
    public bool IsConnected => _isConnected;

    public KnxConnectionService(
        ILogger<KnxConnectionService> logger,
        IGroupAddressCacheService groupAddressCache,
        ITelegramQueue telegramQueue)
    {
        _logger = logger;
        _groupAddressCache = groupAddressCache;
        _telegramQueue = telegramQueue;
    }

    public async Task<bool> ConnectAsync(KnxConfiguration configuration)
    {
        try
        {
            if (_isConnected)
            {
                await DisconnectAsync();
            }

            _logger.LogInformation("Connecting to KNX bus at {IpAddress}:{Port} via {ConnectionType}",
                configuration.IpAddress, configuration.Port, configuration.ConnectionType);

            // Create connector parameters based on configuration type
            ConnectorParameters connectorParameters;
            if (configuration.ConnectionType == Core.Enums.ConnectionType.Tunneling)
            {
                // Create tunneling parameters
                connectorParameters = new IpTunnelingConnectorParameters(
                    configuration.IpAddress,
                    configuration.Port,
                    IpProtocol.Udp,
                    true  // Use NAT mode
                );
            }
            else
            {
                // Create routing parameters
                connectorParameters = new IpRoutingConnectorParameters(IPAddress.Parse(configuration.IpAddress));
            }

            // Create KNX bus instance
            _knxBus = new KnxBus(connectorParameters);

            // Subscribe to events before connecting
            _knxBus.GroupMessageReceived += OnGroupMessageReceived;
            _knxBus.ConnectionStateChanged += OnConnectionStateChanged;

            // Connect to the bus
            await _knxBus.ConnectAsync(_cancellationTokenSource.Token);

            _activeConfiguration = configuration;
            _isConnected = true;

            _logger.LogInformation("Successfully connected to KNX bus. InterfaceAddress: {IndividualAddress}",
                _knxBus.InterfaceConfiguration.IndividualAddress);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to KNX bus");
            _isConnected = false;
            await CleanupBusConnection();
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            await CleanupBusConnection();
            _isConnected = false;
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
            // Map EventType to our MessageType enum
            var eventTypeString = e.EventType.ToString();
            var messageType = eventTypeString switch
            {
                "ValueWrite" => MessageType.Write,
                "ValueRead" => MessageType.Read,
                "ValueResponse" => MessageType.Response,
                _ => MessageType.Read // Default to Read if unknown
            };

            // Get raw value bytes
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

            // Decode value using DPT information
            telegram.ValueDecoded = DptConverter.Decode(datapointType, valueBytes);

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

            // Update our internal connection status
            _isConnected = state == BusConnectionState.Connected;
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
