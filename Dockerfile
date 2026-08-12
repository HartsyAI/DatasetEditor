# Multi-stage build for DatasetStudio: one container serves both the API and the published
# Blazor WASM client (the API already self-hosts it -- see UseBlazorFrameworkFiles in
# src/APIBackend/Configuration/Program.cs -- so `dotnet publish src/APIBackend` is the only
# publish command needed; the ProjectReference to ClientApp.csproj pulls its build in too).

# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Directory.Build.props lives at the repo ROOT (not under src/) and supplies
# <ImplicitUsings>enable</ImplicitUsings>, which most files in this repo rely on instead of
# explicit `using System;`-style directives. Miss this COPY and the build fails with every
# basic BCL type ("List<>", "Task", "DateTime", ...) reported as unresolved -- a genuinely
# confusing error to debug blind, since it looks exactly like a missing target-framework
# reference pack (a real, much rarer possibility for a repo mixing net10.0/net8.0 projects like
# this one) rather than a missing implicit-usings source file. Confirmed by reproducing the
# failure with this file absent and fixing it by adding just this one line, nothing else.
COPY Directory.Build.props .
COPY src/ src/

# -r linux-x64 --self-contained false: publishes a framework-dependent build for this specific
# RID, so only linux-x64's native assets (SkiaSharp, ...) get copied in (measured: 500MB -> 72MB
# publish output) instead of every RID's, without bundling the whole .NET runtime itself (the
# aspnet runtime-stage base image below already provides it). Release publish also runs the
# WASM linker/trimmer on ClientApp automatically (Blazor WebAssembly SDK default) -- verified
# safe (MudBlazor, Blazored.LocalStorage, the whole auth/routing pipeline) via a real Playwright
# run against this exact publish output, not assumed; see docs/verification/phase-6/README.md.
RUN dotnet publish src/APIBackend/APIBackend.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained false \
    -o /app/publish

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# gosu: lets docker-entrypoint.sh start as root (needed to fix up volume-mount ownership at
# container start, not build time -- see that script's own comment) and then cleanly
# exec-replace itself with the app running as the non-root "app" user (uid 1654, already
# present in this image), rather than staying root for the app's whole lifetime.
RUN apt-get update && apt-get install -y --no-install-recommends gosu \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
RUN chmod +x /usr/local/bin/docker-entrypoint.sh && chown -R app:app /app

# 8080, not 80/5000: a non-privileged port a non-root container user can bind without
# CAP_NET_BIND_SERVICE, and the de facto convention .NET's own container images expect.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["docker-entrypoint.sh"]
