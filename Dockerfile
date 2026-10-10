# Stage 1: Build Angular Frontend
FROM node:20-alpine AS frontend-build

WORKDIR /app/frontend

# Copy package files and install dependencies
COPY frontend/package*.json ./
RUN npm ci --silent

# Copy frontend source and build
COPY frontend/ ./
RUN npm run build -- --configuration production

# Stage 2: Build Self-Contained .NET Backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build

ARG TARGETARCH
# Release version forwarded from CI (build-arg). Empty by default, in which case
# dotnet falls back to the csproj <Version>.
ARG VERSION=

WORKDIR /app

# Copy backend source
COPY backend/ ./

# Copy built frontend to wwwroot
COPY --from=frontend-build /app/frontend/dist/frontend/browser ./KnxMonitor.Api/wwwroot

# Build self-contained binary for target architecture
# Note: dotnet publish will automatically restore with the correct RID
RUN if [ "$TARGETARCH" = "arm64" ]; then \
        RID="linux-arm64"; \
    else \
        RID="linux-x64"; \
    fi && \
    echo "Building for RID: $RID" && \
    # Only override the version when VERSION is non-empty; otherwise let the
    # csproj <Version> apply (avoids publishing an empty/blank version).
    if [ -n "$VERSION" ]; then VERSION_ARG="-p:Version=$VERSION"; else VERSION_ARG=""; fi && \
    dotnet publish KnxMonitor.Api/KnxMonitor.Api.csproj \
        -c Release \
        -r $RID \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true \
        $VERSION_ARG \
        -o /app/publish

# Stage 3: Debian Slim Runtime (glibc compatible)
FROM debian:12-slim

WORKDIR /app

# Install runtime dependencies for .NET self-contained apps.
# wget is used by the HEALTHCHECK below.
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        libicu72 \
        ca-certificates \
        wget && \
    rm -rf /var/lib/apt/lists/*

# Create unprivileged user (uid 1000 = first non-system uid on Debian).
RUN groupadd --system --gid 1000 app && \
    useradd  --system --uid 1000 --gid app --home /app --shell /usr/sbin/nologin app

# Copy self-contained application with non-root ownership.
COPY --from=backend-build --chown=app:app /app/publish .

# Entrypoint fixes /app/data ownership and drops to PUID:PGID (default 1000:1000), see #40.
COPY --chmod=755 docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh

# Data volume directory owned by app user.
RUN mkdir -p /app/data && chown app:app /app/data
VOLUME /app/data

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# Single-file self-extracting bundle (IncludeNativeLibrariesForSelfExtract=true)
# defaults its extraction cache to the user's HOME (/app), which is root-owned.
# Point it at /tmp/.net instead so the non-root `app` user can write there.
ENV DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/.net

RUN chmod +x /app/KnxMonitor.Api

# The image starts as root so the entrypoint can hand the data directory to the right user;
# the application itself never runs as root (setpriv drops to PUID:PGID, default 1000:1000).
# Starting the container with --user / compose "user:" still works and skips that step.

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD wget --quiet --spider http://localhost:8080/healthz || exit 1

ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
