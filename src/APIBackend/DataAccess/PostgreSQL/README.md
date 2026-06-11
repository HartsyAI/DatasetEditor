# PostgreSQL Data Access Layer

This directory contains the PostgreSQL database infrastructure for Dataset Studio Phase 2.

## Overview

Dataset Studio uses a hybrid storage approach:
- **PostgreSQL**: Stores dataset metadata, users, captions, and permissions
- **Parquet files**: Stores actual dataset items for large-scale datasets
- **LiteDB** (Legacy): Used in Phase 1, will be migrated to PostgreSQL

## Database Schema

### Tables

#### `users`
Stores user accounts and authentication information.

| Column | Type | Description |
|--------|------|-------------|
| `id` | uuid | Primary key |
| `username` | varchar(100) | Unique username |
| `email` | varchar(200) | Unique email address |
| `password_hash` | varchar(500) | Bcrypt password hash |
| `display_name` | varchar(200) | Display name (optional) |
| `role` | varchar(50) | User role (Admin, User, Guest) |
| `is_active` | boolean | Account active status |
| `email_verified` | boolean | Email verification status |
| `avatar_url` | varchar(500) | Profile picture URL (optional) |
| `preferences` | jsonb | User preferences/settings |
| `created_at` | timestamp | Account creation time |
| `last_login_at` | timestamp | Last login time |
| `updated_at` | timestamp | Last update time |

**Indexes**: `username` (unique), `email` (unique), `role`, `is_active`, `created_at`

---

#### `datasets`
Stores dataset metadata and configuration.

| Column | Type | Description |
|--------|------|-------------|
| `id` | uuid | Primary key |
| `name` | varchar(200) | Dataset display name |
| `description` | text | Dataset description (optional) |
| `format` | varchar(50) | Dataset format (ImageFolder, Parquet, HuggingFace) |
| `modality` | varchar(50) | Data modality (Image, Text, Audio, Video) |
| `item_count` | integer | Total number of items |
| `total_size_bytes` | bigint | Total size in bytes |
| `storage_path` | varchar(500) | File storage location |
| `parquet_path` | varchar(500) | Parquet file path (optional) |
| `huggingface_repo_id` | varchar(200) | HuggingFace repository (optional) |
| `huggingface_split` | varchar(50) | HuggingFace split (train/val/test) |
| `is_public` | boolean | Public/private visibility |
| `metadata` | jsonb | Additional metadata |
| `created_at` | timestamp | Creation time |
| `updated_at` | timestamp | Last update time |
| `created_by_user_id` | uuid | Foreign key to users (optional) |

**Indexes**: `name`, `created_by_user_id`, `created_at`, `format`, `modality`, `is_public`

**Relationships**:
- `created_by_user_id` → `users.id` (SET NULL on delete)

---

#### `dataset_items`
Stores individual item metadata (for small datasets or metadata-only storage).

**Note**: Large datasets should use Parquet files instead of this table for item storage.

| Column | Type | Description |
|--------|------|-------------|
| `id` | uuid | Primary key |
| `dataset_id` | uuid | Foreign key to datasets |
| `item_id` | varchar(500) | Unique identifier within dataset |
| `file_path` | varchar(1000) | File path or URL |
| `mime_type` | varchar(100) | MIME type (image/jpeg, etc.) |
| `file_size_bytes` | bigint | File size |
| `width` | integer | Image/video width |
| `height` | integer | Image/video height |
| `duration_seconds` | real | Audio/video duration |
| `caption` | text | Primary caption/label |
| `tags` | text | Associated tags |
| `quality_score` | real | Quality score (0.0-1.0) |
| `metadata` | jsonb | Additional item properties |
| `embedding` | bytea | Embedding vector for similarity search |
| `is_flagged` | boolean | Flagged for review |
| `is_deleted` | boolean | Soft delete flag |
| `created_at` | timestamp | Creation time |
| `updated_at` | timestamp | Last update time |

