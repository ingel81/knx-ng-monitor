using KnxMonitor.Core.Entities;

namespace KnxMonitor.Core.Interfaces;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByUsernameAsync(string username);
}
