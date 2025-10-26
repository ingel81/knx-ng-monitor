using KnxMonitor.Api.Hubs;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Core.Services;
using KnxMonitor.Infrastructure.Services;
using Microsoft.AspNetCore.SignalR;

namespace KnxMonitor.Api.Services;

public class TelegramBroadcastService : IHostedService
{
    private readonly IKnxConnectionService _knxService;
    private readonly IHubContext<TelegramHub> _hubContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelegramBroadcastService> _logger;

    public TelegramBroadcastService(
        IKnxConnectionService knxService,
        IHubContext<TelegramHub> hubContext,
        IServiceScopeFactory scopeFactory,
        ILogger<TelegramBroadcastService> logger)
    {
        _knxService = knxService;
        _hubContext = hubContext;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Telegram Broadcast Service started");
        _knxService.TelegramReceived += OnTelegramReceived;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Telegram Broadcast Service stopping");
        _knxService.TelegramReceived -= OnTelegramReceived;
        return Task.CompletedTask;
    }

    private async void OnTelegramReceived(object? sender, KnxTelegram telegram)
    {
        try
        {
            // Use cache to get GroupAddress info efficiently
            string? groupAddressName = null;
            string? datapointType = null;

            if (telegram.GroupAddressId.HasValue)
            {
                using var scope = _scopeFactory.CreateScope();
                var cacheService = scope.ServiceProvider.GetRequiredService<IGroupAddressCacheService>();
                var groupAddress = cacheService.GetByAddress(telegram.DestinationAddress);

                if (groupAddress != null)
                {
                    groupAddressName = groupAddress.Name;
                    datapointType = groupAddress.DatapointType;
                }
            }

            await _hubContext.Clients.All.SendAsync("NewTelegram", new
            {
                telegram.Id,
                telegram.Timestamp,
                telegram.SourceAddress,
                telegram.DestinationAddress,
                GroupAddressName = groupAddressName,
                DatapointType = datapointType,
                telegram.MessageType,
                Value = Convert.ToHexString(telegram.Value),
                telegram.ValueDecoded,
                telegram.Priority,
                telegram.Flags
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting telegram");
        }
    }
}
