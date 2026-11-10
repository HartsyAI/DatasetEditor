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
3. `AdminBootstrapper` checks the admin account against
   `ADMIN_USERNAME`/`ADMIN_EMAIL`/`ADMIN_PASSWORD` **on every boot**, not just when the users
   table is empty. It replaces the password hash only when `ADMIN_PASSWORD` has changed, and then
   ends the admin's existing sessions. This is how you reset a forgotten admin password: change
   `ADMIN_PASSWORD` in `.env` and `docker compose ... up -d` again.
4. Caddy requests a certificate for `DOMAIN`. A real public domain gets automatic Let's Encrypt
   TLS with zero extra config. Left at the default `localhost` (or any non-public
   domain/IP), Caddy issues a locally-trusted self-signed certificate instead (`tls internal`
   is implicit for those addresses) — this is what the Phase 6 real-run verification exercised,
   without owning a domain.

## Accounts

Registration is **closed** by default outside Development: the admin account comes from `.env`,
and nobody else can sign up. To let people register, set `ALLOW_REGISTRATION=true` in `.env`
(passed to the API as `Auth__AllowRegistration`). Any signed-in user can upload large files and
start HuggingFace downloads, so open registration only on a server you mean to share.

Login is limited to 10 attempts per client IP per minute and registration to 20 per hour
(`Auth__LoginAttemptsPerMinute`, `Auth__RegistrationsPerHour`). Every request reaches the API
from Caddy, so the client IP is the one Caddy sends in `X-Forwarded-For`. The API believes that
header only from the addresses in `ReverseProxy__KnownProxies__*` / `ReverseProxy__KnownNetworks__*`
and from nowhere by default. The production compose file puts Caddy at a fixed address
(`CADDY_IP`, default `172.31.250.10`, in `PROXY_SUBNET`, default `172.31.250.0/24`) and names only
that address, so a client that reaches the API another way can't choose its own IP. If that range
is already used on the host, change both values in `.env`. An existing install created its compose
network before the subnet was fixed: run `docker compose -f docker-compose.yml -f docker-compose.prod.yml down`
once (volumes stay) before `up -d`.

Deactivating an account (`users.is_active = false`) or changing its role takes effect on its
open sessions within 30 seconds (`Auth__RevalidateEvery`).

## TLS / domain

Edit `DOMAIN` in `.env` to your real domain (must already point at this host) before first
boot, then `Cors__AllowedOrigins__0` in `docker-compose.prod.yml` (`https://${DOMAIN}`)
automatically follows it. Changing `DOMAIN` after Caddy has already issued a certificate for
the old value: `docker compose -f docker-compose.yml -f docker-compose.prod.yml restart caddy`.

## Logs

The API logs to the console (`docker compose logs api`). What it records:

- One line per `/api` request under the category `DatasetStudio.Requests`: method, route
  pattern, dataset id, status, time and the caller's account id (or `anonymous`). The route
  pattern is logged rather than the path, and the query string is left out, so file names and
  search terms don't reach the log. Image and thumbnail fetches are logged at Debug (a grid page
  makes about a hundred); 5xx answers at Error. Tune it with
  `Logging__LogLevel__DatasetStudio.Requests` (e.g. `Warning` to keep only failures).
- `[Audit]` lines for item edits: who, which dataset and items, which fields; never the values.
- Sign-ins, registrations and failed logins by account id; usernames and emails aren't logged.
- Imports refused because the queue is full, with the dataset and user.

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

## Database name

The production database is `dataset_studio_dev` by default, the name every existing install uses.
`POSTGRES_DB` in `.env` overrides it:

- **Existing installs:** leave `POSTGRES_DB` unset or set it to `dataset_studio_dev`. Nothing changes on upgrade.
- **Fresh installs:** you may pick another name before the first `up`. Postgres creates the database
  when it first initialises the data volume.
- **Do not change the name on an existing install.** The API runs EF Core migrations on startup, and
  EF creates a missing database before applying them. A changed name therefore starts an empty
  database and leaves the real data behind, with no error. To move to another name, dump the old
  database and restore it into the new one first.

## S3 storage (opt-in)

