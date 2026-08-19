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
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<CommunicationObject> CommunicationObjects => Set<CommunicationObject>();
    public DbSet<GroupRange> GroupRanges => Set<GroupRange>();
    public DbSet<ProjectKeyringKey> ProjectKeyringKeys => Set<ProjectKeyringKey>();
    public DbSet<ProjectKeyringBlob> ProjectKeyringBlobs => Set<ProjectKeyringBlob>();
    public DbSet<KnxTelegram> KnxTelegrams => Set<KnxTelegram>();
    public DbSet<KnxConfiguration> KnxConfigurations => Set<KnxConfiguration>();
    public DbSet<RecordingSettings> RecordingSettings => Set<RecordingSettings>();
    public DbSet<MonitorHeartbeat> MonitorHeartbeats => Set<MonitorHeartbeat>();

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
            entity.Property(e => e.EtsProjectId).HasMaxLength(100);
            entity.HasIndex(e => e.EtsProjectId);
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

        // Location entity configuration
        modelBuilder.Entity<Location>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ExternalId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.Type).HasMaxLength(50);
            entity.Property(e => e.ParentExternalId).HasMaxLength(100);

            entity.HasIndex(e => new { e.ProjectId, e.ExternalId });

            entity.HasOne(e => e.Project)
                .WithMany(p => p.Locations)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // CommunicationObject entity configuration
        modelBuilder.Entity<CommunicationObject>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DeviceAddress).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Name).HasMaxLength(500);
            entity.Property(e => e.FunctionText).HasMaxLength(500);
            entity.Property(e => e.GroupAddressLink).IsRequired().HasMaxLength(20);
            entity.Property(e => e.DatapointType).HasMaxLength(50);
            entity.Property(e => e.Flags).HasMaxLength(100);

            entity.HasIndex(e => new { e.ProjectId, e.GroupAddressLink });
            entity.HasIndex(e => new { e.ProjectId, e.DeviceAddress });

            entity.HasOne(e => e.Project)
                .WithMany(p => p.CommunicationObjects)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // GroupRange entity configuration (main/middle group names for the GA tree)
        modelBuilder.Entity<GroupRange>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.RangeStart).IsRequired();
            entity.Property(e => e.RangeEnd).IsRequired();

            entity.HasIndex(e => new { e.ProjectId, e.RangeStart });

            entity.HasOne(e => e.Project)
                .WithMany(p => p.GroupRanges)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ProjectKeyringKey entity configuration
        modelBuilder.Entity<ProjectKeyringKey>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.KeyType).IsRequired().HasMaxLength(30);
            entity.Property(e => e.GroupAddress).HasMaxLength(20);
            entity.Property(e => e.IndividualAddress).HasMaxLength(20);
            entity.Property(e => e.KeyBase64).IsRequired().HasMaxLength(200);

            entity.HasIndex(e => new { e.ProjectId, e.KeyType });

            entity.HasOne(e => e.Project)
                .WithMany(p => p.KeyringKeys)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ProjectKeyringBlob entity configuration (raw .knxkeys + password for runtime Data Secure)
        modelBuilder.Entity<ProjectKeyringBlob>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.KeyringFile).IsRequired();
            entity.Property(e => e.KeyringPassword).IsRequired().HasMaxLength(500);

            // One blob per project.
            entity.HasIndex(e => e.ProjectId).IsUnique();

            entity.HasOne(e => e.Project)
                .WithOne(p => p.KeyringBlob)
                .HasForeignKey<ProjectKeyringBlob>(e => e.ProjectId)
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

            // Ring-buffer is count-based; only the indices the history/query path actually
            // uses are kept (composite for time-range/keyset). EF's FK convention already
            // creates IX_KnxTelegrams_GroupAddressId for the GroupAddress relationship below,
            // so deleting a project's GAs (SetNull on telegrams) is index-backed — the real
            // delete cost was EF's tracked cascade, which ProjectRepository.DeleteProjectFastAsync
            // now bypasses with set-based ExecuteUpdate/ExecuteDelete.
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
            entity.Property(e => e.AutoConnect).IsRequired().HasDefaultValue(true);
            entity.Property(e => e.UseSecureTunnel).IsRequired().HasDefaultValue(false);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();
        });

        // RecordingSettings entity configuration (single-row config)
        modelBuilder.Entity<RecordingSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.HotBufferMaxCount).IsRequired();
            entity.Property(e => e.ArchiveEnabled).IsRequired();
            entity.Property(e => e.ArchiveRetentionDays);
            entity.Property(e => e.UpdatedAt).IsRequired();
        });

        // MonitorHeartbeat entity configuration (liveness record behind the availability report)
        modelBuilder.Entity<MonitorHeartbeat>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Timestamp).IsRequired();
            entity.Property(e => e.State).IsRequired().HasConversion<string>();
            entity.Property(e => e.TelegramsSinceLast).IsRequired();
            // Every read is a range scan over time, and retention deletes by the same key.
            entity.HasIndex(e => e.Timestamp);
        });
    }
}
