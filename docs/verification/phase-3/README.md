# Phase 3 — Authentication & multi-user authorization: verification

## What was built

- Real accounts: `POST /api/auth/register`, `POST /api/auth/login`, `POST /api/auth/logout`,
  `GET /api/auth/me`, cookie-based sessions (`DatasetStudio.Auth`, HttpOnly, `SameSite=Lax`,
  7-day sliding expiration), BCrypt password hashing.
- Ownership/permission model: `CreatedByUserId` on datasets, `PermissionEntity` grants
  (access level + `CanDelete` + `ExpiresAt`), a site-admin bypass, and public/private
  visibility (`Dataset.IsPublic`).
- `IDatasetPermissionService` (`CanReadAsync`/`CanWriteAsync`/`CanDeleteAsync`) is the single
  enforcement point, used consistently by both `DatasetEndpoints.cs` and
  `ItemEditEndpoints.cs`.
- Anonymous read is limited to public datasets; every mutation route requires authentication;
  `POST /api/datasets/{id}/import-folder` additionally requires the `Admin` role (open
  registration would otherwise let any account use it as a filesystem-read proxy).
- Admin bootstrap from `ADMIN_USERNAME`/`ADMIN_EMAIL`/`ADMIN_PASSWORD` env vars, idempotent
  across restarts, and specifically repairs the migration-seeded admin row's placeholder
  password hash rather than assuming an empty `users` table means "first run."
- Client: `Login`/`Register` pages, `CookieAuthenticationStateProvider` (`/api/auth/me`-backed,
  since the auth cookie is HttpOnly and unreadable from WASM), `CascadingAuthenticationState` +
  `AuthorizeRouteView` route guards, nav-bar signed-in indicator.

## Automated tests

`dotnet test tests/APIBackend.Tests` — **57/57 passing**, including 14 new unit tests
(`AuthServiceTests.cs`: password hashing, admin bootstrap idempotency/repair, permission-service
owner/admin/grant/expiry matrix) and 12 new integration tests (`AuthEndpointsTests.cs`:
register→me, wrong-password 401, logout, duplicate-username, placeholder-hash-login-is-401-not-500,
create-dataset ownership, delete denied→granted-via-permission→allowed→expired-denied-again,
import-from-folder non-admin 403).

## Real-run verification

Driven with Playwright (Chromium) against `./start.sh` (API :5000, client :5002, Postgres via
Docker Compose) — real browser, real cookies, real HTTP, not a mocked harness.

### 1. Anonymous baseline
Dashboard and nav render correctly logged-out (`00-dashboard-anonymous.png`); `GET /api/auth/me`
correctly 401s; `GET /api/datasets` correctly 200s with only public datasets.

### 2. Register → auto-login → redirect
`01-register-page.png` → `02-register-filled.png` → submit → `03-register-after-submit.png`
(nav shows "Signed in as `<username>`", real `POST /api/auth/register` → `200`, real Set-Cookie,
immediate redirect to `/my-datasets`).

### 3. Auth-gated route + create dataset
`/datasets/create` is `[Authorize]`-gated and renders correctly for a signed-in user
(`04-create-dataset-page.png`). Created a dataset as User A via an authenticated POST (the
same cookie/fetch pipeline the UI itself uses); response correctly carries
`createdByUserId` = User A's id.

### 4. Cross-user visibility and ownership
- **Public dataset** (the create endpoint's current default — see "Scope decisions" below):
  visible to User B in the list, `GET` by id → `200`, unauthorized `DELETE` by User B → `403`.
- **Private dataset** (flipped via direct DB update — no "make private" endpoint exists yet,
  see below): correctly absent from both the anonymous and User B's dataset list; `GET` by id
  → `404`; `DELETE` by User B → **`404`** (see bug fix below, not `403`).
- **Admin**: bypasses ownership/visibility entirely — `GET` on the private dataset → `200`,
  `/api/auth/me` confirms `role: "Admin"` (`05-admin-logged-in.png`).

### 5. Admin bootstrap against a genuinely fresh volume
`docker compose down -v` (confirmed volume removal) → `./start.sh` with a **different**
`ADMIN_PASSWORD` than any prior run → full migration ran from scratch (every `CREATE TABLE`/
`CREATE INDEX`, including `users`/`permissions`) → bootstrap log:
`[Auth] Synced admin account 'admin' from ADMIN_* configuration` → login with the new password
→ `200` with the correct admin identity. Confirms the migration-seeded placeholder-hash row is
repaired on true first boot, not just on top of already-bootstrapped state.

## Bug found and fixed during verification

**Private-dataset existence leak via mutation endpoints.** The read endpoints
(`GetDataset`/`GetDatasetItems`/`ServeDatasetFile`) deliberately return `404` (not `403`) when
the caller can't read a dataset, so privacy means non-existence, matching GitHub's private-repo
behavior. The mutation endpoints (`DeleteDataset`, `UploadDatasetFile`,
`ImportFromHuggingFace`, `ItemEditEndpoints.UpdateItem`) checked only existence (`404`) then
write/delete permission (`403`) — skipping the read-visibility check in between. A non-owner
who has (or guesses) a private dataset's GUID could confirm it exists by getting `403` instead
of `404` on a delete/upload/import attempt, even though the same id 404s on `GET`.

Fixed by adding the same `CanReadAsync` gate (→ `404` if false) before the write/delete check in
all four handlers. Verified live: before the fix, User B's `DELETE` on User A's private dataset
returned `403`; after the fix and a clean rebuild+restart, the same request returns `404`,
consistent with `GET`. All 57 automated tests still pass after the fix.
(`src/APIBackend/Endpoints/DatasetEndpoints.cs`, `src/APIBackend/Endpoints/ItemEditEndpoints.cs`)

## Deliberate scope decisions (not bugs, not silently dropped)

- **New datasets default to public** (`DatasetDto.IsPublic = true`, a pre-existing default from
  before this phase). `CreateDatasetRequest` has no `IsPublic` field, and there is no "make
  private" endpoint or UI toggle — the integration tests and this verification both set
  privacy directly via the database. Building a create-time visibility choice (and a
  toggle/rename endpoint generally) is real, separate scope, not part of "add auth."
- **No grant-permission UI or endpoint.** `PermissionEntity` grants are fully modeled and
  enforced (`IDatasetPermissionService` reads them correctly, respecting `ExpiresAt` and
  `CanDelete`), and covered by both unit and integration tests, but there is no way to create a
  grant except inserting the row directly — a future "share this dataset with a user" feature.
- **`GetAllDatasets`/listing does not surface permission-only grants** — a user with a granted
  permission on someone else's private dataset can read/write it directly by id, but it won't
  appear in their `/api/datasets` listing (the visibility filter is `IsPublic || owner`, not
  `IsPublic || owner || has-a-grant`). Documented here rather than fixed silently, since closing
  it is a small but real behavior change (what "my datasets" means) better made as an explicit
  decision than a drive-by fix.

## Result

Phase 3 real-run gate: **passed**, including the one bug found and fixed along the way. All 57
automated tests green. Screenshots in this directory.
