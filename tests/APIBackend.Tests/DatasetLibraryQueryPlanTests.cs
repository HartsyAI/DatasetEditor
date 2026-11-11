using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Tests.APIBackend.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// The library listing's query plan on a realistically skewed table (audit D-6). The listing is
    /// <c>WHERE is_public OR owner = me OR shared with me ORDER BY created_at DESC OFFSET … LIMIT …</c>.
    /// With the share check as a correlated EXISTS, Postgres could not combine indexes for the OR,
    /// so it walked the created_at index backwards and filtered every row it passed, which grows
    /// with the offset and with the number of datasets the caller can't see.
    /// </summary>
    [Collection(PostgresCollection.Name)]
    public sealed class DatasetLibraryQueryPlanTests : IAsyncLifetime
    {
        private const int DatasetCount = 20_000;
        private const int VisibleCount = 550; // 400 public + 100 owned + 50 shared

        private readonly PostgresContainerFixture _postgres;
        private DatasetStudioApiFactory _factory = default!;

        public DatasetLibraryQueryPlanTests(PostgresContainerFixture postgres)
        {
            _postgres = postgres;
        }

        public async Task InitializeAsync()
        {
            _factory = new DatasetStudioApiFactory(_postgres.ConnectionString);
            await _factory.InitializeDatabaseAsync();
            _ = _factory.Services;
        }

        public async Task DisposeAsync()
        {
            await _factory.DropDatabaseAsync();
            await _factory.DisposeAsync();
        }

        [Fact]
        public async Task LibraryPage_ForASignedInUser_ReadsOnlyVisibleRows()
        {
            Guid caller = Guid.NewGuid();
            string connectionString;
            using (IServiceScope scope = _factory.Services.CreateScope())
            {
                DatasetStudioDbContext db = scope.ServiceProvider.GetRequiredService<DatasetStudioDbContext>();
                connectionString = db.Database.GetConnectionString()!;
                db.Users.Add(new UserEntity
                {
                    Id = caller,
                    Username = "plan-caller",
                    Email = "plan-caller@example.com",
                    PasswordHash = "unused",
                    SecurityStamp = "unused",
                });
                await db.SaveChangesAsync();

                // 2% public, 0.5% owned by the caller, the rest private to nobody in particular.
                await db.Database.ExecuteSqlAsync(
                    $"INSERT INTO datasets (id, name, status, modality, total_items, source_type, is_streaming, is_public, created_by_user_id, created_at, updated_at) SELECT gen_random_uuid(), 'ds-' || g, 2, 'Image', 0, 0, false, g % 50 = 0, CASE WHEN g % 200 = 1 THEN {caller} END, now() - g * interval '1 minute', now() FROM generate_series(1, {DatasetCount}) g");
                // 50 private datasets shared with the caller, plus one whose share has expired.
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO permissions (id, dataset_id, user_id, access_level, can_share, can_delete, granted_at, expires_at) " +
                    "SELECT gen_random_uuid(), id, {0}, 'Read', false, false, now(), CASE WHEN row_number() OVER (ORDER BY name) = 51 THEN now() - interval '1 day' END " +
                    "FROM (SELECT id, name FROM datasets WHERE NOT is_public AND created_by_user_id IS NULL ORDER BY name LIMIT 51) s",
                    caller);
                await db.Database.ExecuteSqlRawAsync("ANALYZE datasets; ANALYZE permissions;");
            }

            PlanCapture capture = new();
            DbContextOptions<DatasetStudioDbContext> options = new DbContextOptionsBuilder<DatasetStudioDbContext>()
                .UseNpgsql(connectionString)
                .AddInterceptors(capture)
                .Options;
            List<DatasetDto> page;
            long total;
            await using (DatasetStudioDbContext db = new(options))
            {
                DatasetRepository repository = new(db);
                DatasetVisibilityFilter visibility = DatasetVisibilityFilter.ForUser(caller, isAdmin: false);
                page = await repository.GetAllAsync(page: 5, pageSize: 50, visibility);
                total = await repository.GetCountAsync(visibility);
            }

            // Same answer as before: only what the caller may see, and the expired share is not in it.
            total.Should().Be(VisibleCount);
            page.Should().HaveCount(50);
            page.Select(d => d.CreatedAt).Should().BeInDescendingOrder();

            string plan = capture.ListingPlan ?? throw new InvalidOperationException("The listing query was not captured.");
            long rowsFiltered = Regex.Matches(plan, @"Rows Removed by Filter: (\d+)").Sum(m => long.Parse(m.Groups[1].Value));
            rowsFiltered.Should().BeLessThan(VisibleCount,
                "a page should read the caller's visible rows through indexes, not filter the whole table; plan:\n" + plan);
            plan.Should().NotContain("SubPlan", "a per-row share check can't be combined with the other indexes; plan:\n" + plan);
        }

        /// <summary>Runs EXPLAIN ANALYZE on the listing query (the one with ORDER BY and OFFSET) as EF sends it.</summary>
        private sealed class PlanCapture : DbCommandInterceptor
        {
            public string? ListingPlan { get; private set; }

            public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
            {
                if (ListingPlan is null && command.CommandText.Contains("ORDER BY", StringComparison.Ordinal)
                    && command.CommandText.Contains("OFFSET", StringComparison.Ordinal))
                {
                    await using NpgsqlCommand explain = new("EXPLAIN (ANALYZE, COSTS OFF, TIMING OFF) " + command.CommandText, (NpgsqlConnection)command.Connection!);
                    foreach (NpgsqlParameter parameter in command.Parameters.Cast<NpgsqlParameter>())
                    {
                        explain.Parameters.Add(parameter.Clone());
                    }
                    List<string> lines = new();
                    await using (NpgsqlDataReader reader = await explain.ExecuteReaderAsync(cancellationToken))
                    {
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            lines.Add(reader.GetString(0));
                        }
                    }
                    ListingPlan = string.Join("\n", lines);
                }
                return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
            }
        }
    }
}
