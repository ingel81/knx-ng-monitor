using KnxMonitor.Api.Hubs;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Services;
using Microsoft.AspNetCore.SignalR;

namespace KnxMonitor.Api.Services;

public class TelegramBroadcastService : IHostedService
{
    private readonly IKnxConnectionService _knxService;
    private readonly IHubContext<TelegramHub> _hubContext;
    private readonly ILogger<TelegramBroadcastService> _logger;

    public TelegramBroadcastService(
        IKnxConnectionService knxService,
        IHubContext<TelegramHub> hubContext,
        ILogger<TelegramBroadcastService> logger)
    {
        _knxService = knxService;
        _hubContext = hubContext;
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
            await _hubContext.Clients.All.SendAsync("NewTelegram", new
            {
                telegram.Id,
                telegram.Timestamp,
                telegram.SourceAddress,
                telegram.DestinationAddress,
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
