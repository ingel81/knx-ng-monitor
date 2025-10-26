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

        // No seeding - use Initial Setup endpoint to create first user
    }
}
