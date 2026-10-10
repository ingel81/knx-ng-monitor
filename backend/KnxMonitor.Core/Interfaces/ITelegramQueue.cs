using KnxMonitor.Core.Entities;

namespace KnxMonitor.Core.Interfaces;

public interface ITelegramQueue
{
    bool TryEnqueue(KnxTelegram telegram);

    /// <summary>
    /// Buffered history writes only: queues a heartbeat so it is written in the same flush as the
    /// telegrams before it. <see cref="HeartbeatEnqueueResult.NotBuffering"/>: the caller writes the
    /// beat itself. <see cref="HeartbeatEnqueueResult.Dropped"/>: the queue is full; the beat must
    /// not be written ahead of the buffered telegrams.
    /// </summary>
    HeartbeatEnqueueResult TryEnqueueHeartbeat(MonitorHeartbeat beat);
}

public enum HeartbeatEnqueueResult
{
    NotBuffering,
    Enqueued,
    Dropped
}
