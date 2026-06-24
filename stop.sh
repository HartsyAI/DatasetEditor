#!/usr/bin/env bash
#
# Stops the DatasetStudio dev stack started by ./start.sh.
#   - stops any dotnet API/client processes for this repo
#   - stops the PostgreSQL container (data is preserved in the volume)
#
# Usage:
#   ./stop.sh            # stop apps + database container (keep data)
#   ./stop.sh --wipe     # also delete the database volume (destroys all data)

set -euo pipefail
cd "$(dirname "$0")"

WIPE=0
[[ "${1:-}" == "--wipe" ]] && WIPE=1

info() { printf '\033[1;34m[stop]\033[0m %s\n' "$*"; }

# Stop dotnet processes launched from this repo (API + client).
REPO_DIR="$(pwd)"
info "Stopping API/client dotnet processes ..."
pkill -f "dotnet.*${REPO_DIR}/src/APIBackend" 2>/dev/null || true
pkill -f "dotnet.*${REPO_DIR}/src/ClientApp"  2>/dev/null || true
pkill -f "APIBackend.dll" 2>/dev/null || true
pkill -f "ClientApp.dll"  2>/dev/null || true

# Pick compose command.
if docker compose version >/dev/null 2>&1; then
  COMPOSE="docker compose"
elif command -v docker-compose >/dev/null 2>&1; then
  COMPOSE="docker-compose"
else
  info "Docker Compose not found; skipping database shutdown."
  exit 0
fi

if [[ "$WIPE" -eq 1 ]]; then
  info "Stopping database and DELETING its data volume ..."
  $COMPOSE down -v
else
  info "Stopping database container (data preserved) ..."
  $COMPOSE down
fi

info "Done."
