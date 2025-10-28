# KNX-NG-Monitor

A modern, self-contained KNX bus monitoring tool with a web interface that displays, historizes, and presents KNX telegrams in real-time.

[![Release](https://img.shields.io/github/v/release/ingel81/knx-ng-monitor)](https://github.com/ingel81/knx-ng-monitor/releases)
[![Docker](https://img.shields.io/docker/v/ingel81/knx-ng-monitor?label=docker)](https://hub.docker.com/r/ingel81/knx-ng-monitor)
[![License](https://img.shields.io/github/license/ingel81/knx-ng-monitor)](LICENSE)

## Features

- **Real-time KNX Monitoring**: Live view of KNX telegrams via SignalR WebSocket
- **ETS Project Import**: Parse .knxproj files to extract group addresses, devices, and DPT mappings
- **Live View UI**: Grid-based display with virtual scrolling, color-coding, pause/resume, auto-scroll
- **Advanced Filtering**: By group address, device, time range, message type, value range, free text
- **KNX Connection Configuration**: IP-based KNX interface connection (Tunneling/Routing)
- **JWT Authentication**: Secure access with Access tokens (15 min) + Refresh tokens (7 days)
- **Persistent History**: All telegrams stored in SQLite database
- **Export Functions**: CSV and JSON export capabilities

## Tech Stack

### Backend
- .NET 9 (ASP.NET Core Web API)
- Entity Framework Core 9 with SQLite
- SignalR for real-time communication
- Knx.Net Library for KNX bus integration
- JWT Authentication

### Frontend
- Angular (latest LTS)
- Angular Material / PrimeNG
- RxJS for reactive streams
- SignalR Client for WebSocket
- Angular CDK Virtual Scrolling

### Database
- SQLite (embedded, no separate container needed)

## Architecture

```
knx-ng-monitor/
├── backend/
│   ├── KnxMonitor.Api/              # ASP.NET Core Web API
│   ├── KnxMonitor.Core/             # Domain Layer (Entities, Interfaces, DTOs)
│   └── KnxMonitor.Infrastructure/   # Infrastructure Layer (Data, Repositories, Services)
├── frontend/                        # Angular Application
├── data/                            # SQLite database (not in git)
├── Dockerfile                       # Multi-stage build
└── docker-compose.yml               # Optional: development
```

## Quick Start (Production)

### Using Docker (Recommended)

```bash
# Pull and run
docker run -d \
  -p 8080:8080 \
  -v ./data:/app/data \
  --name knx-monitor \
  ingel81/knx-ng-monitor:latest

# Access: http://localhost:8080
```

Or using docker-compose:

```yaml
version: '3.8'
services:
  knx-monitor:
    image: ingel81/knx-ng-monitor:latest
    container_name: knx-monitor
    restart: unless-stopped
    ports:
      - "8080:8080"
    volumes:
      - ./data:/app/data
```

### Using Self-Contained Binary

**No .NET installation required!** Download from [Releases](https://github.com/ingel81/knx-ng-monitor/releases):

**Linux/macOS:**
```bash
wget https://github.com/ingel81/knx-ng-monitor/releases/download/v0.0.1/knx-ng-monitor-linux-x64.tar.gz
tar -xzf knx-ng-monitor-linux-x64.tar.gz
./KnxMonitor.Api
# Access: http://localhost:8080
```

**Windows:**
- Download `knx-ng-monitor-win-x64.zip`
- Extract and run `KnxMonitor.Api.exe`
- Access: http://localhost:8080

### First-Time Setup

1. Open browser at `http://localhost:8080`
2. Complete initial setup wizard
3. Create admin account
4. Import your .knxproj file (optional)
5. Configure KNX IP-Interface connection
6. Start monitoring!

## Development Setup

### Prerequisites

- .NET 9 SDK
- Node.js 20+ and npm
- Docker (optional, for containerized deployment)

### Building from Source

#### Backend

```bash
cd backend
dotnet restore
dotnet build
dotnet run --project KnxMonitor.Api
```

The API will be available at `https://localhost:5001`

#### Frontend

```bash
cd frontend
npm install
ng serve
```

The Angular app will be available at `http://localhost:4200`

### Docker Deployment

```bash
# Build the Docker image
docker build -t knx-ng-monitor .

# Run the container
docker run -p 8080:8080 -v ./data:/app/data knx-ng-monitor
```

Or using docker-compose:

```bash
docker-compose up --build
```

## Configuration

### Environment Variables

- `ASPNETCORE_ENVIRONMENT`: Production|Development (default: Production)
- `ASPNETCORE_URLS`: http://+:8080 (default, change port if needed)

**Note:** JWT Secret is automatically generated on first start and persisted in `./data/.jwt-secret`

### Data Directory

All persistent data is stored in `./data/`:
- `.jwt-secret` - Auto-generated JWT secret (do NOT delete!)
- `knxmonitor.db` - SQLite database (telegrams, users, projects, etc.)

**Docker Volume:** `-v ./data:/app/data`
**Binary:** Created in same directory as executable

### KNX Connection

Configure your KNX interface through the Settings page in the web UI:
- IP Address of KNX IP-Interface/Router
- Port (default: 3671 for KNXnet/IP)
- Connection Type: Tunneling or Routing
- Physical Address (optional)

## API Documentation

OpenAPI documentation is available in Development mode:
- Development: `http://localhost:5075/scalar/v1` (when running backend separately)
- Production: Integrated in frontend, no separate Swagger UI

## Database Schema

The application uses SQLite with the following main entities:
- **Users**: Authentication and user management
- **Projects**: Imported .knxproj files
- **GroupAddresses**: KNX group addresses from ETS projects
- **Devices**: KNX devices from ETS projects
- **KnxTelegrams**: Logged KNX telegrams (main monitoring data)
- **KnxConfigurations**: KNX connection settings
- **RefreshTokens**: JWT refresh tokens

## Development

### Backend Commands

```bash
# Create a new migration
dotnet ef migrations add <MigrationName> --project KnxMonitor.Infrastructure --startup-project KnxMonitor.Api

# Update database
dotnet ef database update --project KnxMonitor.Infrastructure --startup-project KnxMonitor.Api

# Run tests
dotnet test
```

### Frontend Commands

```bash
# Generate a new component
ng generate component features/<feature-name>/<component-name>

# Generate a new service
ng generate service core/<service-name>

# Run tests
ng test

# Build for production
ng build --configuration production
```

## Contributing

This is a personal project. If you have suggestions or find bugs, feel free to open an issue.

## License

MIT

## Author

Joerg

## Releases

### v0.0.1 (Latest)

**Production-Ready Features:**
- ✅ Self-contained binaries for 6 platforms (Linux, Windows, macOS - x64 & ARM64)
- ✅ Docker image (linux/amd64, ~120 MB)
- ✅ Auto-generated JWT secrets (secure by default)
- ✅ Correct database paths for Docker & binaries
- ✅ Environment-based configuration (Dev/Prod separation)
- ✅ Backend serves frontend in production (single port deployment)

**Downloads:**
- Docker: `docker pull ingel81/knx-ng-monitor:v0.0.1`
- Binaries: [GitHub Releases](https://github.com/ingel81/knx-ng-monitor/releases/tag/v0.0.1)

## Project Status

**Current:** v0.0.1 - Production ready for basic KNX monitoring
**Next:** Performance optimizations, additional features

See [docs/ai/PROJECT_PLAN.md](docs/ai/PROJECT_PLAN.md) for detailed implementation phases and [docs/ai/RELEASE_PLAN.md](docs/ai/RELEASE_PLAN.md) for release automation details.
