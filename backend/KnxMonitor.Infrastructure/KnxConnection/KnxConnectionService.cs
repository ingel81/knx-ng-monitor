using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Enums;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnxMonitor.Infrastructure.KnxConnection;

public class KnxConnectionService : IKnxConnectionService, IDisposable
{
    private readonly ILogger<KnxConnectionService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private Timer? _simulationTimer;
    private KnxConfiguration? _activeConfiguration;
    private bool _isConnected;
    private readonly Random _random = new();

    public event EventHandler<KnxTelegram>? TelegramReceived;
    public bool IsConnected => _isConnected;

    public KnxConnectionService(
        ILogger<KnxConnectionService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task<bool> ConnectAsync(KnxConfiguration configuration)
    {
        try
        {
            if (_isConnected)
            {
                await DisconnectAsync();
            }

            _logger.LogInformation("Connecting to KNX bus at {IpAddress}:{Port}",
                configuration.IpAddress, configuration.Port);

            // TODO: Implement actual KNX connection with Knx.Falcon.Sdk
            // For now, simulate telegrams for demo purposes
            _simulationTimer = new Timer(SimulateTelegram, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));

            _activeConfiguration = configuration;
            _isConnected = true;

            _logger.LogInformation("Successfully connected to KNX bus (simulation mode)");
            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to KNX bus");
            _isConnected = false;
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_simulationTimer != null)
        {
            try
            {
                await _simulationTimer.DisposeAsync();
                _simulationTimer = null;
                _isConnected = false;
                _activeConfiguration = null;

                _logger.LogInformation("Disconnected from KNX bus");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting from KNX bus");
            }
        }

        await Task.CompletedTask;
    }

    public Task<KnxConfiguration?> GetActiveConfigurationAsync()
    {
        return Task.FromResult(_activeConfiguration);
    }

    private void SimulateTelegram(object? state)
    {
        try
        {
            var groupAddresses = new[] { "0/0/1", "0/0/2", "0/1/5", "1/2/10", "1/2/11", "2/0/1" };
            var messageTypes = new[] { MessageType.Write, MessageType.Read, MessageType.Response };

            var randomValue = _random.Next(0, 256);
            var telegram = new KnxTelegram
            {
                Timestamp = DateTime.UtcNow,
                SourceAddress = $"1.1.{_random.Next(1, 255)}",
                DestinationAddress = groupAddresses[_random.Next(groupAddresses.Length)],
                MessageType = messageTypes[_random.Next(messageTypes.Length)],
                Value = new byte[] { (byte)randomValue },
                ValueDecoded = randomValue.ToString(),
                Priority = 0,
                Flags = "00"
            };

            // Save to database (using scope because this is a Singleton service)
            Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var telegramRepository = scope.ServiceProvider.GetRequiredService<ITelegramRepository>();
                    await telegramRepository.AddAsync(telegram);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to save telegram to database");
                }
            });

            // Raise event for SignalR broadcasting
            TelegramReceived?.Invoke(this, telegram);

            _logger.LogDebug("Simulated telegram: {Source} -> {Dest} ({Type})",
                telegram.SourceAddress, telegram.DestinationAddress, telegram.MessageType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error simulating KNX telegram");
        }
    }

    public void Dispose()
    {
        DisconnectAsync().Wait();
        GC.SuppressFinalize(this);
    }
}
