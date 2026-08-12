# Dataset Studio — Documentation

Reference documentation for Dataset Studio. For setup and a quick start, see the
[main README](../README.md) (requirements, `./start.sh`, configuration, and testing).

## Contents

- **[architecture.md](architecture.md)** — system architecture, the data layer (PostgreSQL +
  Parquet), the streaming vs. local data paths, the virtualized client, and the extension
  system's real state (§6 — most built-in extensions are stubs, only CoreViewer is functional).
- **[DEPLOYMENT.md](DEPLOYMENT.md)** — self-host production deployment: Docker Compose + Caddy
  reverse proxy, first-boot behavior, backups, S3 opt-in, and known Docker-build landmines.

Each `docs/verification/phase-N/` directory is a dated record of what was built and tested for
that phase of the production-readiness effort, kept for provenance rather than as reference docs
— read `architecture.md` and `DEPLOYMENT.md` for the current state, not the phase logs.