`Storage:S3StorageProfiles` defaults to an empty array — no S3 support is configured out of the
box. To add a profile, add `Storage__S3StorageProfiles__0__*` environment entries to the `api`
service in `docker-compose.prod.yml`. The keys are `Name`, `ServiceUrl` (blank for real AWS S3, set for
MinIO/R2/Backblaze), `AccessKey`, `SecretKey`, `ForcePathStyle`, and
`AllowedBuckets__0`/`__1`/... — the explicit allow-list a profile's credentials may actually
reach (see `S3StorageProfileConfig`'s own doc comment for why this isn't just "whatever bucket
the request names").

`ForcePathStyle` is optional. Left unset, a profile with a `ServiceUrl` uses path-style addressing
(what MinIO and most self-hosted endpoints need, and what every profile used before) and a real
AWS profile (no `ServiceUrl`) uses virtual-hosted style, which AWS requires for buckets that don't
accept path-style requests. Set `true` or `false` to override either default.

Every image an S3 dataset shows is proxied through the API with the profile's credentials, so a
public S3 dataset spends your egress. Two settings bound that:

- `Storage:S3MaxServeBytes` (default `67108864`, 64 MB): originals larger than this are not
  proxied (404, logged as a warning); their `?w=` thumbnails still are. `0` or less turns the cap off.
- `RateLimiting:S3FilePermitsPerMinute` (default `600`): requests to the S3 file route per caller
  per minute; see "Rate limits".

## Rate limits

Two routes have a fixed one-minute window per caller, where the caller is the signed-in user, or
the client IP address for anonymous requests. Over the limit the API answers `429` with
`Retry-After`.

| Setting | Default | Route |
|---|---|---|
| `RateLimiting:S3FilePermitsPerMinute` | 600 | `GET /api/datasets/{id}/s3-file` |
| `RateLimiting:HuggingFaceDiscoverPermitsPerMinute` | 10 | `POST /api/datasets/huggingface/discover` |

A grid page requests one thumbnail per visible card, so keep the S3 limit well above the number of
cards a user scrolls past in a minute. Behind Caddy (or any reverse proxy) the API sees the proxy's
address, so anonymous callers are told apart by the `X-Forwarded-For` address Caddy sends. The
API believes that header only from the proxies in `ReverseProxy__KnownProxies__*` /
`ReverseProxy__KnownNetworks__*`; the production compose file names Caddy's fixed address (see
"Accounts"). With nothing configured, anonymous callers share one budget.
Signed-in users always get their own budget.

## Server folder import (opt-in)

Admins can point a dataset at a folder already on the server ("Server folder" on the create
page). It is off until you list the folders it may read in `Storage:FolderImportRoots`: with the
default empty list, the import is refused with a 400 that says so. A folder is accepted when it is
one of the roots or inside one, compared by real path (a symlink inside a root that leads
elsewhere is refused). In Docker the roots are container paths, so mount the image folder and list
the mount point, for example in the `api` service of `docker-compose.prod.yml`:

```yaml
    volumes:
      - /srv/photos:/imports/photos:ro
    environment:
      Storage__FolderImportRoots__0: "/imports"
```

Don't list `/app/data` or anything above it: it holds the DataProtection key ring. Only image files
(`.jpg`, `.jpeg`, `.png`, `.webp`, `.gif`, `.bmp`) are ever served from an imported folder.

## Extensions (trust model)

The API loads extensions from two folders next to `/app`: `/Extensions/BuiltIn` (baked into the
image; only the CoreViewer reference extension, which does nothing) and `/Extensions/Community`
(empty unless you mount one). To install an extension, mount a folder there with one subfolder
per extension holding its `extension.manifest.json` and its `dotnet publish` output, for example
`- ./extensions:/Extensions/Community:ro` under the `api` service, and restart the API. Set
`Extensions__DisabledExtensions__0=<id>` to skip one without removing it.

**An extension is fully trusted code.** Any `{Id}.Api.dll` the API finds in those folders runs
inside the API process with the API's privileges: it can read and change the database, every
dataset file, the S3 credentials and the cookie-signing keys, and it can register or replace any
service. Nothing is sandboxed, assemblies are not signed or verified, and a manifest's
`requiredPermissions` (from older manifests) is ignored; it never restricted anything. Only
install extensions whose source you have read or whose author you trust as much as this API, and
keep the extensions folder writable by administrators only (mount it read-only as above).

Extension routes live under `/api/extensions/{id}` and require a signed-in user, but they get no
dataset permission check, so any signed-in user can call them.

## Known landmines (found during Phase 6's own real-run verification)

- **`Directory.Build.props` must be in the Docker build context.** It supplies
  `<ImplicitUsings>enable</ImplicitUsings>`, which most of this repo's source relies on instead
  of explicit `using System;`-style directives. Miss it and the build fails with every basic
  BCL type reported as unresolved — a genuinely confusing error that looks like a missing
  reference pack rather than a missing implicit-usings source file.
- **Don't split `dotnet restore` (with only `.csproj` files) from the full-source `publish`
  step** as a Docker layer-caching optimization -- confirmed live that doing so left the SDK in
  a state where the later publish failed with the same symptom as above, even with
  `Directory.Build.props` present. A single restore+publish after the full source
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