**Indexes**: `dataset_id`, `(dataset_id, item_id)` (unique), `created_at`, `quality_score`, `is_flagged`, `is_deleted`

**Relationships**:
- `dataset_id` → `datasets.id` (CASCADE on delete)

---

#### `captions`
Stores AI-generated and manual captions/annotations.

| Column | Type | Description |
|--------|------|-------------|
| `id` | uuid | Primary key |
| `dataset_id` | uuid | Foreign key to datasets |
| `item_id` | varchar(500) | Item identifier within dataset |
| `text` | text | Caption text |
| `source` | varchar(100) | Caption source (Manual, BLIP, GPT-4, etc.) |
| `score` | real | Confidence/quality score (optional) |
| `language` | varchar(10) | Language code (en, es, fr, etc.) |
| `is_primary` | boolean | Primary caption for the item |
| `metadata` | jsonb | Additional caption properties |
| `created_at` | timestamp | Creation time |
| `created_by_user_id` | uuid | Foreign key to users (optional for AI) |
| `updated_at` | timestamp | Last update time |

**Indexes**: `dataset_id`, `(dataset_id, item_id)`, `source`, `is_primary`, `created_at`, `score`

**Relationships**:
- `dataset_id` → `datasets.id` (CASCADE on delete)
- `created_by_user_id` → `users.id` (SET NULL on delete)

---

#### `permissions`
Stores dataset access control and sharing permissions.

| Column | Type | Description |
|--------|------|-------------|
| `id` | uuid | Primary key |
| `dataset_id` | uuid | Foreign key to datasets |
| `user_id` | uuid | Foreign key to users |
| `access_level` | varchar(50) | Access level (Read, Write, Admin, Owner) |
| `can_share` | boolean | Can share with others |
| `can_delete` | boolean | Can delete dataset |
| `expires_at` | timestamp | Permission expiration (optional) |
| `granted_at` | timestamp | When permission was granted |
| `granted_by_user_id` | uuid | Who granted the permission |
| `updated_at` | timestamp | Last update time |

**Indexes**: `dataset_id`, `user_id`, `(dataset_id, user_id)` (unique), `access_level`, `expires_at`

**Relationships**:
- `dataset_id` → `datasets.id` (CASCADE on delete)
- `user_id` → `users.id` (CASCADE on delete)
- `granted_by_user_id` → `users.id` (SET NULL on delete)

---

## Setting Up PostgreSQL Locally

### Option 1: Using Docker (Recommended)

1. **Install Docker Desktop** from https://www.docker.com/products/docker-desktop/

2. **Create a `docker-compose.yml` file** in the project root:

```yaml
version: '3.8'
services:
  postgres:
    image: postgres:16-alpine
    container_name: dataset_studio_db
    environment:
      POSTGRES_DB: dataset_studio_dev
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports:
      - "5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data
    restart: unless-stopped

volumes:
  postgres_data:
```

3. **Start PostgreSQL**:
```bash
docker-compose up -d
```

4. **Verify it's running**:
```bash
docker ps
```

5. **Stop PostgreSQL**:
```bash
docker-compose down
```

---

### Option 2: Native Installation

#### Windows

1. Download PostgreSQL from https://www.postgresql.org/download/windows/
2. Run the installer and set a password for the `postgres` user
3. Default port is `5432`
4. Use pgAdmin (included) to manage databases

#### macOS

Using Homebrew:
```bash
brew install postgresql@16
brew services start postgresql@16
createdb dataset_studio_dev
```

#### Linux (Ubuntu/Debian)

```bash
sudo apt update
sudo apt install postgresql postgresql-contrib
sudo systemctl start postgresql
sudo -u postgres createdb dataset_studio_dev
```

---

### Option 3: Using a Cloud PostgreSQL Service

- **Supabase** (free tier): https://supabase.com/
- **Neon** (free tier): https://neon.tech/
- **Railway** (free tier): https://railway.app/
- **Heroku Postgres** (free tier): https://www.heroku.com/postgres

Update the connection string in `appsettings.json` with your cloud database credentials.

