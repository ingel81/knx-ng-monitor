using Microsoft.AspNetCore.SignalR;

namespace KnxMonitor.Api.Hubs;

public class TelegramHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("Connected", $"Client {Context.ConnectionId} connected");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}
