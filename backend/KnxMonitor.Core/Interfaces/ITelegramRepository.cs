using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Enums;

namespace KnxMonitor.Core.Interfaces;

public interface ITelegramRepository : IRepository<KnxTelegram>
{
    Task<IEnumerable<KnxTelegram>> GetByDateRangeAsync(DateTime from, DateTime to);
    Task<IEnumerable<KnxTelegram>> GetByGroupAddressAsync(string address);
    Task<IEnumerable<KnxTelegram>> GetByMessageTypeAsync(MessageType messageType);
    Task<IEnumerable<KnxTelegram>> GetPagedAsync(int page, int pageSize);
    Task<int> GetTotalCountAsync();
}
