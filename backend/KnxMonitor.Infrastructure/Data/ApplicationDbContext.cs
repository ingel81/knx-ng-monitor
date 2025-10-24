using Microsoft.EntityFrameworkCore;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Enums;

namespace KnxMonitor.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<GroupAddress> GroupAddresses => Set<GroupAddress>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<KnxTelegram> KnxTelegrams => Set<KnxTelegram>();
    public DbSet<KnxConfiguration> KnxConfigurations => Set<KnxConfiguration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User entity configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
        });

        // RefreshToken entity configuration
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Token).IsRequired();
            entity.HasIndex(e => e.Token).IsUnique();
            entity.Property(e => e.ExpiresAt).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasOne(e => e.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Project entity configuration
        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(500);
            entity.Property(e => e.ImportDate).IsRequired();
        });

        // GroupAddress entity configuration
        modelBuilder.Entity<GroupAddress>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Address).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.DatapointType).HasMaxLength(50);
            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasIndex(e => e.Address);
            entity.HasIndex(e => new { e.ProjectId, e.Address });

            entity.HasOne(e => e.Project)
                .WithMany(p => p.GroupAddresses)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Device entity configuration
        modelBuilder.Entity<Device>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.PhysicalAddress).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Manufacturer).HasMaxLength(200);
            entity.Property(e => e.ProductName).HasMaxLength(200);

            entity.HasIndex(e => e.PhysicalAddress);

            entity.HasOne(e => e.Project)
                .WithMany(p => p.Devices)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // KnxTelegram entity configuration
        modelBuilder.Entity<KnxTelegram>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Timestamp).IsRequired();
            entity.Property(e => e.SourceAddress).IsRequired().HasMaxLength(20);
            entity.Property(e => e.DestinationAddress).IsRequired().HasMaxLength(20);
            entity.Property(e => e.MessageType)
                .IsRequired()
                .HasConversion<string>();
            entity.Property(e => e.Value).IsRequired();
            entity.Property(e => e.ValueDecoded).HasMaxLength(500);
            entity.Property(e => e.Priority).IsRequired();

            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.DestinationAddress);
            entity.HasIndex(e => e.MessageType);
            entity.HasIndex(e => new { e.Timestamp, e.DestinationAddress });

            entity.HasOne(e => e.GroupAddress)
                .WithMany(g => g.Telegrams)
                .HasForeignKey(e => e.GroupAddressId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // KnxConfiguration entity configuration
        modelBuilder.Entity<KnxConfiguration>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.IpAddress).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Port).IsRequired();
            entity.Property(e => e.ConnectionType)
                .IsRequired()
                .HasConversion<string>();
            entity.Property(e => e.PhysicalAddress).IsRequired().HasMaxLength(20);
            entity.Property(e => e.IsActive).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();
        });
    }
}
