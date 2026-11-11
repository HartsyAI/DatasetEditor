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
    /// Users table
    /// </summary>
    public DbSet<UserEntity> Users { get; set; } = null!;

    /// <summary>
    /// Permissions table (for dataset access control)
    /// </summary>
    public DbSet<PermissionEntity> Permissions { get; set; } = null!;

    /// <summary>Queued, running and finished ingestion jobs (audit I-1).</summary>
    public DbSet<IngestionJobEntity> IngestionJobs { get; set; } = null!;

    /// <summary>Edits of streamed (HuggingFace, S3) items, applied over their pages (audit E-3).</summary>
    public DbSet<ItemOverlayEntity> ItemOverlays { get; set; } = null!;

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
            // The library lists `is_public OR owner OR shared ORDER BY created_at DESC` (audit D-6).
            // One index per OR branch lets Postgres combine them; each also carries created_at so
            // a single-branch listing (anonymous: public only) is an ordered index scan with no
            // sort. The owner index replaces the plain created_by_user_id one (same leading column,
            // so it still serves the foreign key), and the partial public index replaces a boolean
            // index over every row. created_at alone stays for the unfiltered admin listing.
            entity.HasIndex(e => new { e.CreatedByUserId, e.CreatedAt });
            entity.HasIndex(e => e.CreatedAt);
            // Named, so EF keeps it apart from the unfiltered created_at index above.
            entity.HasIndex(e => e.CreatedAt, "IX_datasets_created_at_public")
                .HasFilter("is_public");
            // No index on modality: every row holds "Image", so it only cost writes (audit D-5).

            // Relationships
            entity.HasOne(d => d.CreatedByUser)
                .WithMany(u => u.CreatedDatasets)
                .HasForeignKey(d => d.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(d => d.Permissions)
                .WithOne(p => p.Dataset)
                .HasForeignKey(p => p.DatasetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure UserEntity
        modelBuilder.Entity<UserEntity>(entity =>
        {
            // Indexes
            // Username and email are unique case-insensitively, through unique indexes on
            // lower(username) and lower(email) created in the AddAccountHardening migration (EF
            // can't model expression indexes). The old case-sensitive unique indexes let "Alice"
            // and "alice" both register (audit A-4).
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

        modelBuilder.Entity<IngestionJobEntity>(entity =>
        {
            // Claim order: oldest queued job first.
            entity.HasIndex(e => new { e.State, e.CreatedAt });

            // At most one running job per dataset. Two workers can each pick a different queued
            // job for the same dataset in the same instant (SKIP LOCKED only stops them taking the
            // same row), and ingestion appends to the dataset's shards, so the loser of this
            // index fails its claim and tries again later.
            entity.HasIndex(e => e.DatasetId)
                .IsUnique()
                .HasFilter("state = 1")
                .HasDatabaseName("ix_ingestion_jobs_one_running_per_dataset");

            entity.HasOne(j => j.Dataset)
                .WithMany()
                .HasForeignKey(j => j.DatasetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ItemOverlayEntity>(entity =>
        {
            // One overlay per item; a page's overlays are read by (dataset_id, item_id = ANY(...)).
            entity.HasKey(e => new { e.DatasetId, e.ItemId });

            entity.HasOne(o => o.Dataset)
                .WithMany()
                .HasForeignKey(o => o.DatasetId)
                .OnDelete(DeleteBehavior.Cascade);
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
                // Set CreatedAt for new entities -- guarded the same way the UpdatedAt branch
                // below already is. PermissionEntity has no CreatedAt property (it has
                // GrantedAt instead), and entry.Property("CreatedAt") throws
                // InvalidOperationException for an entity type that doesn't declare it, so an
                // unconditional call here made every PermissionEntity insert throw. Nothing
                // exercised this path before -- Permissions had zero write call sites until
                // Phase 3's dataset-permission-grant tests became the first code to insert one.
                if (entry.Metadata.FindProperty("CreatedAt") != null)
                {
                    object? createdAtValue = entry.Property("CreatedAt").CurrentValue;
                    if (createdAtValue is not DateTime existingCreatedAt || existingCreatedAt == default)
                    {
                        entry.Property("CreatedAt").CurrentValue = DateTime.UtcNow;
                    }
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
