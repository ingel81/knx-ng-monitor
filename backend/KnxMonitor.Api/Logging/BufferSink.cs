using KnxMonitor.Core.DTOs;
using KnxMonitor.Infrastructure.Logging;
using Serilog.Core;
using Serilog.Events;

namespace KnxMonitor.Api.Logging;

/// <summary>
/// Serilog sink that mirrors every log event into the in-memory <see cref="LogBuffer"/>
/// so the in-app log viewer can show live + recent history.
/// </summary>
public sealed class BufferSink : ILogEventSink
{
    private readonly LogBuffer _buffer;

    public BufferSink(LogBuffer buffer)
    {
        _buffer = buffer;
    }

    public void Emit(LogEvent logEvent)
    {
        string? source = null;
        if (logEvent.Properties.TryGetValue("SourceContext", out var sc) && sc is ScalarValue { Value: string s })
        {
            // Shorten "KnxMonitor.Infrastructure.Services.AuthService" -> "AuthService"
            var dot = s.LastIndexOf('.');
            source = dot >= 0 && dot < s.Length - 1 ? s[(dot + 1)..] : s;
        }

        _buffer.Add(new LogEntryDto
        {
            Timestamp = logEvent.Timestamp,
            Level = logEvent.Level.ToString(),
            Message = logEvent.RenderMessage(),
            Source = source,
            Exception = logEvent.Exception?.ToString()
        });
    }
}
