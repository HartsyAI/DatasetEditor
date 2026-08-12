# Phase 1 verification — Data-path correctness & durability

Plan: `~/.claude/plans/fuzzy-soaring-garden.md`, Phase 1.

## Automated tests

`dotnet test tests/APIBackend.Tests/APIBackend.Tests.csproj`

```
Passed!  - Failed: 0, Passed: 28, Skipped: 0, Total: 28, Duration: 10 s
```

28/28 green (up from 25 at the end of Phase 0), including 5 new/changed regression tests for this phase:
- `AddRangeAsync_CalledTwice_AppendsToExistingShardWithoutCorruption` / `AddRangeAsync_ExceedingBatchSize_WritesAllItemsAcrossMultipleAppends` — Parquet append-path fix (confirmed to fail with the exact predicted `IOException` when temporarily reverted).
- `ImportFromHuggingFace_StreamingMode_CompletesWithoutObjectDisposedException` — HF-import scope-capture fix.
- `NewRepositoryInstance_OverExistingData_ReportsRealCountNotZero` — item-count-reset-on-restart fix (found while fixing the deadlock risk).
- `GetPageAsync_WithSparseFilter_PagesWithoutDuplicatesOrGaps` — filter-cursor undercounting fix (confirmed to fail without the fix).
- `ServeDatasetFileSecurityTests` — symlink-following fix (confirmed to fail without it).
- `ImportFromFolderTests` (2 tests) — local-folder ingestion end-to-end, including the cumulative-item-count fix.

## Real run

`docker compose up -d` → `dotnet run` (API + client) → driven with headless Playwright, plus direct API calls for the larger-scale checks.

### HuggingFace streaming import — `ylecun/mnist` (real, live, 70,000 rows)

Created a dataset, imported `ylecun/mnist` in streaming mode, browsed it in the real viewer. `01-mnist-streaming-viewer.png`: real, correctly-labeled MNIST digits (5, 0, 4, 1, 9, 2, 1, 3...) rendering in the virtualized grid, "Ingestion status: Completed", "Total items: 70000", "500 / 70,000" buffered.

This surfaced and fixed two bugs the unit tests couldn't have caught (they need real HuggingFace response shapes):
- `ImportFromHuggingFace` never persisted `HuggingFaceRepository`/`Config`/`Split` onto the dataset (the comment claiming "already saved by the endpoint" was wrong) — every streaming items request failed with "missing repository metadata". Masked until Phase 1's `AsTracking` fix made `UpdateAsync` calls actually persist at all.
- The image-URL detection heuristic only recognized columns literally named `image_url`/`img_url`/`url`, and its extension-sniffing fallback used a plain `EndsWith(".jpg")` check — real HuggingFace datasets-server URLs are `.../image.jpg?Expires=...&Signature=...`, so neither path ever matched. Fixed to recognize HF's actual `image` column and to check the URL's path portion, not its raw tail.

### Local folder import — real UI, real images

Drove the actual Create Dataset → Local Folder form end-to-end in the browser (`02-local-folder-form.png`, `03-local-folder-imported.png`): 5 real generated JPEGs imported and rendering with correct colors/titles, "Dataset 'Real Local Photos' imported successfully with 5 items!", navigated straight into the viewer.

This surfaced a much bigger, pre-existing bug while testing it: **`@bind-Value` was mangled to `@@@bind-Value` (and `@bind-ActivePanelIndex`/`@bind-Open` to `@@bind-...`) in 23 places across 8 files** — every field on the HuggingFace import form, the new local-folder form, dataset library search/filters, image detail panel editing, the add-tag dialog, API key settings, and the main layout's nav drawer. The extra `@` silently broke two-way binding in ways that only showed up as symptoms (a card's "InitialTabIndex" not taking effect, an import button staying disabled after typing a value) — not build errors, not console errors. Fixed all 23 occurrences (`sed 's/@@@bind-/@bind-/g; s/@@bind-/@bind-/g'` across the affected files, then verified zero remain). This was already present in the pre-existing HuggingFace form before this session touched it — the new local-folder fields inherited it because they were written by pattern-matching the existing (broken) code.

### Append durability & cumulative counts at scale — real API, 24,000 items

Uploaded the same 12,000-row CSV into one dataset twice (`large_dataset.csv`, generated locally):
- First upload: multi-batch append **within one ingestion call** (12,000 > `DefaultBatchSize` 10,000, forcing 2 internal `WriteBatchAsync` calls) → `status: Completed, totalItems: 12000`.
- Second upload into the same dataset: append **across two separate ingestion calls** (the shard file already existed) → `status: Completed, totalItems: 24000`, confirmed against the real underlying Parquet data (`GET .../items?pageSize=50000` returned exactly 24,000 unique `externalId`s).

