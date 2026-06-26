using KnxMonitor.Api.Hubs;
using KnxMonitor.Core.DTOs;
using KnxMonitor.Infrastructure.Logging;
using Microsoft.AspNetCore.SignalR;

namespace KnxMonitor.Api.Services;

/// <summary>
/// Bridges the <see cref="LogBuffer"/> to the <see cref="LogHub"/> — every new log entry
/// is pushed to connected viewers as a "log" event. Kept separate from the Serilog sink so
/// the sink stays DI-free.
/// </summary>
public class LogBroadcastWorker : IHostedService
{
    private readonly LogBuffer _buffer;
    private readonly IHubContext<LogHub> _hub;
    private Action<LogEntryDto>? _handler;

    public LogBroadcastWorker(LogBuffer buffer, IHubContext<LogHub> hub)
    {
        _buffer = buffer;
        _hub = hub;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _handler = entry =>
        {
            // Fire-and-forget; never block or throw on the logging thread.
            _ = _hub.Clients.All.SendAsync("log", entry);
        };
        _buffer.Emitted += _handler;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_handler != null)
        {
            _buffer.Emitted -= _handler;
            _handler = null;
        }
        return Task.CompletedTask;
    }
}
