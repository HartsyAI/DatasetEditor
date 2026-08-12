# Phase 2 verification — Streaming ingestion rewrite

Plan: `~/.claude/plans/fuzzy-soaring-garden.md`, Phase 2.

## Automated tests

`dotnet test tests/APIBackend.Tests/APIBackend.Tests.csproj`

```
Passed!  - Failed: 0, Passed: 31, Skipped: 0, Total: 31, Duration: 17 s
```

31/31 green (up from 28 at the end of Phase 1), including 3 new tests in
`StreamingIngestionTests.cs` written specifically to distinguish real streaming from a
chunker that just re-slices an already-fully-parsed list (an audit from the advisor caught
that the plan's original "max in-flight buffer ≤ 2000" test would pass either way, since the
chunker's own buffer is bounded by construction regardless of what the parser underneath it
does):

- `ParseCsvAsync_YieldsItemsBeforeConsumingTheWholeStream` — feeds a 5,000-row CSV through
  `ParseCsvAsync` directly (made `internal` + `InternalsVisibleTo` for this), stops after the
  first 2,000 yielded items, and asserts the source `MemoryStream`'s `Position` is still less
  than its `Length` — proof the parser hasn't read the rest of the file it hasn't yielded yet.
- `StartIngestionAsync_LargeCsv_WritesInMultipleBoundedChunksNotOneGiantInsert` — drives the
  real public entry point with a fake `IDatasetItemRepository` recording each
  `InsertItemsAsync` call's batch size; asserts exactly `[2000, 2000, 1000]` for a 5,000-row
  file (the old code would have produced a single `[5000]` call).
- `StartIngestionAsync_EmptyCsv_FailsInsteadOfSilentlyCompleting` — the old `items.Count == 0`
  guard has no direct streaming equivalent (there's no materialized list to check the count
  of); this proves the re-derived "zero items written after draining the stream" check still
  catches an empty file and reports `Failed`, not a silent 0-item `Completed`.

All Phase 0/1 regression tests (route enumeration, Parquet append, HF streaming, local-folder
ingestion including its two-import cumulative-count check, path-traversal/symlink security)
still pass unchanged.

## Design decisions made mid-phase (advisor-reviewed before implementation)

Before writing any code, the streaming design was reviewed by the advisor, which surfaced
three things the plan's text didn't spell out and that would have caused real problems if
found only after implementation:

1. **`ParseZipAsync` can't `yield return` inside its existing `try/catch`** — C# forbids that
   (CS1626). Restructured as `try/finally` (legal around `yield return`) with a
   `completedSuccessfully` flag: cleanup only runs if the loop didn't finish normally, whether
   because of an exception or because the caller stopped enumerating early (e.g.
   cancellation). Verified this compiles and behaves correctly via the real ZIP-import test
   below.
2. **Partial-write failure policy, made explicit.** Chunked writing means a parse failure
   partway through a large file can leave earlier chunks already committed to Parquet — unlike
   the old all-or-nothing behavior. Decided (and commented in code) to leave partial data in
   place rather than roll back: `DeleteByDatasetAsync`-style rollback would also destroy items
   from a *prior* successful ingestion into the same dataset (the exact case Phase 1's
   cumulative-count fix exists for), so a partial-but-real dataset plus an honest
   `"Failed after writing N item(s): ..."` error message is more useful than silently losing
   data. Applied identically to `IngestAsync` and `IngestFromFolderAsync`.
3. **The empty-input guard has no streaming equivalent** — `if (items.Count == 0) throw` was
   rewritten to check the drain loop's running total after it finishes, not before it starts;
   covered by the `StartIngestionAsync_EmptyCsv_...` test above so a regression here would be
   caught immediately (an empty file silently reporting `Completed` would otherwise be an easy
   mistake to reintroduce).

Also folded in while touching this file: `IngestFromFolderAsync`'s `Directory.GetFiles(...)`
`.ToList()` had the identical "materialize everything first" shape as the five file parsers —
converted to `Directory.EnumerateFiles` + a streaming `EnumerateFolderImagesAsync` iterator so
folder ingestion gets the same chunked-write treatment, not just file uploads. `ParseZipAsync`
now also only extracts ZIP entries that match a supported image extension (previously
extracted every entry to disk, including non-images, and filtered afterward) — one pass, no
dead files left in managed storage for archive members that were never going to become items.

