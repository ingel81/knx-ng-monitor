using KnxMonitor.Core.Entities;
using Microsoft.EntityFrameworkCore;
using BCrypt.Net;

namespace KnxMonitor.Infrastructure.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(ApplicationDbContext context)
    {
        // Ensure database is created
        await context.Database.MigrateAsync();

        // Check if admin user exists
        if (await context.Users.AnyAsync())
        {
            return; // Database already seeded
        }

        // Create default admin user
        var adminUser = new User
        {
            Username = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin"),
            CreatedAt = DateTime.UtcNow
        };

        context.Users.Add(adminUser);
        await context.SaveChangesAsync();
    }
}
