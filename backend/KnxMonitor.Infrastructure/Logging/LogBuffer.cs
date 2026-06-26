using KnxMonitor.Core.DTOs;

namespace KnxMonitor.Infrastructure.Logging;

/// <summary>
/// Thread-safe ring buffer of the most recent log entries, plus an event for live
/// fan-out (SignalR). Registered as a singleton and also fed by the Serilog sink.
/// </summary>
public sealed class LogBuffer
{
    private readonly int _capacity;
    private readonly LinkedList<LogEntryDto> _entries = new();
    private readonly object _gate = new();

    /// <summary>Fired for every new entry. Subscribers must not throw.</summary>
    public event Action<LogEntryDto>? Emitted;

    public LogBuffer(int capacity = 2000)
    {
        _capacity = capacity;
    }

    public void Add(LogEntryDto entry)
    {
        lock (_gate)
        {
            _entries.AddLast(entry);
            while (_entries.Count > _capacity)
            {
                _entries.RemoveFirst();
            }
        }

        // Notify outside the lock; swallow subscriber errors so logging never breaks.
        var handler = Emitted;
        if (handler != null)
        {
            try { handler(entry); } catch { /* ignore */ }
        }
    }

    public IReadOnlyList<LogEntryDto> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToList();
        }
    }
}
