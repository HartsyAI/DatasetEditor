#!/bin/sh
# Runs as root (see Dockerfile: no USER directive before this) specifically to fix up
# ownership of Docker named volumes mounted at container START, not build, time -- a fresh
# named volume (Storage:*Path -- data/, blobs/, uploads/) is created root-owned by the Docker
# daemon regardless of what the image had baked in at that path, so the Dockerfile's own
# build-time `chown -R app:app /app` never applies to them; confirmed live (the api container
# crash-looped on "Access to the path '/app/data/parquet' is denied" the first time this ran
# without this fixup). gosu then exec-replaces this process with the actual app running as the
# non-root "app" user -- unlike `su`, it doesn't leave a wrapper process between PID 1 and the
# app, so SIGTERM (docker stop) reaches dotnet directly for a clean shutdown.
set -e
chown -R app:app /app/data /app/blobs /app/uploads 2>/dev/null || true
exec gosu app dotnet APIBackend.dll
