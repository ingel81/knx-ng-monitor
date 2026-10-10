using KnxMonitor.Core.Entities;

namespace KnxMonitor.Core.Interfaces;

public interface ITelegramQueue
{
    bool TryEnqueue(KnxTelegram telegram);

    /// <summary>
    /// Buffered history writes only: queues a heartbeat so it is written in the same flush as the
    /// telegrams before it. False when nothing is buffered; the caller writes the beat itself.
    /// </summary>
    bool TryEnqueueHeartbeat(MonitorHeartbeat beat);
}
