using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace KnxMonitor.Api.Hubs;

/// <summary>
/// Live log stream for the in-app log viewer. Server pushes "log" events (LogEntryDto).
/// </summary>
[Authorize]
public class LogHub : Hub
{
}
