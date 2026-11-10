# Parquet item storage

Dataset items for **local** datasets (CSV/TSV/JSON/JSONL/Parquet/ZIP/image-folder imports) live
in Apache Parquet files on disk. Dataset metadata, users and permissions live in PostgreSQL; this
directory only handles items. HuggingFace and S3 datasets are streamed and never written here.

## Files

Each dataset is a manifest over immutable segment files, each with an id index:

```
{Storage:ParquetPath}/dataset_{datasetId:N}.manifest.json             the live segments, in row order
{Storage:ParquetPath}/dataset_{datasetId:N}_seg_{n:D8}.parquet        a segment
{Storage:ParquetPath}/dataset_{datasetId:N}_seg_{n:D8}.parquet.ids    its id index
```

The manifest is the only record of which files are live. Anything else with the dataset's prefix
(a segment from a write that died before its commit, files replaced by a merge, `*.tmp`) is an
orphan and is deleted on the dataset's next write. Rows are written in row groups of
`ParquetSchemaDefinition.DefaultBatchSize` (10,000). The schema is defined once in
`ParquetSchemaDefinition.Schema` and is the single source of truth for column names and types.

Datasets written before manifests (`dataset_{id}_shard_{n:D6}.parquet`, appended in place) are
adopted on first access: their shards become segments in shard order and a manifest is saved;
no data is rewritten. Their id indexes are built the first time an item is looked up.

## Components

| File | Responsibility |
|---|---|
| `ParquetSchemaDefinition.cs` | Schema, writer/reader options, legacy shard file names. |
| `ParquetManifest.cs`, `ParquetSegment.cs` | The manifest record. |
| `ParquetManifestStore.cs` | Loads, caches and atomically replaces manifests; adopts legacy shards; sweeps orphans. |
| `SegmentIdIndex.cs` | Sorted id → row index per segment; binary search. |
| `ParquetItemWriter.cs` | Writes a segment through a temp file and rename. |
| `ParquetItemReader.cs` | Pages by cursor, single items, counts, whole segments, one row group at a time. |
| `ParquetItemRepository.cs` | `IDatasetItemRepository` used by the API: adds, pages, edits, deletes, statistics, merges. |

## Behaviour

- **Writes are crash-safe.** A segment is written whole to a temp file and renamed into place;
  a write is committed only when the manifest is replaced (temp file plus rename). A process
  killed at any point leaves the previous, complete dataset plus orphans. Appending in place used
  to rewrite the shard footer, and a kill mid-append made the whole shard unreadable (P-11).
- **Appends.** Each `AddRangeAsync` writes one new segment. The item count is the manifest's row
  total (P-7).
- **Merging.** When the last eight or more segments are each under 250,000 rows, they are merged
  into one (streamed a row group at a time) and committed by a manifest swap (P-5).
- **Paging.** The cursor is `r{offset}`, an absolute row offset, so it survives merges; the older
  `"shard:row"` form is still accepted. A page skips every row group that ends before the cursor
  using footer row counts, then decodes groups until the page is full, and returns no cursor when
  it reaches the last row.
- **Item lookup.** `GetItemAsync(datasetId, itemId)` binary-searches each segment's id index and
  decodes only the row group holding the row. Without a dataset id, each dataset is searched the
  same way (P-2). Edit requests carry the dataset id.
- **Edits.** `EditItemAsync`/`EditItemsAsync` apply a change to each item as it stands, under the
  dataset's lock, and rewrite only the segments holding an edited item, streaming one row group at
  a time into a temp file renamed over the segment (P-3, P-4). Rows keep their positions, so the
  id index stays valid. `EditItemAsync` checks the caller's precondition (`If-Match`) in the same
  lock (E-4). Each edit stamps a new `UpdatedAt`, at least a millisecond after the previous one,
  whose ticks are the item's `Version`. `UpdateItemsAsync` (whole-item replacement) uses the same
  path.
- **Locking.** All operations on a dataset are serialised with a `SemaphoreSlim` keyed by dataset
  id; other datasets are unaffected.
- **Compression.** Snappy, set on every file written.
- **Columns.** A page decodes every column of the groups it reads; there is no column projection.

## Adding or changing a column

1. Change `ParquetSchemaDefinition.Schema` and the mapping in `ParquetItemReader` /
   `ParquetItemWriter`.
2. Existing segment files are not migrated. Re-import the dataset, or write a one-off conversion.
3. Add a round-trip test to `tests/APIBackend.Tests/ParquetItemRepositoryTests.cs`.

## Where the design is described

- [docs/architecture.md](../../../../docs/architecture.md) §3–§4: data model and paging contract.
- [docs/refactor-audit.md](../../../../docs/refactor-audit.md) §7 (P-1…P-11, G-1…G-3): known
  defects and the storage plan.
