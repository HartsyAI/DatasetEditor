using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using DatasetStudio.DTO.Datasets;
using DatasetStudio.Tests.APIBackend.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using DatasetStudio.APIBackend.DataAccess.PostgreSQL.Repositories;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// PR 3.1 (audit I-1, I-3, I-4): ingestion jobs live in <c>ingestion_jobs</c>, survive a
    /// restart, are claimed once each with at most one running job per dataset, are recovered at
    /// startup, and the queue refuses work past its limit. Runs against real Postgres because the
    /// claim relies on <c>FOR UPDATE SKIP LOCKED</c> and a partial unique index.
    /// </summary>
    [Collection(PostgresCollection.Name)]
    public sealed class IngestionQueueTests : IAsyncLifetime
    {
        /// <summary>Ingestion:MaxQueuedJobs for these tests.</summary>
        private const int QueueLimit = 10;

        private readonly PostgresContainerFixture _postgres;
        private readonly List<string> _foldersToDelete = new();
        private DatasetStudioApiFactory _factory = default!;

        public IngestionQueueTests(PostgresContainerFixture postgres)
        {
            _postgres = postgres;
        }

        public async Task InitializeAsync()
        {
            // No live worker: each test drives the store and the recovery step itself.
            _factory = CreateFactory(runWorker: false, maxQueuedJobs: QueueLimit);
            await _factory.InitializeDatabaseAsync();
        }

        public async Task DisposeAsync()
        {
            await _factory.DropDatabaseAsync();
            await _factory.DisposeAsync();
            foreach (string folder in _foldersToDelete.Where(Directory.Exists))
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        private DatasetStudioApiFactory CreateFactory(bool runWorker, int maxQueuedJobs = 50, string? databaseName = null)
        {
            DatasetStudioApiFactory factory = new(_postgres.ConnectionString, databaseName);
            factory.ConfigureTestServices = services =>
            {
                if (!runWorker)
                {
                    ServiceDescriptor? worker = services.FirstOrDefault(d =>
                        d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(IngestionBackgroundService));
                    if (worker is not null)
                    {
                        services.Remove(worker);
                    }
                }
                services.Configure<IngestionOptions>(options =>
                {
                    options.MaxQueuedJobs = maxQueuedJobs;
                    options.PollInterval = TimeSpan.FromMilliseconds(200);
                });
            };
            return factory;
        }

        private async Task<Guid> CreateDatasetAsync(IngestionStatusDto status = IngestionStatusDto.Pending)
        {
            using IServiceScope scope = _factory.Services.CreateScope();
            IDatasetRepository datasets = scope.ServiceProvider.GetRequiredService<IDatasetRepository>();
            Guid id = Guid.NewGuid();
            await datasets.CreateAsync(new DatasetDto { Id = id, Name = "queue-" + id.ToString("N")[..8], Status = status });
            return id;
        }

        private async Task EnqueueAsync(IngestionJob job)
        {
            using IServiceScope scope = _factory.Services.CreateScope();
            (await scope.ServiceProvider.GetRequiredService<IIngestionJobQueue>().TryEnqueueAsync(job, CancellationToken.None))
                .Should().BeTrue("the test queue has room");
        }

        private async Task<ClaimedIngestionJob?> ClaimAsync(string worker = "test-worker")
        {
            using IServiceScope scope = _factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IIngestionJobStore>().TryClaimNextAsync(worker, CancellationToken.None);
        }

        private async Task<IngestionJobEntity> GetJobAsync(Guid jobId)
        {
            using IServiceScope scope = _factory.Services.CreateScope();
            DatasetStudioDbContext db = scope.ServiceProvider.GetRequiredService<DatasetStudioDbContext>();
            return await db.IngestionJobs.SingleAsync(j => j.Id == jobId);
        }

        private async Task<DatasetDto> GetDatasetAsync(Guid datasetId)
        {
            using IServiceScope scope = _factory.Services.CreateScope();
            return (await scope.ServiceProvider.GetRequiredService<IDatasetRepository>().GetByIdAsync(datasetId))!;
        }

        private IngestionBackgroundService CreateWorkerHost(int maxAttempts = 3)
        {
            return new IngestionBackgroundService(
                _factory.Services.GetRequiredService<IServiceScopeFactory>(),
                _factory.Services.GetRequiredService<IngestionSignal>(),
                Options.Create(new IngestionOptions { MaxAttempts = maxAttempts }),
                NullLogger<IngestionBackgroundService>.Instance);
        }

        /// <summary>A HuggingFace token is stored encrypted, comes back intact, and is dropped when the job ends.</summary>
        [Fact]
        public async Task Enqueue_HuggingFaceToken_IsEncryptedAtRestAndRemovedWhenFinished()
        {
            Guid datasetId = await CreateDatasetAsync();
            const string token = "hf_secret_token_value";
            await EnqueueAsync(new IngestionJob(datasetId, null,
                new ImportHuggingFaceDatasetRequest { Repository = "org/ds", AccessToken = token }));

            ClaimedIngestionJob claimed = (await ClaimAsync())!;
            IngestionJobEntity stored = await GetJobAsync(claimed.Id);

            stored.PayloadJson.Should().NotContain(token, "the token must not sit in the database in clear text");
            claimed.Job.HuggingFaceRequest!.AccessToken.Should().Be(token);
            claimed.Job.HuggingFaceRequest.Repository.Should().Be("org/ds");

            using (IServiceScope scope = _factory.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IIngestionJobStore>().CompleteAsync(claimed.Id, CancellationToken.None);
            }

            IngestionJobEntity finished = await GetJobAsync(claimed.Id);
            finished.State.Should().Be(IngestionJobState.Completed);
            finished.PayloadJson.Should().Contain("org/ds").And.NotContain("AccessToken\":\"", "the encrypted token is removed once the job is done");
        }

        /// <summary>Oldest first, and a dataset with a running job is skipped until that job finishes.</summary>
        [Fact]
        public async Task TryClaimNext_TakesOldestFirst_AndRunsOneJobPerDatasetAtATime()
        {
            Guid datasetA = await CreateDatasetAsync();
            Guid datasetB = await CreateDatasetAsync();
            await EnqueueAsync(new IngestionJob(datasetA, "/staged/a1.csv"));
            await EnqueueAsync(new IngestionJob(datasetA, "/staged/a2.csv"));
            await EnqueueAsync(new IngestionJob(datasetB, "/staged/b1.csv"));

            ClaimedIngestionJob first = (await ClaimAsync())!;
            ClaimedIngestionJob second = (await ClaimAsync())!;
            ClaimedIngestionJob? third = await ClaimAsync();

            first.Job.FilePath.Should().Be("/staged/a1.csv");
            second.Job.FilePath.Should().Be("/staged/b1.csv", "a2 waits while a1 runs on the same dataset");
            third.Should().BeNull();
            first.Attempts.Should().Be(1);

            using (IServiceScope scope = _factory.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IIngestionJobStore>().CompleteAsync(first.Id, CancellationToken.None);
            }

            ClaimedIngestionJob next = (await ClaimAsync())!;
            next.Job.FilePath.Should().Be("/staged/a2.csv");
        }

        /// <summary>Many workers claiming at once never take the same job or two jobs of one dataset.</summary>
        [Fact]
        public async Task TryClaimNext_ConcurrentWorkers_ClaimEachJobOnce()
        {
            List<Guid> datasetIds = new();
            for (int i = 0; i < 3; i++)
            {
                Guid id = await CreateDatasetAsync();
                datasetIds.Add(id);
                // Two jobs per dataset: only one of each pair may run.
                await EnqueueAsync(new IngestionJob(id, $"/staged/{i}-1.csv"));
                await EnqueueAsync(new IngestionJob(id, $"/staged/{i}-2.csv"));
            }

            ClaimedIngestionJob?[] results = await Task.WhenAll(
                Enumerable.Range(0, 12).Select(i => Task.Run(() => ClaimAsync($"worker-{i}"))));
            List<ClaimedIngestionJob> claimed = results.Where(r => r is not null).Select(r => r!).ToList();

            claimed.Select(c => c.Id).Should().OnlyHaveUniqueItems();
            claimed.GroupBy(c => c.Job.DatasetId).Should().OnlyContain(g => g.Count() == 1,
                "a dataset never has two running jobs");
            claimed.Should().HaveCountLessThanOrEqualTo(datasetIds.Count);
        }

        /// <summary>
        /// Audit I-1: a job queued before the process stopped is run by the next process. With the
        /// in-memory channel it was lost and the dataset stayed Pending forever.
        /// </summary>
        [Fact]
        public async Task QueuedJob_SurvivesRestart_AndIsRunByTheNextProcess()
        {
            string folder = Path.Combine(Path.GetTempPath(), "DatasetStudioTests", "queue-restart", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            _foldersToDelete.Add(folder);
            byte[] fakeJpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02 };
            await File.WriteAllBytesAsync(Path.Combine(folder, "one.jpg"), fakeJpeg);
            await File.WriteAllBytesAsync(Path.Combine(folder, "two.png"), fakeJpeg);

            Guid datasetId = await CreateDatasetAsync();
            await EnqueueAsync(new IngestionJob(datasetId, null, FolderPath: folder));
            string databaseName = _factory.DatabaseName;

            // "Restart": stop this host without running the job, then start one with workers on
            // the same database. The fixture's DisposeAsync drops the database via the new host.
            await _factory.DisposeAsync();
            _factory = CreateFactory(runWorker: true, databaseName: databaseName);
            _ = _factory.Services;

            DatasetDto dataset = await GetDatasetAsync(datasetId);
            for (int i = 0; i < 300 && dataset.Status != IngestionStatusDto.Completed && dataset.Status != IngestionStatusDto.Failed; i++)
            {
                await Task.Delay(100);
                dataset = await GetDatasetAsync(datasetId);
            }

            using (IServiceScope scope = _factory.Services.CreateScope())
            {
                IngestionJobEntity job = await scope.ServiceProvider.GetRequiredService<DatasetStudioDbContext>()
                    .IngestionJobs.SingleAsync(j => j.DatasetId == datasetId);
                string diagnosis = $"the restarted host should run the queued job (job {job.State}, attempts {job.Attempts}, "
                    + $"job error '{job.Error}', dataset error '{dataset.ErrorMessage}')";
                dataset.Status.Should().Be(IngestionStatusDto.Completed, diagnosis);
                job.State.Should().Be(IngestionJobState.Completed, diagnosis);
            }
            dataset.TotalItems.Should().Be(2);
        }

        /// <summary>
        /// At startup a running job that wrote nothing goes back in the queue; one that wrote items
        /// is failed (running it again would append duplicates) and the dataset says how many it kept.
        /// </summary>
        [Fact]
        public async Task Recovery_RequeuesJobsThatWroteNothing_AndFailsJobsThatWroteItems()
        {
            Guid cleanDataset = await CreateDatasetAsync(IngestionStatusDto.Processing);
            Guid partialDataset = await CreateDatasetAsync(IngestionStatusDto.Processing);
            await EnqueueAsync(new IngestionJob(cleanDataset, "/staged/clean.csv"));
            await EnqueueAsync(new IngestionJob(partialDataset, "/staged/partial.csv"));
            ClaimedIngestionJob cleanJob = (await ClaimAsync())!;
            ClaimedIngestionJob partialJob = (await ClaimAsync())!;

            using (IServiceScope scope = _factory.Services.CreateScope())
            {
                IIngestionJobStore store = scope.ServiceProvider.GetRequiredService<IIngestionJobStore>();
                await store.SetItemsBeforeAsync(cleanJob.Id, 0, CancellationToken.None);
                await store.SetItemsBeforeAsync(partialJob.Id, 0, CancellationToken.None);
                // Progress the interrupted run reported for rows that never reached a shard.
                await scope.ServiceProvider.GetRequiredService<IDatasetRepository>().UpdateItemCountAsync(cleanDataset, 4_000);
                IDatasetItemRepository items = scope.ServiceProvider.GetRequiredService<IDatasetItemRepository>();
                await items.AddRangeAsync(partialDataset, Enumerable.Range(0, 3).Select(i => new DatasetItemDto
                {
                    Id = Guid.NewGuid(),
                    DatasetId = partialDataset,
                    ExternalId = $"row-{i}",
                    Title = $"item {i}",
                }).ToList());
            }

            await CreateWorkerHost().RecoverInterruptedJobsAsync(CancellationToken.None);

            (await GetJobAsync(cleanJob.Id)).State.Should().Be(IngestionJobState.Queued);
            DatasetDto clean = await GetDatasetAsync(cleanDataset);
            clean.Status.Should().Be(IngestionStatusDto.Pending);
            clean.TotalItems.Should().Be(0, "the stale progress count is replaced by the real item count");

            IngestionJobEntity failedJob = await GetJobAsync(partialJob.Id);
            failedJob.State.Should().Be(IngestionJobState.Failed);
            DatasetDto partial = await GetDatasetAsync(partialDataset);
            partial.Status.Should().Be(IngestionStatusDto.Failed);
            partial.ErrorMessage.Should().Contain("3 item(s)");
            partial.TotalItems.Should().Be(3);
        }

        /// <summary>
        /// A job whose dataset can't be counted (a shard cut short by the crash) is failed. Left
        /// Running, it would block every later job for that dataset.
        /// </summary>
        [Fact]
        public async Task Recovery_UnreadableDataset_FailsJobInsteadOfLeavingItRunning()
        {
            Guid datasetId = await CreateDatasetAsync(IngestionStatusDto.Processing);
            await EnqueueAsync(new IngestionJob(datasetId, "/staged/torn.csv"));
            ClaimedIngestionJob claimed = (await ClaimAsync())!;
            Directory.CreateDirectory(_factory.ParquetPath);
            await File.WriteAllBytesAsync(
                Path.Combine(_factory.ParquetPath, $"dataset_{datasetId:N}_shard_000.parquet"),
                new byte[] { 0x50, 0x41, 0x52, 0x31, 0x00, 0x01 });

            await CreateWorkerHost().RecoverInterruptedJobsAsync(CancellationToken.None);

            (await GetJobAsync(claimed.Id)).State.Should().Be(IngestionJobState.Failed);
            DatasetDto dataset = await GetDatasetAsync(datasetId);
            dataset.Status.Should().Be(IngestionStatusDto.Failed);
            dataset.ErrorMessage.Should().StartWith("Ingestion was interrupted by a server restart and could not be recovered");
        }

        /// <summary>A job interrupted on its last allowed attempt is failed instead of retried forever.</summary>
        [Fact]
        public async Task Recovery_FailsJobThatUsedAllAttempts()
        {
            Guid datasetId = await CreateDatasetAsync(IngestionStatusDto.Processing);
            await EnqueueAsync(new IngestionJob(datasetId, "/staged/x.csv"));
            ClaimedIngestionJob claimed = (await ClaimAsync())!;

            await CreateWorkerHost(maxAttempts: 1).RecoverInterruptedJobsAsync(CancellationToken.None);

            (await GetJobAsync(claimed.Id)).State.Should().Be(IngestionJobState.Failed);
            (await GetDatasetAsync(datasetId)).Status.Should().Be(IngestionStatusDto.Failed);
        }

        /// <summary>
        /// A dataset left Processing with no job (its job was lost before jobs were persisted) is
        /// failed with a reason. A Pending dataset with no job is just waiting for an upload.
        /// </summary>
        [Fact]
        public async Task Recovery_FailsProcessingDatasetsWithNoJob_AndLeavesPendingOnes()
        {
            Guid stuck = await CreateDatasetAsync(IngestionStatusDto.Processing);
            Guid waiting = await CreateDatasetAsync(IngestionStatusDto.Pending);

            await CreateWorkerHost().RecoverInterruptedJobsAsync(CancellationToken.None);

            DatasetDto stuckNow = await GetDatasetAsync(stuck);
            stuckNow.Status.Should().Be(IngestionStatusDto.Failed);
            stuckNow.ErrorMessage.Should().Be(IngestionBackgroundService.OrphanedDatasetMessage);
            (await GetDatasetAsync(waiting)).Status.Should().Be(IngestionStatusDto.Pending);
        }

        private async Task<Guid?> ReserveAsync(IngestionJob job)
        {
            using IServiceScope scope = _factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IIngestionJobQueue>().TryReserveAsync(job, CancellationToken.None);
        }

        /// <summary>
        /// Audit I-4 (Codex review on #19): many uploads arriving at once near the limit are admitted
        /// one at a time, so exactly the free slots are handed out; a separate check-then-insert let
        /// them all through. A finished job frees its slot.
        /// </summary>
        [Fact]
        public async Task TryReserve_ConcurrentRequests_NeverExceedTheLimit()
        {
            Guid datasetId = await CreateDatasetAsync();

            Guid?[] reservations = await Task.WhenAll(Enumerable.Range(0, 25)
                .Select(i => Task.Run(() => ReserveAsync(new IngestionJob(datasetId, $"/staged/{i}.csv")))));

            List<Guid> granted = reservations.Where(r => r is not null).Select(r => r!.Value).ToList();
            granted.Should().HaveCount(QueueLimit);
            (await ReserveAsync(new IngestionJob(datasetId, "/staged/extra.csv"))).Should().BeNull("the queue is full");

            using (IServiceScope scope = _factory.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IIngestionJobQueue>().ReleaseAsync(granted[0], CancellationToken.None);
            }
            (await ReserveAsync(new IngestionJob(datasetId, "/staged/extra.csv"))).Should().NotBeNull("a released slot is free again");
        }

        /// <summary>A reservation is never run until it is committed, and commit makes it claimable.</summary>
        [Fact]
        public async Task Reservation_IsClaimedOnlyAfterCommit()
        {
            Guid datasetId = await CreateDatasetAsync();
            Guid reservation = (await ReserveAsync(new IngestionJob(datasetId, "/staged/pending.csv")))!.Value;

            (await ClaimAsync()).Should().BeNull("the upload is still being written");

            using (IServiceScope scope = _factory.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IIngestionJobQueue>().CommitAsync(reservation, CancellationToken.None);
            }
            (await ClaimAsync())!.Id.Should().Be(reservation);
        }

        /// <summary>
        /// Codex review on #19: deleting a dataset whose upload is still queued deletes the staged
        /// file. The cascade used to remove the only record of the path, leaving the file forever.
        /// </summary>
        [Fact]
        public async Task DeleteDataset_WithQueuedUpload_DeletesTheStagedFile()
        {
            using HttpClient client = _factory.CreateClient();
            await client.PostAsJsonAsync("/api/auth/register",
                new DatasetStudio.DTO.Auth.RegisterRequest("queue-delete-user", "queue-delete-user@example.com", "password123"));
            HttpResponseMessage created = await client.PostAsJsonAsync("/api/datasets", new CreateDatasetRequest("queued-delete", null));
            DatasetDetailDto dataset = (await created.Content.ReadFromJsonAsync<DatasetDetailDto>())!;

            using MultipartFormDataContent form = new();
            form.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("title\na\n")), "file", "rows.csv");
            HttpResponseMessage upload = await client.PostAsync($"/api/datasets/{dataset.Id}/upload", form);
            upload.StatusCode.Should().Be(System.Net.HttpStatusCode.Accepted);
            Directory.EnumerateFiles(_factory.UploadPath).Should().ContainSingle("the upload is staged and waiting for a worker");

            HttpResponseMessage deleted = await client.DeleteAsync($"/api/datasets/{dataset.Id}");

            deleted.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
            Directory.EnumerateFiles(_factory.UploadPath).Should().BeEmpty("the staged upload must go with its dataset");
            (await ClaimAsync()).Should().BeNull();
        }

        /// <summary>
        /// Codex review on #19: requests are accepted while startup recovery runs, so a reservation
        /// made by this process (an upload being written right now) must survive recovery.
        /// </summary>
        [Fact]
        public async Task Recovery_KeepsReservationsMadeAfterThisProcessStarted()
        {
            IngestionBackgroundService host = CreateWorkerHost();
            Guid datasetId = await CreateDatasetAsync();
            Directory.CreateDirectory(_factory.UploadPath);
            string inProgress = Path.Combine(_factory.UploadPath, "in-progress.csv");
            await File.WriteAllTextAsync(inProgress, "title\n");
            Guid reservation = (await ReserveAsync(new IngestionJob(datasetId, inProgress)))!.Value;

            await host.RecoverInterruptedJobsAsync(CancellationToken.None);

            File.Exists(inProgress).Should().BeTrue();
            using IServiceScope scope = _factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IIngestionJobQueue>().CommitAsync(reservation, CancellationToken.None);
            (await ClaimAsync())!.Id.Should().Be(reservation);
        }

        /// <summary>Committing a reservation that no longer exists fails instead of pretending the import was queued.</summary>
        [Fact]
        public async Task Commit_OfAVanishedReservation_Throws()
        {
            Guid datasetId = await CreateDatasetAsync();
            Guid reservation = (await ReserveAsync(new IngestionJob(datasetId, "/staged/gone.csv")))!.Value;
            using IServiceScope scope = _factory.Services.CreateScope();
            IIngestionJobQueue queue = scope.ServiceProvider.GetRequiredService<IIngestionJobQueue>();
            await queue.ReleaseAsync(reservation, CancellationToken.None);

            Func<Task> act = () => queue.CommitAsync(reservation, CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        /// <summary>A reservation left by an upload interrupted mid-write is removed at startup, with its partial file.</summary>
        [Fact]
        public async Task Recovery_RemovesAbandonedReservationsAndTheirFiles()
        {
            Guid datasetId = await CreateDatasetAsync();
            Directory.CreateDirectory(_factory.UploadPath);
            string partial = Path.Combine(_factory.UploadPath, "partial.csv");
            await File.WriteAllTextAsync(partial, "title\n");
            Guid reservation = (await ReserveAsync(new IngestionJob(datasetId, partial)))!.Value;

            await CreateWorkerHost().RecoverInterruptedJobsAsync(CancellationToken.None);

            File.Exists(partial).Should().BeFalse();
            using IServiceScope scope = _factory.Services.CreateScope();
            (await scope.ServiceProvider.GetRequiredService<DatasetStudioDbContext>().IngestionJobs.AnyAsync(j => j.Id == reservation))
                .Should().BeFalse();
        }

        /// <summary>Deleting a dataset deletes its jobs, so a worker never runs work for a dataset that is gone.</summary>
        [Fact]
        public async Task DeletingDataset_DeletesItsJobs()
        {
            Guid datasetId = await CreateDatasetAsync();
            await EnqueueAsync(new IngestionJob(datasetId, "/staged/gone.csv"));

            using (IServiceScope scope = _factory.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IDatasetRepository>().DeleteAsync(datasetId);
            }

            (await ClaimAsync()).Should().BeNull();
        }
    }
}