---

## Running Migrations

### Prerequisites

Ensure you have the EF Core CLI tools installed:

```bash
dotnet tool install --global dotnet-ef
# or update existing:
dotnet tool update --global dotnet-ef
```

### Creating Your First Migration

From the `src/APIBackend` directory:

```bash
# Create the initial migration
dotnet ef migrations add InitialCreate --context DatasetStudioDbContext --output-dir DataAccess/PostgreSQL/Migrations

# Apply the migration to the database
dotnet ef database update --context DatasetStudioDbContext
```

### Common Migration Commands

```bash
# Create a new migration
dotnet ef migrations add <MigrationName> --context DatasetStudioDbContext

# Apply all pending migrations
dotnet ef database update --context DatasetStudioDbContext

# Rollback to a specific migration
dotnet ef database update <MigrationName> --context DatasetStudioDbContext

# Remove the last migration (if not applied)
dotnet ef migrations remove --context DatasetStudioDbContext

# View migration status
dotnet ef migrations list --context DatasetStudioDbContext

# Generate SQL script without applying
dotnet ef migrations script --context DatasetStudioDbContext --output migration.sql
```

### Migration Best Practices

1. **Always create a migration** when changing entity models
2. **Review the generated migration** before applying it
3. **Test migrations** on a development database first
4. **Use descriptive names** for migrations (e.g., `AddUserPreferences`, `AddCaptionScoring`)
5. **Never delete migrations** that have been applied to production
6. **Create rollback scripts** for critical migrations

---

## Configuring the Application

### Update `appsettings.json`

The connection string is already configured:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=dataset_studio;Username=postgres;Password=your_password_here;Include Error Detail=true"
  },
  "Database": {
    "LiteDbPath": "./data/hartsy.db",
    "UsePostgreSQL": false
  }
}
```

### Update `appsettings.Development.json`

Development settings use a separate database:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=dataset_studio_dev;Username=postgres;Password=postgres;Include Error Detail=true"
  },
  "Database": {
    "UsePostgreSQL": false
  }
}
```

### Enable PostgreSQL in Program.cs

To switch from LiteDB to PostgreSQL, update `Program.cs`:

```csharp
// Add to ConfigureServices
var usePostgreSql = builder.Configuration.GetValue<bool>("Database:UsePostgreSQL");

if (usePostgreSql)
{
    builder.Services.AddDbContext<DatasetStudioDbContext>(options =>
        options.UseNpgsql(
            builder.Configuration.GetConnectionString("DefaultConnection"),
            npgsqlOptions => npgsqlOptions.EnableRetryOnFailure()
        )
    );

    // Register PostgreSQL repositories
    builder.Services.AddScoped<IDatasetRepository, PostgreSqlDatasetRepository>();
}
else
{
    // Use LiteDB repositories (legacy)
    builder.Services.AddScoped<IDatasetRepository, LiteDbDatasetRepository>();
}
```

Then set `"UsePostgreSQL": true` in `appsettings.json` when ready to switch.

---

## Database Connection Strings

### Local Development (Docker)
```
Host=localhost;Port=5432;Database=dataset_studio_dev;Username=postgres;Password=postgres;Include Error Detail=true
```

### Local Development (Native)
```
Host=localhost;Port=5432;Database=dataset_studio;Username=postgres;Password=your_password;Include Error Detail=true
```

### Production
```
Host=your-db-host.com;Port=5432;Database=dataset_studio;Username=dataset_studio_user;Password=strong_password;SSL Mode=Require;Include Error Detail=false
```

### Cloud Services

**Supabase**:
```
Host=db.your-project.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=your_password;SSL Mode=Require
```

**Neon**:
```
Host=your-project.neon.tech;Port=5432;Database=neondb;Username=your_username;Password=your_password;SSL Mode=Require
```

---

## Environment Variables (Optional)

For security, use environment variables instead of hardcoded passwords:

