using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using Xunit.Sdk;

namespace DatasetStudio.Tests.APIBackend
{
    public sealed class PostgreSqlMigrationsTests
    {
        private static string? GetBaseConnectionString()
        {
            string? connectionString = Environment.GetEnvironmentVariable("DATASETSTUDIO_TEST_POSTGRES_CONNECTION");
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                return connectionString;
            }

            connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DatasetStudio");
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                return connectionString;
            }

            return null;
        }

        private static void Skip(string reason)
        {
            throw new SkipException(reason);
        }

        [Fact]
        public async Task MigrateAsync_CreatesExpectedSchema()
        {
            string? baseConnectionString = GetBaseConnectionString();
            if (string.IsNullOrWhiteSpace(baseConnectionString))
            {
                Skip("PostgreSQL connection string not configured. Set DATASETSTUDIO_TEST_POSTGRES_CONNECTION to run this test.");
                return;
            }

            NpgsqlConnectionStringBuilder baseBuilder;
            try
            {
                baseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString);
            }
            catch (Exception ex)
            {
                Skip("Invalid PostgreSQL connection string: " + ex.Message);
                return;
            }

            string databaseName = $"dataset_studio_test_{Guid.NewGuid():N}";

            NpgsqlConnectionStringBuilder adminBuilder = new NpgsqlConnectionStringBuilder(baseBuilder.ConnectionString)
            {
                Database = "postgres",
                Pooling = false
            };

            NpgsqlConnectionStringBuilder testDatabaseBuilder = new NpgsqlConnectionStringBuilder(baseBuilder.ConnectionString)
            {
                Database = databaseName,
                Pooling = false
            };

            try
            {
                await using NpgsqlConnection adminConnection = new NpgsqlConnection(adminBuilder.ConnectionString);
                try
                {
                    await adminConnection.OpenAsync();
                }
                catch (Exception ex)
                {
                    Skip("PostgreSQL is not reachable: " + ex.Message);
                    return;
                }

                try
                {
                    using NpgsqlCommand createDbCommand = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", adminConnection);
                    await createDbCommand.ExecuteNonQueryAsync();
                }
                catch (PostgresException ex) when (ex.SqlState == "42501")
                {
                    Skip("Unable to create test database: " + ex.MessageText);
                    return;
                }
                catch (Exception ex)
                {
                    Skip("Unable to create test database: " + ex.Message);
                    return;
                }

                DbContextOptionsBuilder<DatasetStudioDbContext> dbContextOptionsBuilder =
                    new DbContextOptionsBuilder<DatasetStudioDbContext>();

                dbContextOptionsBuilder.UseNpgsql(testDatabaseBuilder.ConnectionString, npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsAssembly(typeof(DatasetStudioDbContext).Assembly.GetName().Name);
                });

                await using (DatasetStudioDbContext context = new DatasetStudioDbContext(dbContextOptionsBuilder.Options))
                {
                    await context.Database.MigrateAsync();
                }

                await using NpgsqlConnection testConnection = new NpgsqlConnection(testDatabaseBuilder.ConnectionString);
                await testConnection.OpenAsync();

                HashSet<string> expectedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "__EFMigrationsHistory",
                    "users",
                    "datasets",
                    "dataset_items",
                    "captions",
                    "permissions"
                };

                HashSet<string> actualTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                using (NpgsqlCommand listTablesCommand = new NpgsqlCommand(
                           "SELECT tablename FROM pg_tables WHERE schemaname = 'public'",
                           testConnection))
                await using (var reader = await listTablesCommand.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        actualTables.Add(reader.GetString(0));
                    }
                }

                actualTables.Should().Contain(expectedTables);

                using (NpgsqlCommand historyCommand = new NpgsqlCommand(
                           "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\"",
                           testConnection))
                await using (var reader = await historyCommand.ExecuteReaderAsync())
                {
                    List<string> migrations = new List<string>();
                    while (await reader.ReadAsync())
                    {
                        migrations.Add(reader.GetString(0));
                    }

                    migrations.Should().Contain("20251215035334_InitialCreate");
                }

                using NpgsqlCommand adminSeedCommand = new NpgsqlCommand(
                    "SELECT username FROM users WHERE id = '00000000-0000-0000-0000-000000000001'",
                    testConnection);

                object? seedResult = await adminSeedCommand.ExecuteScalarAsync();
                seedResult.Should().Be("admin");
            }
            finally
            {
                try
                {
                    NpgsqlConnection.ClearAllPools();
                }
                catch
                {
                }

                try
                {
                    await using NpgsqlConnection adminConnection = new NpgsqlConnection(adminBuilder.ConnectionString);
                    await adminConnection.OpenAsync();

                    using (NpgsqlCommand terminateCommand = new NpgsqlCommand(
                               "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @dbName AND pid <> pg_backend_pid();",
                               adminConnection))
                    {
                        terminateCommand.Parameters.AddWithValue("dbName", databaseName);
                        await terminateCommand.ExecuteNonQueryAsync();
                    }

                    using NpgsqlCommand dropCommand = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\"", adminConnection);
                    await dropCommand.ExecuteNonQueryAsync();
                }
                catch
                {
                }
            }
        }
    }
}
