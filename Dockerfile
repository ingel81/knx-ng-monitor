# Stage 1: Build Angular Frontend
FROM node:20-alpine AS frontend-build

WORKDIR /app/frontend

# Copy package files and install dependencies
COPY frontend/package*.json ./
RUN npm ci --silent

# Copy frontend source and build
COPY frontend/ ./
RUN npm run build -- --configuration production

# Stage 2: Build .NET Backend
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS backend-build

WORKDIR /app

# Copy backend source
COPY backend/ ./

# Copy built frontend to wwwroot
COPY --from=frontend-build /app/frontend/dist/frontend/browser ./KnxMonitor.Api/wwwroot

# Restore dependencies
RUN dotnet restore

# Build and publish
RUN dotnet publish KnxMonitor.Api/KnxMonitor.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Stage 3: Runtime Image
FROM mcr.microsoft.com/dotnet/aspnet:9.0

WORKDIR /app

# Copy published application
COPY --from=backend-build /app/publish .

# Create data volume directory
VOLUME /app/data

# Expose port
EXPOSE 8080

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Start application
ENTRYPOINT ["dotnet", "KnxMonitor.Api.dll"]