```bash
# Linux/macOS
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=dataset_studio;Username=postgres;Password=your_password"

# Windows (PowerShell)
$env:ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=dataset_studio;Username=postgres;Password=your_password"

# Or use User Secrets (Development only)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=dataset_studio_dev;Username=postgres;Password=postgres"
```

---

## Troubleshooting

### Connection Issues

**Error**: `NpgsqlException: could not connect to server`
- Ensure PostgreSQL is running (`docker ps` or check system services)
- Verify the host and port in the connection string
- Check firewall settings

**Error**: `password authentication failed for user "postgres"`
- Verify the password in your connection string
- Reset the PostgreSQL password if needed

### Migration Issues

**Error**: `The entity type 'X' requires a primary key to be defined`
- Ensure all entities have a `[Key]` attribute or are configured in `OnModelCreating`

**Error**: `A migration has already been applied`
- Use `dotnet ef database update <PreviousMigration>` to rollback first

### Performance Issues

- **Add indexes** for frequently queried columns
- **Use JSONB** for flexible metadata storage
- **Enable query logging** in development to identify slow queries
- **Use connection pooling** (enabled by default in Npgsql)

---

## Performance Optimization

### Indexing Strategy

The schema includes indexes on:
- Primary keys (automatic)
- Foreign keys (dataset_id, user_id, etc.)
- Frequently filtered columns (created_at, format, modality, etc.)
- Unique constraints (username, email, etc.)

### Query Optimization Tips

1. **Use async methods** for all database operations
2. **Batch operations** when inserting/updating multiple records
3. **Use pagination** for large result sets
4. **Avoid N+1 queries** by using `.Include()` for related entities
5. **Use projections** (select only needed columns) with LINQ

Example:
```csharp
// Good
var datasets = await context.Datasets
    .Where(d => d.IsPublic)
    .Select(d => new DatasetSummaryDto { Id = d.Id, Name = d.Name })
    .ToListAsync();

// Bad (loads all columns)
var datasets = await context.Datasets
    .Where(d => d.IsPublic)
    .ToListAsync();
```

---

## Backup and Restore

### Backup Database

```bash
# Using Docker
docker exec dataset_studio_db pg_dump -U postgres dataset_studio_dev > backup.sql

# Using native PostgreSQL
pg_dump -U postgres dataset_studio > backup.sql
```

### Restore Database

```bash
# Using Docker
cat backup.sql | docker exec -i dataset_studio_db psql -U postgres dataset_studio_dev

# Using native PostgreSQL
psql -U postgres dataset_studio < backup.sql
```

---

## Monitoring

### View Active Connections

```sql
SELECT * FROM pg_stat_activity WHERE datname = 'dataset_studio_dev';
```

### Check Database Size

```sql
SELECT pg_size_pretty(pg_database_size('dataset_studio_dev'));
```

### View Table Sizes

```sql
SELECT
    schemaname,
    tablename,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename)) AS size
FROM pg_tables
WHERE schemaname = 'public'
ORDER BY pg_total_relation_size(schemaname||'.'||tablename) DESC;
```

---

## Next Steps

1. **Enable PostgreSQL** by setting `"UsePostgreSQL": true` in appsettings.json
2. **Create initial migration**: `dotnet ef migrations add InitialCreate`
3. **Apply migration**: `dotnet ef database update`
4. **Create repositories** in `DataAccess/PostgreSQL/Repositories/`
5. **Migrate data** from LiteDB to PostgreSQL using a migration script
6. **Update Program.cs** to register DbContext and repositories

---

## Additional Resources

- [Entity Framework Core Documentation](https://learn.microsoft.com/en-us/ef/core/)
- [Npgsql EF Core Provider](https://www.npgsql.org/efcore/)
- [PostgreSQL Documentation](https://www.postgresql.org/docs/)
- [Dataset Studio Architecture](../../../REFACTOR_PLAN.md)

---

**Phase**: Phase 2 - Database Migration
**Status**: Infrastructure Ready (awaiting repository implementation)
**Last Updated**: 2025-12-11
