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
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS backend-build

ARG TARGETARCH

WORKDIR /app

# Copy backend source
COPY backend/ ./

# Copy built frontend to wwwroot
COPY --from=frontend-build /app/frontend/dist/frontend/browser ./KnxMonitor.Api/wwwroot

# Restore dependencies
RUN dotnet restore

# Map Docker TARGETARCH to .NET RID
RUN if [ "$TARGETARCH" = "arm64" ]; then \
        RID="linux-arm64"; \
    else \
        RID="linux-x64"; \
    fi && \
    echo "Building for RID: $RID" && \
    dotnet publish KnxMonitor.Api/KnxMonitor.Api.csproj \
        -c Release \
        -r $RID \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true \
        -o /app/publish \
        --no-restore

# Stage 3: Minimal Alpine Runtime
FROM alpine:latest

WORKDIR /app

# Install runtime dependencies for .NET self-contained apps
RUN apk add --no-cache \
    libstdc++ \
    libintl \
    icu-libs \
    icu-data-full

# Copy self-contained application
COPY --from=backend-build /app/publish .

# Create data volume directory
RUN mkdir -p /app/data
VOLUME /app/data

# Expose port
EXPOSE 8080

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# Make the binary executable
RUN chmod +x /app/KnxMonitor.Api

# Start application (no 'dotnet' command needed - it's self-contained!)
ENTRYPOINT ["/app/KnxMonitor.Api"]