This surfaced one more bug: `UpdateItemCountAsync` was called with the current batch's item count, not the dataset's true cumulative total — the second upload's `totalItems` would have read back as `12000` again (this run's batch size) instead of `24000`. Fixed by querying the real count from the repository after insert, and added `IDatasetItemRepository.GetCountAsync` (the interface Phase 1.4 trimmed down) to support it.

`04-large-csv-24000-items.png`: viewer correctly shows "Total items: 24000" and "500 / 24,000 items" buffered; every card shows "Failed to load" — expected and correct, since this synthetic CSV's `image_url` values are unreachable `https://example.com/...` placeholders, and `ImageCard`'s error-state handling is doing exactly what it should.

## Bugs found and fixed this phase (beyond the plan's literal checklist)

1. Parquet append `FileMode.Append` → `FileMode.Open, FileAccess.ReadWrite` (planned).
2. HF-import `Task.Run` scope-capture → routed through `IngestionQueue` (planned); `IngestionJob` extended to carry an HF-request or a folder-path variant alongside the existing file-path one.
3. **`DatasetRepository.UpdateAsync`/`UpdateStatusAsync`/`UpdateItemCountAsync` were silent no-ops** — the DbContext's global `QueryTrackingBehavior.NoTracking` default meant every fetch-mutate-`SaveChangesAsync` pattern fetched an untracked entity, so the mutations were invisible to the change tracker. The entire ingestion-status feature (Pending → Processing → Completed/Failed) had never actually persisted. Fixed with `.AsTracking()` on the three mutating queries; `DeleteAsync` needed the same fix for a related identity-tracking conflict it exposed once tests started running for real.
4. `GetOrInitializeItemCount` sync-over-async `.Wait()` → `await` (planned) — while fixing it, found `InitializeItemCounts()` was eagerly seeding every existing dataset's count to `0` at startup "to be computed on first access," except that `0` *is* the cached value from then on, so item counts for pre-existing datasets read as 0 forever after every service restart, and a subsequent `AddRangeAsync` would compute new items' starting index from 0 too. Fixed by not pre-seeding the cache at all.
5. Dead/dangerous `ItemRepository` methods deleted (planned); item-repository interface trimmed to just `InsertItemsAsync`, later extended with `GetCountAsync` for fix #7.
6. `ServeDatasetFile` path-traversal hardening: trailing-separator guard + symlink-target resolution (planned); `ParseZipAsync` rewritten to extract entry-by-entry with an explicit containment check, **and** to extract into the dataset's managed storage folder instead of a temp directory that got deleted in a `finally` block after every ZIP import — ZIP-imported images could never have been served, regardless of URL scheme.
7. Local-folder ingestion wired up (planned) — also fixed `IngestFromFolderAsync`'s (and `ParseZipAsync`'s) `ImageUrl = file:///...` bug: a `file://` URI points at the *server's* filesystem, which a browser can never fetch. Added `DatasetDto.StoragePath` (already added in Phase 0) as the mechanism `ServeDatasetFile` uses to serve folder-ingested datasets straight from their original location, and a shared `DatasetStorageConventions` helper so the writer (ingestion) and reader (`ServeDatasetFile`) agree on the managed-storage naming scheme instead of `DatasetEndpoints.cs` owning it privately.
8. Filter-cursor undercounting fixed via a `RowsExamined` return value from `ReadFromShardAsync` (planned).
9. HF streaming metadata never persisted (found via live mnist test, see above).
10. HF image-URL detection heuristic too narrow for real datasets-server URLs (found via live mnist test, see above).
11. Systemic `@bind-Value`/`@bind-ActivePanelIndex`/`@bind-Open` mangling, 23 occurrences across 8 client files (found via live local-folder UI test, see above).
12. `UpdateItemCountAsync` called with the current batch's count instead of the true cumulative total (found via live large-CSV re-upload test, see above).

## What's still known-broken (expected, later phases)

- No real thumbnails, no S3, no auth — later phases, as planned.
- An "An unhandled error has occurred" banner appeared once, specifically when ~500 simultaneously-buffered cards all failed to load images from intentionally-fake `example.com` test URLs at once. Not reproduced with real image URLs (mnist, local folder) in this session — noted for a future look, not chased further here since it's outside this phase's scope and the negative-test condition that triggered it isn't representative of real usage.
