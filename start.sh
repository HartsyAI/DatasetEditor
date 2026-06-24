#!/usr/bin/env bash
#
# DatasetStudio dev launcher.
#   - starts PostgreSQL via Docker Compose (and waits until it's ready)
#   - runs the API   on http://localhost:5000  (auto-applies EF migrations)
#   - runs the client on http://localhost:5002
#
# Usage:
#   ./start.sh              # start db + api + client, stream logs, Ctrl+C to stop
#   ./start.sh --stop-db    # on exit, also stop the Postgres container
#   ./start.sh --help
#
# On Ctrl+C the API and client are stopped. The database keeps running by default
# (so restarts are fast); pass --stop-db to also stop it, or run ./stop.sh later.

set -euo pipefail

cd "$(dirname "$0")"

API_URL="http://localhost:5000"
CLIENT_URL="http://localhost:5002"
STOP_DB=0
API_PID=""
CLIENT_PID=""

for arg in "$@"; do
  case "$arg" in
    --stop-db) STOP_DB=1 ;;
    -h|--help)
      awk 'NR==1{next} /^#/{sub(/^# ?/,""); print; next} {exit}' "$0"
      exit 0 ;;
    *) echo "Unknown option: $arg (try --help)"; exit 1 ;;
  esac
done

# ----- helpers -------------------------------------------------------------
info()  { printf '\033[1;34m[start]\033[0m %s\n' "$*"; }
warn()  { printf '\033[1;33m[start]\033[0m %s\n' "$*"; }
err()   { printf '\033[1;31m[start]\033[0m %s\n' "$*" >&2; }

# Pick `docker compose` (v2) or legacy `docker-compose`.
detect_compose() {
  if docker compose version >/dev/null 2>&1; then
    COMPOSE="docker compose"
  elif command -v docker-compose >/dev/null 2>&1; then
    COMPOSE="docker-compose"
  else
    err "Docker Compose not found. Install Docker (https://docs.docker.com/engine/install/) and retry."
    exit 1
  fi
}

# Poll a URL until it responds (any HTTP status) or we time out.
wait_for_http() {
  local url="$1" name="$2" timeout="${3:-120}" i=0
  info "Waiting for $name ($url) ..."
  while (( i < timeout )); do
    if curl -s -o /dev/null --max-time 2 "$url"; then
      info "$name is up."
      return 0
    fi
    sleep 1; ((i++))
  done
  err "$name did not come up within ${timeout}s."
  return 1
}

cleanup() {
  echo
  info "Shutting down ..."
  [[ -n "$CLIENT_PID" ]] && kill "$CLIENT_PID" 2>/dev/null || true
  [[ -n "$API_PID"    ]] && kill "$API_PID"    2>/dev/null || true
  # also reap any dotnet children
  [[ -n "$CLIENT_PID" ]] && pkill -P "$CLIENT_PID" 2>/dev/null || true
  [[ -n "$API_PID"    ]] && pkill -P "$API_PID"    2>/dev/null || true
  if [[ "$STOP_DB" -eq 1 ]]; then
    info "Stopping database container ..."
    $COMPOSE down || true
  else
    info "Database left running. Stop it with: ./stop.sh  (or '$COMPOSE down')."
  fi
}
trap cleanup INT TERM EXIT

# ----- prerequisites -------------------------------------------------------
command -v dotnet >/dev/null 2>&1 || { err "dotnet SDK not found. Install .NET 8/10 SDK."; exit 1; }
command -v curl   >/dev/null 2>&1 || { err "curl not found. Install curl."; exit 1; }
detect_compose

if ! docker info >/dev/null 2>&1; then
  err "Docker daemon is not running (or you lack permission). Start Docker and retry."
  exit 1
fi

# ----- 1. database ---------------------------------------------------------
info "Starting PostgreSQL via Compose ..."
$COMPOSE up -d

info "Waiting for PostgreSQL to accept connections ..."
DB_READY=0
for i in $(seq 1 60); do
  if $COMPOSE exec -T postgres pg_isready -U postgres -d dataset_studio_dev >/dev/null 2>&1; then
    DB_READY=1; break
  fi
  sleep 1
done
if [[ "$DB_READY" -ne 1 ]]; then
  err "PostgreSQL did not become ready. Check: $COMPOSE logs postgres"
  exit 1
fi
info "PostgreSQL is ready."

# ----- 2. API (auto-migrates on startup) -----------------------------------
info "Starting API ($API_URL) ..."
dotnet run --project src/APIBackend -c Debug &
API_PID=$!

wait_for_http "$API_URL/swagger/index.html" "API" 180 || {
  err "API failed to start. See the API logs above."
  exit 1
}

# ----- 3. client -----------------------------------------------------------
info "Starting client ($CLIENT_URL) ..."
dotnet run --project src/ClientApp -c Debug &
CLIENT_PID=$!

wait_for_http "$CLIENT_URL" "Client" 180 || {
  err "Client failed to start. See the client logs above."
  exit 1
}

echo
info "================================================================"
info " DatasetStudio is running:"
info "   App: $CLIENT_URL"
info "   API: $API_URL   (Swagger: $API_URL/swagger)"
info " Press Ctrl+C to stop."
info "================================================================"
echo

# Keep running until a child exits or the user hits Ctrl+C.
wait -n "$API_PID" "$CLIENT_PID"
