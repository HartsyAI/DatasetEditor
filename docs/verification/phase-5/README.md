# Phase 5 — S3 / S3-compatible storage: verification

## What was built

- `AWSSDK.S3`, configured via `AmazonS3Config.ServiceURL` + `ForcePathStyle = true` so real AWS
  S3 and S3-compatible endpoints (MinIO, R2, Backblaze) work identically.
- **Storage profiles**, not per-dataset credentials: a `Storage:S3StorageProfiles` config array
  (same `GetSection(...).Get<T[]>()` idiom already used for `Cors:AllowedOrigins`), each with a
  name, endpoint, credentials, and an explicit `AllowedBuckets` list. A dataset row stores
  `S3StorageProfileName` + `S3Bucket` + `S3Prefix` — never credentials. Real credentials come
  from environment variables (`Storage__S3StorageProfiles__0__AccessKey`, etc.) at deploy time,
  matching `ConnectionStrings`/`ADMIN_PASSWORD`.
- **Live enumeration, no bulk ingestion** — `POST /api/datasets/{id}/import-s3` validates the
  profile/bucket (including one cheap connectivity check) and records the pointer; `GetDatasetItems`
  gets a new branch that lists bucket objects live, on every page request, mirroring the
  HuggingFace-streaming branch's design exactly. The S3 continuation token is passed through as
  the page cursor as-is (never parsed as a number, unlike HF's numeric offset).
- **Proxy-streaming, not presigned URLs** — a new `GET /api/datasets/{id}/s3-file?key=&w=`
  endpoint (mirroring `ServeDatasetFile`'s shape) streams bucket objects through the API, keeping
  Phase 3's permission checks and Phase 4's thumbnail cache as the single enforcement point for
  S3-sourced content too. `S3KeySafety.IsKeyWithinPrefix` validates every key against the
  dataset's own stored prefix before any `GetObjectAsync` call — the S3 equivalent of
  `ServeDatasetFile`'s local-path containment check, including the same sibling-prefix
  false-positive guard Phase 1 hardened for local paths.
- **Client**: a genuinely new 4th source card + tab panel (`CreateDataset.razor`/
  `DatasetUploader.razor`) — S3 had zero create-flow presence before this phase, only a dead
  filter-dropdown label in `DatasetLibrary.razor`.
- **Migration**: `AddS3StorageFields` — the first incremental EF migration this repo has ever
  had (everything before Phase 5 was folded into the single `InitialCreate`). Three nullable
  `AddColumn` calls, nothing else.
- `docker-compose.yml` gained a dev-only `minio` service (not part of a future production
  compose file — operators bring their own S3-compatible endpoint in production).

## Automated tests

`dotnet test tests/APIBackend.Tests` — **104/104 passing**, including:
- `S3KeySafetyTests` (9): prefix containment, the sibling-prefix false positive class, path
  traversal, null/empty prefix, exact-match edge case.
- `S3StorageProfileResolverTests` (6): profile lookup (case-insensitive), unknown profile, and
  the core security property — a bucket not on a profile's `AllowedBuckets` list is rejected
  even though the profile itself resolves fine.
- `S3EndpointsTests` (10), against a **real MinIO container** (`Testcontainers.Minio`), not a
  mock: `import-s3` connects and persists metadata; unknown profile and disallowed-bucket both
  400; live bucket listing skips non-image keys; pagination via continuation token (not numeric
  offset) actually returns different objects per page; `s3-file` serves original bytes
  unmodified and a real resized JPEG via `?w=`; a key outside the dataset's prefix 404s even
  though it's in the same bucket; a private dataset 404s (not 403) for a non-owner, tying
  Phase 3's existence-leak fix to this new endpoint; a non-existent key 404s.

## Bugs found and fixed during verification (all before the real-run gate, via smoke-testing the actual API early)

1. **AWS SDK v4 / MinIO checksum incompatibility.** The very first live MinIO call (`PutBucketAsync`)
   failed with `"The provided 'x-amz-content-sha256' header does not match what was computed"`.
   AWS SDK v4 defaults to always computing/sending flexible-checksum headers; MinIO's SigV4
   implementation rejects them. Fixed by setting `RequestChecksumCalculation`/
   `ResponseChecksumValidation` to `WHEN_REQUIRED` (restores v3-era behavior) in
   `S3StorageService`. Found by smoke-testing against a real MinIO container *before* writing
   any test on top of the client, per this session's established discipline — would otherwise
   have silently broken every real MinIO deployment, not just tests.
2. **`response.S3Objects` can be `null` for a zero-match `ListObjectsV2` listing.** A plain
   `.Select()` on it threw `ArgumentNullException`, surfacing as a confusing "Could not reach
   bucket" 400 on `import-s3` for any brand-new prefix with zero objects yet — exactly the
   common first-connect case. Found via the integration test suite, fixed with a null-coalesce
   in `S3StorageService.ListObjectsAsync`.
3. **Test-infrastructure-only: a second, concurrent Postgres container.** Adding a separate
   xUnit collection for the MinIO fixture meant a *second*, fully independent Postgres container
   ran concurrently with the existing one (xUnit parallelizes collections by default), pushing
   concurrent EF-migration load past a real Npgsql/Postgres concurrency ceiling — intermittent
   `duplicate key value violates unique constraint "pg_type_typname_nsp_index"` and `relation
   "__EFMigrationsHistory" already exists` errors on the *full* suite (individual filtered runs
   never showed it). Fixed by folding `MinioContainerFixture` into the existing `PostgresCollection`
   instead of a new one. Verified with two consecutive full 104-test runs, both clean.

## Real-run verification

Driven with Playwright (Chromium) against `./start.sh` + `docker compose up -d minio`, with a
real `Storage:S3StorageProfiles` profile (`minio-dev`, `AllowedBuckets: [dataset-images]`)
configured via env vars, and a real bucket seeded with 6 genuine JPEGs plus one non-image
`manifest.json` (via a standalone script using the same AWS SDK client code path).

- **Create flow**: the new 4th "S3 bucket" card and tab render correctly alongside the existing
  three (`01-create-page-four-cards.png`). Filled Storage profile / Bucket / Prefix / Dataset
  Name (`02-s3-form-filled.png`) and clicked Connect Bucket — real `POST /api/datasets` (201) →
  real `POST .../import-s3` (202) → navigated to the viewer.
- **Real image rendering**: all 6 real images rendered in the grid via 6 `GET .../s3-file?key=...&w=400`
  requests, all `200`, all `image/jpeg`, ~2.9-3.7KB each (vs. ~6.4-8.2KB originals — real
  resize, not passthrough). `manifest.json` correctly excluded (6 items shown, not 7).
  Screenshot: `03-s3-viewer-grid-real-images.png`.
- **Bucket allow-list enforcement, live**: attempted to connect a bucket not on the profile's
  `AllowedBuckets` list through the actual UI — correctly rejected with a `400` and a visible
  error toast ("Upload failed: S3 import request failed."). Screenshot:
  `04-bucket-not-allowed-rejected.png`.

## Bug found during verification, NOT fixed (pre-existing, out of Phase 5's scope)

**Blazor's default unhandled-exception banner (`#blazor-error-ui`, "An unhandled error has
occurred. Reload") appears alongside a correctly-handled import failure.** Visible in
`04-bucket-not-allowed-rejected.png`'s bottom-left corner, alongside the correct red error toast.
Confirmed this is **pre-existing and NOT introduced by Phase 5**: the identical banner reproduces
via the untouched, Phase-1-built local-folder-import failure path (tested directly — a
nonexistent folder path triggers the same banner). The actual error handling underneath it works
correctly in both cases (correct HTTP status, correct caught exception, correct logged message,
correct toast) — this is a separate, cosmetic-but-real issue in whatever triggers Blazor's
top-level unhandled-exception UI on *any* import failure path (upload, HuggingFace, folder, and
now S3 alike), not specific to the S3 code this phase added. No `pageerror`/console stack trace
beyond the correctly-caught-and-logged one was observable via Playwright, so root-causing it
further would need in-browser WASM debugging tools this environment doesn't have easy access to.
Flagging as a real, reproducible finding for a future session rather than leaving it
undocumented or claiming it was fixed.

## Design deviations from the plan's literal text (deliberate, evidence-driven)

- **S3-sourced items are enumerated live, not eagerly registered into Parquet.** The plan named
  both options; live enumeration was chosen to mirror the already-correct HuggingFace-streaming
  design exactly, and because it needs no new `IngestionJob` variant (no bulk work happens at
  connect time, same as HF streaming's own no-op ingestion step).
- **`s3-file` is keyed by S3 object key, not a fabricated offset.** Unlike HF-streaming rows
  (which have no stable identity at all, forcing `hf-thumbnail`'s row-offset scheme), an S3
  object key already is a stable, natural identity — using it directly is simpler than adding an
  artificial identifier, and lets `ImageUrl`/`ThumbnailUrl` be identical (matching the
  folder/ZIP ingestion convention) instead of needing HF's split design.
- **Import gated by `CanWriteAsync`, not Admin-only.** Considered and rejected mirroring
  `ImportFromFolder`'s blanket Admin-only rule; an S3 import can only ever reach a bucket
  explicitly present on the named profile's `AllowedBuckets` list, so it doesn't carry
  `ImportFromFolder`'s "arbitrary server filesystem" risk — the allow-list is the actual
  security boundary, not the caller's role.

## Result

Phase 5 real-run gate: **passed**, including three bugs found and fixed (two real S3-client
bugs that would have affected any real MinIO/S3-compatible deployment, one test-infrastructure
concurrency issue) and one pre-existing, out-of-scope bug found and honestly documented rather
than silently left unmentioned. 104/104 automated tests green, reliable across repeated runs.
Screenshots in this directory.
