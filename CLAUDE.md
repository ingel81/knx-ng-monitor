# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

KNX-NG-Monitor is a modern KNX bus monitoring tool with a web interface that displays, historizes, and presents KNX telegrams in real-time. The application is deployed as a Docker container combining a .NET 9 backend with an Angular frontend.

## Tech Stack

### Backend
- **.NET 9** (ASP.NET Core Web API)
- **Entity Framework Core 9** with SQLite
- **SignalR** for real-time communication
- **Knx.Net Library** for KNX bus integration
- **JWT Authentication** (Access Token + Refresh Token)

### Frontend
- **Angular** (latest LTS version)
- **UI Framework**: Angular Material / PrimeNG / AG-Grid (to be decided)
- **RxJS** for reactive streams
- **SignalR Client** for WebSocket connection
- **Angular CDK Virtual Scrolling** for performance

### Database
- **SQLite** - embedded database
- Stores: KNX telegrams, group addresses, configuration, projects

## Project Structure

```
knx-ng-monitor/
├── backend/
│   ├── KnxMonitor.Api/              # ASP.NET Core Web API
│   ├── KnxMonitor.Core/             # Domain Layer (Entities, Interfaces, DTOs)
│   └── KnxMonitor.Infrastructure/   # Infrastructure Layer (Data, Repositories, Services)
├── frontend/                        # Angular Application
│   └── src/app/
│       ├── core/                    # Singleton Services (auth, signalr, api)
│       ├── features/                # Feature modules (login, dashboard, live-view, etc.)
│       └── shared/                  # Reusable components, pipes, models
├── data/                            # SQLite database (not in git)
├── Dockerfile                       # Multi-stage build
└── docker-compose.yml               # Optional: development
```

## Architecture Principles

### Backend (Clean Architecture)
- **Core Layer**: Entities, Interfaces, DTOs (no dependencies)
- **Infrastructure Layer**: Implementations, EF Core, external libraries
- **API Layer**: Controllers, SignalR Hubs, Middleware
- **Patterns**: Repository Pattern, Dependency Injection, Async/Await throughout

### Frontend
- **Modular structure**: Core, Features, Shared
- **Reactive Programming** with RxJS
- **Smart/Dumb Components Pattern**:
  - Smart: Containers with business logic
  - Dumb: Presentational components
- **Route Guards** for authentication
- **HTTP Interceptors** for JWT
- **Lazy Loading** for features

## Development Commands

### Backend (.NET)
```bash
# Navigate to backend directory
cd backend

# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Run the API project
dotnet run --project KnxMonitor.Api

# Run tests
dotnet test

# Create a new migration
dotnet ef migrations add <MigrationName> --project KnxMonitor.Infrastructure --startup-project KnxMonitor.Api

# Update database
dotnet ef database update --project KnxMonitor.Infrastructure --startup-project KnxMonitor.Api

# Publish for production
dotnet publish -c Release -o /app/publish
```

### Frontend (Angular)
```bash
# Navigate to frontend directory
cd frontend

# Install dependencies
npm install

# Start development server
ng serve

# Build for production
ng build --configuration production

# Run tests
ng test

# Run end-to-end tests
ng e2e

# Generate a new component
ng generate component features/<feature-name>/<component-name>

# Generate a new service
ng generate service core/<service-name>
```

### Docker
```bash
# Build Docker image
docker build -t knx-ng-monitor .

# Run container
docker run -p 8080:8080 -v ./data:/app/data knx-ng-monitor

# Using docker-compose (development)
docker-compose up --build

# Stop containers
docker-compose down
```

## Database Schema

### Core Entities
- **Users**: Authentication and user management
- **Projects**: Imported .knxproj files
- **GroupAddresses**: KNX group addresses from ETS projects (linked to Projects)
- **Devices**: KNX devices from ETS projects (linked to Projects)
- **KnxTelegrams**: Logged KNX telegrams (main monitoring data)
- **KnxConfigurations**: KNX connection settings (IP, port, connection type)
- **RefreshTokens**: JWT refresh tokens for authentication

## API Endpoints Structure

- `/api/auth/*` - Authentication (login, refresh, logout)
- `/api/projects/*` - Project management and .knxproj import
- `/api/groupaddresses/*` - Group address queries
- `/api/telegrams/*` - Telegram queries, search, and export
- `/api/knx/*` - KNX configuration and connection management
- `/hubs/telegram` - SignalR hub for real-time telegram broadcasting

## Key Features

1. **Real-time KNX Monitoring**: Live view of KNX telegrams via SignalR
2. **ETS Project Import**: Parse .knxproj files (ZIP archives with XML) to extract group addresses, devices, and DPT mappings
3. **Live View UI**: Grid-based display with virtual scrolling, color-coding (Read=Blue, Write=Green, Response=Yellow), pause/resume, auto-scroll
4. **Advanced Filtering**: By group address, device, time range, message type, value range, free text
5. **KNX Connection Configuration**: IP-based KNX interface connection (Tunneling/Routing)
6. **JWT Authentication**: Access tokens (15 min) + Refresh tokens (7 days)

## Important Implementation Notes

### .knxproj Parser
- .knxproj files are ZIP archives containing XML files
- Extract and parse to get group addresses, devices, DPT types
- Handle different ETS versions with version detection

### DPT Conversion
- Implement DPT (Datapoint Type) decoder for common types
- Convert raw KNX values to human-readable formats
- Located in `KnxMonitor.Infrastructure/KnxConnection/DptConverter.cs`

### Performance Considerations
- Use **Virtual Scrolling** in Angular for large telegram lists (1000+ items)
- Implement **SignalR throttling** for high-frequency telegram bursts
- Use **OnPush Change Detection** strategy where possible
- Implement **debounced filtering** to avoid excessive re-renders
- Database indexing on frequently queried fields (timestamp, group address)

### SignalR Authentication
- SignalR hub must validate JWT tokens
- Implement authentication in hub configuration

## Docker Multi-Stage Build

The Dockerfile uses three stages:
1. **Node.js** - Build Angular frontend
2. **.NET SDK** - Build backend
3. **.NET Runtime** - Final image with compiled backend + Angular static files in wwwroot

## Environment Variables

```
ASPNETCORE_ENVIRONMENT=Production|Development
JWT_SECRET=<secret-key>
JWT_ISSUER=knx-ng-monitor
JWT_AUDIENCE=knx-ng-monitor-client
DATABASE_PATH=/app/data/knxmonitor.db
```

## Current Implementation Status

The project is in the planning phase. Refer to PROJECT_PLAN.md for detailed implementation phases (Phase 1-17) and task checklists.

Next steps according to the plan:
1. Create project structure (backend solution + frontend Angular app)
2. Set up backend fundamentals (EF Core, entities, repositories)
3. Implement authentication system
4. Integrate KNX library and implement monitoring
5. Build .knxproj parser
6. Create frontend architecture and UI components
7. Implement Docker deployment

## Design Guidelines

- **Dark theme** as primary (for monitoring environments)
- **Responsive layout** (desktop-focused but mobile-capable)
- Use **virtual scrolling** for performance with large datasets
- **Keyboard shortcuts** for common actions (e.g., Space for pause, / for filter)
- Provide **tooltips** for all icons and buttons
- Show **loading indicators** for all async operations
- Display **user-friendly error messages**
- WCAG 2.1 Level AA compliance for accessibility
- schreibe commits generell immer kompakt und immer ohne claude footer.
- never commit on your own
- parallel läuft immer bereits ng serve