# Parquet item storage

Dataset items for **local** datasets (CSV/TSV/JSON/JSONL/ZIP/image-folder imports) live in
Apache Parquet files on disk. Dataset metadata, users and permissions live in PostgreSQL; this
directory only handles items. HuggingFace and S3 datasets are streamed and never written here.

## Files

Each dataset is split into shards of up to `ParquetSchemaDefinition.ItemsPerShard` (10,000,000)
items:

```
{Storage:ParquetPath}/dataset_{datasetId:N}_shard_{index:D6}.parquet    (default ./data/parquet)
```

Shard index is `itemIndex / ItemsPerShard`. Rows are written in batches of
`ParquetSchemaDefinition.DefaultBatchSize` (10,000); each batch becomes one row group. The schema
is defined once in `ParquetSchemaDefinition.Schema` and is the single source of truth for column
names and types.

## Components

| File | Responsibility |
|---|---|
| `ParquetSchemaDefinition.cs` | Schema, shard sizing, shard file naming and parsing. |
| `ParquetItemWriter.cs` | Appends batches to a shard, creates shards, deletes shards. |
| `ParquetItemReader.cs` | Reads pages by cursor, single items, counts and whole shards. |
| `ParquetItemRepository.cs` | `IDatasetItemRepository` used by the API: adds, pages, edits, deletes, statistics. |

## Behaviour

- **Paging.** The cursor is an opaque `"shardIndex:rowIndex"` string. A page reads row groups
  sequentially from the cursor position until the page is full. Cost is bounded by row-group
  size, not by dataset size. Malformed cursors are not yet rejected (audit G-3) and an
  exact-fill last page returns a cursor past the end (P-10).
- **Ingestion.** Items are appended batch by batch. Each append re-opens the shard and rewrites its
  footer, so a shard written in many small appends ends up with many small row groups (P-5).
- **Edits.** `UpdateItemsAsync` rewrites only the shards that contain an updated item. Each such
  shard is read whole into memory, its items are replaced, and it is written back as one row group.
  The delete-then-write is not atomic: a crash between the two loses the shard (P-3, P-4).
- **Locking.** Writes are serialised per dataset with a `SemaphoreSlim` keyed by dataset id, so
  editing one dataset never blocks another.
- **Compression.** `ParquetSchemaDefinition.Compression` is declared as Snappy, but the writer
  option that would apply it is commented out (P-6), so files use the library's default codec.
- **Item lookup by id** (`GetItemAsync`) has no dataset hint and scans every local dataset's
  shards (P-2).

## Adding or changing a column

1. Change `ParquetSchemaDefinition.Schema` and the mapping in `ParquetItemReader` /
   `ParquetItemWriter`.
2. Existing shard files are not migrated. Re-import the dataset, or write a one-off conversion.
3. Add a round-trip test to `tests/APIBackend.Tests/ParquetItemRepositoryTests.cs`.

## Where the design is described

- [docs/architecture.md](../../../../docs/architecture.md) §3–§4: data model and paging contract.
- [docs/refactor-audit.md](../../../../docs/refactor-audit.md) §7 (P-1…P-10, G-1…G-3): known
  defects and the Phase 4 storage-scale plan.