## Real run

`./start.sh` (Postgres via existing container + `dotnet run` for API and client) → driven with
real HTTP calls against the live API for the ingestion checks, then headless Playwright against
the real running client for the visual checks.

### Chunked CSV — 12,000 rows, twice (cumulative count through the new path)

Uploaded a 12,000-row CSV; completed in ~1.6s wall-clock (`createdAt` → `updatedAt`), status
`Completed`, `totalItems: 12000`. Uploaded the *same* file into the same dataset a second time
to force the append path through 6 write-chunks each (12,000 / 2,000): `totalItems: 24000`,
confirming the Phase 1 cumulative-count fix still holds under chunked writes, not just the old
single-batch write. `02-library-after-phase2-imports.png` shows the final card:
"phase2-large-csv-test · 24000 items · Completed".

### ZIP — 6 real generated JPEGs + 1 non-image file, real extraction

Built a ZIP with 6 small real JPEGs (`images/photo-N.jpg`) plus a `readme.txt` at the root.
Imported via the real upload endpoint: `status: Completed, totalItems: 6, errorMessage: null`.
Confirmed `readme.txt` was never extracted to disk (`find` on the managed storage folder shows
only the 6 `.jpg` files) — proof the new "only extract entries that match a supported image
extension" behavior works, not just that item-counting matches. Fetched
`GET /api/datasets/{id}/files/images/photo-0.jpg` directly: `HTTP 200`, real JPEG bytes.
`03-zip-imported-real-images.png`: all 6 images render with correct, distinct colors in the
real grid viewer, "Ingestion status: Completed", "6 / 6 items", zero console errors.

### JSON — 3,000-item array via `DeserializeAsyncEnumerable`, plus the non-array-root negative case

A 3,000-object JSON array imported cleanly: `status: Completed, totalItems: 3000`. A second
file with a non-array root (`{"not": "an array"}`) was imported into a separate dataset to
prove the "clear error, not silent full-buffering" requirement from the plan actually holds:
`status: Failed`, `errorMessage: "The JSON value could not be converted to
System.Collections.Generic.List\`1[System.Text.Json.JsonElement]. Path: $ ..."` — a real,
immediate `JsonException` surfaced through `IngestAsync`'s catch block, not a hang or a silent
0-item success. Visible in the library screenshot as "phase2-json-bad-root-test · 0 items ·
Failed" alongside the correctly-completed 3,000-item run.

### JSONL — 2,500 lines plus a trailing blank line

Imported cleanly: `status: Completed, totalItems: 2500` — confirms the `EndOfStream`→
`ReadLineAsync(ct) is not null` rewrite (which also incidentally clears a pre-existing CA2024
build warning) still correctly skips blank lines rather than crashing on them.

`01-dashboard.png` / `02-library-after-phase2-imports.png`: baseline smoke — dashboard and
library both load without console errors, library shows every dataset from this phase's real
runs with correct per-format status (4 Completed, 1 Failed-as-expected).

## Bugs found and fixed this phase (beyond the plan's literal checklist)

1. `ParseZipAsync`'s `yield return` inside `try/catch` (CS1626) — caught by the advisor review
   before writing any code, not by a failed build. See design decision #1 above.
2. `IngestFromFolderAsync`'s `Directory.GetFiles(...).ToList()` — same full-materialization
   pattern as the five parsers Phase 2's plan text named explicitly, in the same file, fixed
   alongside them rather than left for a later phase to rediscover.
3. `ParseZipAsync` extracting every archive entry (including non-images) before filtering —
   now only extracts entries it's actually going to catalogue.

## What's still known-broken (expected, later phases)

- No real thumbnails, no S3, no auth — later phases, as planned.
- The large-scale (multi-thousand-image, real bandwidth) streaming/virtualization gate is
  deliberately deferred to Phase 4 per the plan, once thumbnails exist to bound per-image
  transfer size.
