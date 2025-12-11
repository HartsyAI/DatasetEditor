using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL;

/// <summary>
/// Entity Framework Core DbContext for Dataset Studio.
/// Manages database operations for PostgreSQL.
/// </summary>
public class DatasetStudioDbContext : DbContext
{
    public DatasetStudioDbContext(DbContextOptions<DatasetStudioDbContext> options)
        : base(options)
    {
    }

    // DbSet properties for each entity

    /// <summary>
    /// Datasets table
    /// </summary>
    public DbSet<DatasetEntity> Datasets { get; set; } = null!;

    /// <summary>
    /// Dataset items table (for metadata and small datasets)
    /// Note: Large datasets should use Parquet storage
    /// </summary>
    public DbSet<DatasetItemEntity> DatasetItems { get; set; } = null!;

    /// <summary>
    /// Users table
    /// </summary>
    public DbSet<UserEntity> Users { get; set; } = null!;

    /// <summary>
    /// Captions table (for AI-generated and manual captions)
    /// </summary>
    public DbSet<CaptionEntity> Captions { get; set; } = null!;

    /// <summary>
    /// Permissions table (for dataset access control)
    /// </summary>
    public DbSet<PermissionEntity> Permissions { get; set; } = null!;

    /// <summary>
    /// Configure model relationships and constraints
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure DatasetEntity
        modelBuilder.Entity<DatasetEntity>(entity =>
        {
            // Indexes
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.CreatedByUserId);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.Format);
            entity.HasIndex(e => e.Modality);
            entity.HasIndex(e => e.IsPublic);

            // Relationships
            entity.HasOne(d => d.CreatedByUser)
                .WithMany(u => u.CreatedDatasets)
                .HasForeignKey(d => d.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(d => d.Captions)
                .WithOne(c => c.Dataset)
                .HasForeignKey(c => c.DatasetId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(d => d.Permissions)
                .WithOne(p => p.Dataset)
                .HasForeignKey(p => p.DatasetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure DatasetItemEntity
        modelBuilder.Entity<DatasetItemEntity>(entity =>
        {
            // Indexes
            entity.HasIndex(e => e.DatasetId);
            entity.HasIndex(e => new { e.DatasetId, e.ItemId }).IsUnique();
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.QualityScore);
            entity.HasIndex(e => e.IsFlagged);
            entity.HasIndex(e => e.IsDeleted);

            // Relationships
            entity.HasOne(i => i.Dataset)
                .WithMany()
                .HasForeignKey(i => i.DatasetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure UserEntity
        modelBuilder.Entity<UserEntity>(entity =>
        {
            // Indexes
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.Role);
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.CreatedAt);

            // Relationships
            entity.HasMany(u => u.CreatedDatasets)
                .WithOne(d => d.CreatedByUser)
                .HasForeignKey(d => d.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(u => u.Permissions)
                .WithOne(p => p.User)
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure CaptionEntity
        modelBuilder.Entity<CaptionEntity>(entity =>
        {
            // Indexes
            entity.HasIndex(e => e.DatasetId);
            entity.HasIndex(e => new { e.DatasetId, e.ItemId });
            entity.HasIndex(e => e.Source);
            entity.HasIndex(e => e.IsPrimary);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.Score);

            // Full-text search index on caption text (PostgreSQL specific)
            // Uncomment when using PostgreSQL extensions
            // entity.HasIndex(e => e.Text).HasMethod("GIN").IsTsVectorExpressionIndex("english");

            // Relationships
            entity.HasOne(c => c.Dataset)
                .WithMany(d => d.Captions)
                .HasForeignKey(c => c.DatasetId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(c => c.CreatedByUser)
                .WithMany()
                .HasForeignKey(c => c.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Configure PermissionEntity
        modelBuilder.Entity<PermissionEntity>(entity =>
        {
            // Indexes
            entity.HasIndex(e => e.DatasetId);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.DatasetId, e.UserId }).IsUnique();
            entity.HasIndex(e => e.AccessLevel);
            entity.HasIndex(e => e.ExpiresAt);

            // Relationships
            entity.HasOne(p => p.Dataset)
                .WithMany(d => d.Permissions)
                .HasForeignKey(p => p.DatasetId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(p => p.User)
                .WithMany(u => u.Permissions)
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(p => p.GrantedByUser)
                .WithMany()
                .HasForeignKey(p => p.GrantedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Seed data for single-user mode (optional)
        SeedDefaultData(modelBuilder);
    }

    /// <summary>
    /// Seed default data for single-user mode
    /// </summary>
    private void SeedDefaultData(ModelBuilder modelBuilder)
    {
        // Create a default admin user for single-user mode
        var defaultAdminId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        modelBuilder.Entity<UserEntity>().HasData(new UserEntity
        {
            Id = defaultAdminId,
            Username = "admin",
            Email = "admin@localhost",
            PasswordHash = "$2a$11$placeholder_hash_replace_on_first_run", // Should be replaced on first run
            DisplayName = "Administrator",
            Role = "Admin",
            IsActive = true,
            EmailVerified = true,
            CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
    }

    /// <summary>
    /// Override SaveChanges to automatically update timestamps
    /// </summary>
    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    /// <summary>
    /// Override SaveChangesAsync to automatically update timestamps
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Automatically update CreatedAt and UpdatedAt timestamps
    /// </summary>
    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                // Set CreatedAt for new entities
                if (entry.Property("CreatedAt").CurrentValue == null ||
                    (DateTime)entry.Property("CreatedAt").CurrentValue == default)
                {
                    entry.Property("CreatedAt").CurrentValue = DateTime.UtcNow;
                }
            }

            if (entry.State == EntityState.Modified)
            {
                // Set UpdatedAt for modified entities
                if (entry.Metadata.FindProperty("UpdatedAt") != null)
                {
                    entry.Property("UpdatedAt").CurrentValue = DateTime.UtcNow;
                }
            }
        }
    }
}
