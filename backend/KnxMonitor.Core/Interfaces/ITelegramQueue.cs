using KnxMonitor.Core.Entities;

namespace KnxMonitor.Core.Interfaces;

public interface ITelegramQueue
{
    bool TryEnqueue(KnxTelegram telegram);
}
