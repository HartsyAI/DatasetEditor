# Self-host deployment runbook

DatasetStudio ships as **one container** — the API self-hosts the published Blazor WASM client
(`UseBlazorFrameworkFiles`/`MapFallbackToFile` in `src/APIBackend/Configuration/Program.cs`), so
there's nothing to deploy separately for the frontend.

## Quick start (production)

```bash
cp .env.example .env
# edit .env: real POSTGRES_PASSWORD, ADMIN_PASSWORD, and DOMAIN if you have one
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
```

This starts three services: `postgres`, `api` (built from the repo's `Dockerfile`), and `caddy`
(reverse proxy, port 80/443 only — `postgres`/`api` are not published to the host in this
overlay, Caddy is the sole entry point). MinIO is **not** part of this stack; S3 storage (Phase
5) is opt-in — see "S3 storage" below.

## What happens on first boot

1. `api` waits for `postgres`'s healthcheck (`pg_isready`).
2. EF Core migrations apply automatically (`db.Database.Migrate()` in `Program.cs`, before the
   app starts accepting requests) — including the seeded placeholder-admin row from
   `InitialCreate`.
3. `AdminBootstrapper` re-syncs the `admin` user's password/email/role from
   `ADMIN_USERNAME`/`ADMIN_EMAIL`/`ADMIN_PASSWORD` **on every boot**, not just when the users
   table is empty — this is also how you reset a forgotten admin password: change
   `ADMIN_PASSWORD` in `.env` and `docker compose ... up -d` again.
4. Caddy requests a certificate for `DOMAIN`. A real public domain gets automatic Let's Encrypt
   TLS with zero extra config. Left at the default `localhost` (or any non-public
   domain/IP), Caddy issues a locally-trusted self-signed certificate instead (`tls internal`
   is implicit for those addresses) — this is what the Phase 6 real-run verification exercised,
   without owning a domain.

## TLS / domain

Edit `DOMAIN` in `.env` to your real domain (must already point at this host) before first
boot, then `Cors__AllowedOrigins__0` in `docker-compose.prod.yml` (`https://${DOMAIN}`)
automatically follows it. Changing `DOMAIN` after Caddy has already issued a certificate for
the old value: `docker compose -f docker-compose.yml -f docker-compose.prod.yml restart caddy`.

## Backups

Everything that matters lives in three places:

| What | Where | Volume |
|---|---|---|
| Postgres data (users, datasets metadata, permissions) | `/var/lib/postgresql/data` | `datasetstudio_pgdata` |
| Dataset items (Parquet), local-folder storage, DataProtection session keys | `/app/data` | `datasetstudio_api_data` |
| Thumbnail cache | `/app/blobs` | `datasetstudio_api_blobs` |
| In-flight upload staging | `/app/uploads` | `datasetstudio_api_uploads` |

`docker compose down` (no `-v`) stops containers but keeps all four volumes. Back them up with
your usual Docker volume backup approach (e.g. `docker run --rm -v datasetstudio_pgdata:/data
-v $PWD:/backup alpine tar czf /backup/pgdata.tar.gz -C /data .`, repeated per volume) — or, for
Postgres specifically, a logical `pg_dump` is usually preferable to a raw volume copy for
point-in-time restores.

**Losing `datasetstudio_api_data` invalidates every session cookie** (it holds the
DataProtection key ring `Program.cs` persists to `Storage:DataProtectionKeysPath`, default
`./data/keys`) — not data-destroying, but every logged-in user has to log back in.

## S3 storage (opt-in)

`Storage:S3StorageProfiles` defaults to an empty array — no S3 support is configured out of the
box. To add a profile, add `Storage__S3StorageProfiles__0__*` environment entries to the `api`
service in `docker-compose.prod.yml` (see the commented `S3_PROFILE_*` variables in
`.env.example` for the values you'll need): `Name`, `ServiceUrl` (blank for real AWS S3, set for
MinIO/R2/Backblaze), `AccessKey`, `SecretKey`, `ForcePathStyle`, and
`AllowedBuckets__0`/`__1`/... — the explicit allow-list a profile's credentials may actually
reach (see `S3StorageProfileConfig`'s own doc comment for why this isn't just "whatever bucket
the request names").

## Known landmines (found during Phase 6's own real-run verification)

- **`Directory.Build.props` must be in the Docker build context.** It supplies
  `<ImplicitUsings>enable</ImplicitUsings>`, which most of this repo's source relies on instead
  of explicit `using System;`-style directives. Miss it and the build fails with every basic
  BCL type reported as unresolved — a genuinely confusing error that looks like a missing
  net8.0/net10.0 multi-targeting reference pack (a real, much rarer possibility given this repo
  mixes TFMs) rather than a missing implicit-usings source file.
- **Don't split `dotnet restore` (with only `.csproj` files) from the full-source `publish`
  step** as a Docker layer-caching optimization -- confirmed live that doing so left the SDK in
  a state where the later publish failed on the net8.0 projects with the same symptom as above,
  even with `Directory.Build.props` present. A single restore+publish after the full source
  copy works; the cost is a full restore on every source change, not just dependency changes.
- **Docker named volumes mount root-owned**, regardless of what the image had baked in at that
  path via a build-time `chown`. The `api` container's entrypoint (`docker-entrypoint.sh`)
  fixes this up at container *start* (`chown` as root, then `gosu app` to drop to the non-root
  `app` user for the actual process) -- confirmed live: without it, first boot against fresh
  volumes crash-loops on `Access to the path '/app/data/parquet' is denied`.
- **The `mcr.microsoft.com/dotnet/aspnet` image does not run as its bundled non-root `app` user
  by default** -- a `docker exec ... whoami` against a container with no explicit `USER`
  confirmed `root`. `docker-entrypoint.sh` handles this (see above); a `docker exec` shell into
  a running container still defaults to root (no image-level `USER` directive is set, since it
  would break the entrypoint's own startup chown) -- use `docker exec -u app ...` for a
  non-root shell if you need one.
