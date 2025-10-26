using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace KnxMonitor.Api.Hubs;

[Authorize]
public class TelegramHub : Hub
{
    private readonly ILogger<TelegramHub> _logger;

    public TelegramHub(ILogger<TelegramHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var username = Context.User?.Identity?.Name ?? "Unknown";
        _logger.LogInformation("User {Username} connected to TelegramHub (ConnectionId: {ConnectionId})",
            username, Context.ConnectionId);

        await Clients.Caller.SendAsync("Connected", $"Client {Context.ConnectionId} connected");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var username = Context.User?.Identity?.Name ?? "Unknown";
        _logger.LogInformation("User {Username} disconnected from TelegramHub (ConnectionId: {ConnectionId})",
            username, Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }
}
