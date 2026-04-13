# KNX-NG-Monitor - Projektplan

> **Stand 2026-04-13 (v0.1.0):** Phasen 1-15 abgeschlossen. Live-Image und Binaries stehen unter
> [GitHub Releases](https://github.com/ingel81/knx-ng-monitor/releases). KNX Secure (Password +
> Keyring) ist End-to-End umgesetzt. Aktuelle Lückenliste am Ende des Dokuments unter
> "Nächste Schritte". Implementierungs-Detail-Stand des Parser-Refactors: siehe
> [`docs/PARSER_LIBRARY_PROGRESS.md`](../PARSER_LIBRARY_PROGRESS.md).

## Projektziel

Ein modernes KNX Bus Monitoring-Tool mit Weboberfläche, das KNX-Telegramme in Echtzeit anzeigt, historisiert und übersichtlich darstellt. Das Tool wird als Docker-Container bereitgestellt und kombiniert ein C# Backend mit einem Angular Frontend.

---

## Tech Stack

### Backend
- **.NET 9** (ASP.NET Core Web API)
- **Entity Framework Core 9** mit SQLite
- **SignalR** für Echtzeit-Kommunikation
- **Knx.Net Library** für KNX Bus Integration
- **JWT Authentication** (Access Token + Refresh Token)

### Frontend
- **Angular** (neueste LTS Version)
- **UI Framework**: Angular Material / PrimeNG / AG-Grid (zu entscheiden)
- **RxJS** für reaktive Streams
- **SignalR Client** für WebSocket-Verbindung
- **Angular CDK Virtual Scrolling** für Performance

### Datenbank
- **SQLite** - embedded, keine separate Container nötig
- Speichert: KNX Telegramme, Gruppenadressen, Konfiguration, Projekte

### Deployment
- **Docker** - Multi-Stage Dockerfile
  - Stage 1: Node.js für Angular Build
  - Stage 2: .NET SDK für Backend Build
  - Stage 3: .NET Runtime mit Angular-Build als statische Dateien
- ASP.NET Core liefert Frontend und API aus

---

## Hauptfunktionen

### 1. KNX Bus Monitoring
- **Live-Ansicht** von KNX-Telegrammen in Echtzeit
- **Historie** aller empfangenen Telegramme
- **Persistent Logging** in SQLite
- Anzeige von:
  - Timestamp
  - Richtung (Read/Write/Response)
  - Gruppenadresse
  - Gerätename (aus .knxproj)
  - Wert (dekodiert nach DPT)
  - Flags und Metadaten

### 2. ETS Projekt Import
- **Import von .knxproj Dateien**
  - .knxproj ist ZIP-Archiv mit XML-Dateien
  - Parser für Gruppenadressen, Datenpunkttypen, Geräte
  - Extraktion von Namen, Beschreibungen, DPT-Zuordnungen
- Speicherung in SQLite
- Zuordnung von Telegrammen zu Gruppenadressen mit Namen

### 3. Live View UI
- **Grid-basierte, moderne Darstellung**
  - Tabellarische Ansicht mit Spalten
  - Neue Nachrichten oben oder unten (konfigurierbar)
  - Virtual Scrolling für Performance bei tausenden Einträgen
  - Sticky Header
  - Dunkles Theme für Monitoring-Umgebung
- **Echtzeit-Updates** via SignalR
- **Pause/Resume Button** - Live-Stream anhalten
- **Auto-Scroll Toggle** - automatisches Scrollen zu neuen Nachrichten
- **Color-Coding**:
  - Read = Blau
  - Write = Grün
  - Response = Gelb
- **Kompakt/Detail-Ansicht** umschaltbar

### 4. Umfangreiche Filter-Optionen
- **Gruppenadresse** (mit Autocomplete)
- **Geräte/Namen** (aus .knxproj)
- **Zeitraum** (von-bis)
- **Nachrichtentyp** (Read/Write/Response)
- **Wert-Bereich** (numerische Filter)
- **Freier Text-Filter** (durchsucht alle Felder)
- **Favoriten/Bookmarks** (häufig genutzte Filter speichern)
- Filter-Kombinationen als Chips dargestellt
- Live-Filter-Anwendung ohne Neuladen

### 5. KNX Verbindungs-Konfiguration
- **Settings-Seite** in Angular
- Konfigurierbare Parameter:
  - IP-Adresse des KNX IP-Interface/Router
  - Port (Standard: 3671 für KNXnet/IP)
  - Verbindungstyp:
    - Tunneling (dedizierte Verbindung)
    - Routing (Multicast)
  - Physikalische Adresse (optional)
  - Auto-Discovery (KNX-Geräte im Netzwerk suchen)
- **Validierung** (IP-Format, Port-Range)
- **Verbindungstest** vor Speichern
- **Status-Anzeige** (Connected/Disconnected/Error)
- **Reconnect-Button**
- Speicherung in SQLite

### 6. Authentication
- **JWT-basierte Authentifizierung**
  - Access Token (15 Minuten Gültigkeit)
  - Refresh Token (7 Tage Gültigkeit)
  - Login-Seite vor Zugriff auf Hauptanwendung
- Stateless - ideal für Container
- Token-Speicherung im Frontend (Memory/LocalStorage)
- Automatische Token-Erneuerung
- Logout-Funktion

---

## Projektstruktur

```
knx-ng-monitor/
├── backend/
│   ├── KnxMonitor.Api/              # ASP.NET Core Web API
│   │   ├── Controllers/             # API Endpoints
│   │   ├── Hubs/                    # SignalR Hubs
│   │   ├── Middleware/              # JWT, Error Handling
│   │   ├── Program.cs
│   │   └── appsettings.json
│   │
│   ├── KnxMonitor.Core/             # Domain Layer
│   │   ├── Entities/                # Domain Models
│   │   │   ├── User.cs
│   │   │   ├── KnxTelegram.cs
│   │   │   ├── GroupAddress.cs
│   │   │   ├── Device.cs
│   │   │   ├── Project.cs
│   │   │   └── KnxConfiguration.cs
│   │   ├── Interfaces/              # Service Interfaces
│   │   │   ├── IKnxService.cs
│   │   │   ├── IProjectImportService.cs
│   │   │   ├── ITelegramRepository.cs
│   │   │   └── IAuthService.cs
│   │   ├── DTOs/                    # Data Transfer Objects
│   │   └── Enums/                   # Enumerations
│   │
│   └── KnxMonitor.Infrastructure/   # Infrastructure Layer
│       ├── Data/                    # Database
│       │   ├── ApplicationDbContext.cs
│       │   └── Migrations/
│       ├── Repositories/            # Data Access
│       │   ├── TelegramRepository.cs
│       │   └── GroupAddressRepository.cs
│       ├── Services/                # Business Logic
│       │   ├── KnxService.cs        # KNX Bus Connection
│       │   ├── ProjectImportService.cs  # .knxproj Parser
│       │   ├── TelegramLoggingService.cs
│       │   └── AuthService.cs
│       └── KnxConnection/           # KNX Integration
│           ├── KnxClient.cs
│           └── DptConverter.cs      # Datenpunkt-Typ Decoder
│
├── frontend/                        # Angular Application
│   ├── src/
│   │   ├── app/
│   │   │   ├── core/                # Singleton Services
│   │   │   │   ├── auth/
│   │   │   │   │   ├── auth.service.ts
│   │   │   │   │   ├── auth.guard.ts
│   │   │   │   │   └── jwt.interceptor.ts
│   │   │   │   ├── signalr/
│   │   │   │   │   └── signalr.service.ts
│   │   │   │   └── api/
│   │   │   │       └── api.service.ts
│   │   │   ├── features/
│   │   │   │   ├── login/           # Login-Komponente
│   │   │   │   ├── dashboard/       # Hauptansicht
│   │   │   │   ├── live-view/       # Live Telegram View
│   │   │   │   │   ├── telegram-grid/
│   │   │   │   │   ├── filter-panel/
│   │   │   │   │   └── live-controls/
│   │   │   │   ├── history/         # Historie & Suche
│   │   │   │   ├── settings/        # KNX Konfiguration
│   │   │   │   │   └── knx-connection-settings/
│   │   │   │   └── project/         # .knxproj Management
│   │   │   │       └── project-import/
│   │   │   ├── shared/              # Wiederverwendbare Komponenten
│   │   │   │   ├── components/
│   │   │   │   ├── pipes/
│   │   │   │   └── models/
│   │   │   └── app.component.ts
│   │   ├── assets/
│   │   └── environments/
│   ├── angular.json
│   ├── package.json
│   └── tsconfig.json
│
├── data/                            # SQLite Datei (nicht in Git)
│   └── knxmonitor.db
│
├── .gitignore                       # C#, Angular, Node, SQLite
├── README.md                        # Projektdokumentation
├── Dockerfile                       # Multi-Stage Build
├── docker-compose.yml               # Optional: Entwicklung
└── PROJECT_PLAN.md                  # Dieser Plan
```

---

## Datenbank-Schema (SQLite)

### Users
```
Id (PK)
Username
PasswordHash
CreatedAt
LastLogin
```

### Projects
```
Id (PK)
Name
FileName
ImportDate
ProjectData (JSON/BLOB - optional)
```

### GroupAddresses
```
Id (PK)
ProjectId (FK)
Address (z.B. "1/2/3")
Name
Description
DatapointType (DPT)
CreatedAt
```

### Devices
```
Id (PK)
ProjectId (FK)
Name
PhysicalAddress
Manufacturer
ProductName
```

### KnxTelegrams
```
Id (PK)
Timestamp
SourceAddress
DestinationAddress (Gruppenadresse)
GroupAddressId (FK - optional)
MessageType (Read/Write/Response)
Value (Raw)
ValueDecoded (String)
Priority
Flags (JSON)
```

### KnxConfigurations
```
Id (PK)
IpAddress
Port
ConnectionType (Tunneling/Routing)
PhysicalAddress
IsActive
CreatedAt
UpdatedAt
```

### RefreshTokens
```
Id (PK)
UserId (FK)
Token
ExpiresAt
CreatedAt
```

---

## Architektur-Prinzipien

### Backend
- **Clean Architecture** / **Onion Architecture**
  - Core: Entities, Interfaces, DTOs (keine Dependencies)
  - Infrastructure: Implementierungen, EF Core, externe Libraries
  - API: Controller, SignalR Hubs, Middleware
- **Dependency Injection** durchgängig
- **Repository Pattern** für Datenzugriff
- **Service Layer** für Business Logic
- **Async/Await** für alle I/O-Operationen
- **Logging** mit ILogger
- **Error Handling** via Middleware

### Frontend
- **Modularer Aufbau** (Core, Features, Shared)
- **Reactive Programming** mit RxJS
- **Smart/Dumb Components** Pattern
  - Smart: Container mit Business Logic
  - Dumb: Presentational Components
- **Services für Zustandsverwaltung**
- **Route Guards** für Authentication
- **HTTP Interceptors** für JWT
- **Lazy Loading** für Features

### Erweiterbarkeit
- **Plugin-System** für zusätzliche KNX-Protokolle (zukünftig)
- **Export-Formate** (CSV, JSON) einfach erweiterbar
- **Custom Filter** speichern und laden
- **Notification-System** (z.B. bei bestimmten Telegrammen)
- **API Versionierung** vorbereitet

---

## API Endpoints (Übersicht)

### Authentication
```
POST /api/auth/login
POST /api/auth/refresh
POST /api/auth/logout
```

### Projects
```
GET    /api/projects
GET    /api/projects/{id}
POST   /api/projects/import          # Upload .knxproj
DELETE /api/projects/{id}
```

### Group Addresses
```
GET /api/groupaddresses
GET /api/groupaddresses/{id}
GET /api/groupaddresses/search?query=
```

### Telegrams
```
GET  /api/telegrams                  # Pagination, Filter
GET  /api/telegrams/{id}
POST /api/telegrams/search           # Erweiterte Suche
GET  /api/telegrams/export           # CSV/JSON Export
```

### KNX Configuration
```
GET    /api/knx/config
PUT    /api/knx/config
POST   /api/knx/test-connection
POST   /api/knx/discover             # Auto-Discovery
GET    /api/knx/status               # Connection Status
```

### SignalR Hub
```
/hubs/telegram                       # WebSocket für Live-Telegramme
  - OnTelegramReceived(telegram)
  - OnConnectionStatusChanged(status)
```

---

## UI/UX Anforderungen

### Design
- **Modernes, dunkles Theme** (primär für Monitoring)
- **Responsives Layout** (Desktop-fokussiert, aber mobile-fähig)
- **Material Design** oder modernes Flat Design
- **Konsistente Farbcodes**:
  - Primary: Blau/Türkis
  - Success: Grün
  - Warning: Gelb/Orange
  - Error: Rot
  - Neutral: Grau

### Performance
- **Virtual Scrolling** - nur sichtbare Zeilen rendern
- **Lazy Loading** - Features on-demand laden
- **Debounced Filter** - nicht bei jedem Tastendruck filtern
- **Efficient Change Detection** - OnPush Strategy wo möglich
- **WebSocket Throttling** - bei sehr hoher Telegram-Frequenz

### Usability
- **Keyboard Shortcuts** (z.B. Pause: Space, Filter: /)
- **Tooltips** für alle Icons und Buttons
- **Loading Indicators** für asynchrone Operationen
- **Error Messages** benutzerfreundlich
- **Confirmation Dialogs** bei kritischen Aktionen
- **Toast Notifications** für Feedback

### Accessibility
- **ARIA Labels** für Screen Reader
- **Keyboard Navigation** vollständig möglich
- **Kontrastverhältnisse** WCAG 2.1 Level AA

---

## Docker-Strategie

### Multi-Stage Dockerfile
```dockerfile
# Stage 1: Build Angular
FROM node:20 AS angular-build
WORKDIR /app/frontend
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build -- --configuration production

# Stage 2: Build .NET
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS dotnet-build
WORKDIR /app/backend
COPY backend/ ./
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=dotnet-build /app/publish ./
COPY --from=angular-build /app/frontend/dist ./wwwroot
EXPOSE 8080
ENTRYPOINT ["dotnet", "KnxMonitor.Api.dll"]
```

### Volumes
- `/app/data` - SQLite Datenbank persistieren

### Environment Variables
```
ASPNETCORE_ENVIRONMENT=Production
JWT_SECRET=<secret>
JWT_ISSUER=knx-ng-monitor
JWT_AUDIENCE=knx-ng-monitor-client
DATABASE_PATH=/app/data/knxmonitor.db
```

### docker-compose.yml (Optional für Entwicklung)
```yaml
version: '3.8'
services:
  knx-monitor:
    build: .
    ports:
      - "8080:8080"
    volumes:
      - ./data:/app/data
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
```

---

## Implementierungsphasen

### Phase 1: Projektsetup
- [x] Projektplanung und Anforderungsanalyse
- [x] Projektstruktur anlegen
- [x] .gitignore, README.md
- [x] Backend: .NET 9 Solution mit 3 Projekten (+ später ProjectParser, ProjectParser.Tests, ParserTool)
- [x] Frontend: Angular initialisieren
- [x] Initial Commit

### Phase 2: Backend Grundgerüst
- [x] EF Core + SQLite einrichten
- [x] Entities definieren
- [x] DbContext und Migrations
- [x] Repository Pattern implementieren
- [x] Dependency Injection konfigurieren
- [x] Logging einrichten (Serilog-Cleanup ist noch TODO, siehe unten)

### Phase 3: Authentication
- [x] JWT Service implementieren (Secret wird beim ersten Start auto-generiert in `./data/.jwt-secret`)
- [x] User Management (Repository, Service)
- [x] Auth Controller (Login, Refresh, Logout)
- [x] JWT Middleware
- [x] Initial-Setup-Wizard (statt Seed) — Admin wird beim ersten UI-Start angelegt

### Phase 4: KNX Integration
- [x] Knx.Falcon.Sdk integriert (statt der ursprünglich geplanten Knx.Net)
- [x] KnxConnectionService implementiert
- [x] DPT Decoder (DPT 1/2/3/5/6/7/8/9/10/11/12/13/14/16/17/18/19/20)
- [x] Configuration Management (KnxConfiguration Entity)
- [x] Connection Status Tracking
- [x] Telegram Logging Service

### Phase 5: .knxproj Parser
- [x] In dedizierte Library `KnxMonitor.ProjectParser` ausgelagert
- [x] ETS 4 / 5 / 6 Loader, Multi-Addressing-Style (3-Level / 2-Level / Free)
- [x] Password-Support inkl. ETS6-PBKDF2/AES-Wrapping
- [x] **KNX Secure Keyring (.knxkeys)** — PBKDF2-HMAC-SHA256 + AES-128-CBC, BackboneKey, ToolKeys, ManagementPassword, GA-Keys
- [x] DPT-Parsing inkl. Main/Sub-Number
- [x] Import-Wizard 2-stufig (Re-Detect nach Password, optionaler Keyring-Step)
- [x] Auto-Activate des ersten Projekts
- Detail: siehe [`docs/PARSER_LIBRARY_PROGRESS.md`](../PARSER_LIBRARY_PROGRESS.md)

### Phase 6: Telegram API
- [x] Telegram Repository
- [x] Telegram Controller
- [x] Filter-Logik
- [x] Export-Funktionen (CSV, JSON)
- [x] Performance-Optimierungen (Indizes auf Timestamp / DestinationAddress)

### Phase 7: SignalR Integration
- [x] SignalR Hub implementiert
- [x] Telegram Broadcasting
- [x] Connection Status Broadcasting
- [x] Authentifizierung für WebSocket (JWT via Query-String beim Negotiate)

### Phase 8: Frontend Grundgerüst
- [x] Angular Projekt strukturieren (Core, Features, Shared) — Standalone-Components
- [x] Routing konfigurieren
- [x] UI Framework: Angular Material + AG-Grid Community
- [x] Dark Theme
- [x] Layout Components (Header, Sidebar)

### Phase 9: Frontend Authentication
- [x] Login-Komponente
- [x] Auth Service (JWT handling)
- [x] Auth Guard
- [x] HTTP Interceptor für Token
- [x] Auto-Refresh Logic
- [x] Logout-Funktion

### Phase 10: Settings & KNX Configuration
- [x] Settings-Seite
- [x] KNX Configuration Form mit Validierung
- [x] Connection Test
- [x] Status-Anzeige
- [x] API Integration

### Phase 11: Project Import UI
- [x] File Upload Komponente
- [x] .knxproj Upload
- [x] Import Progress (Step-Status, Polling)
- [x] Project List
- [x] Project Details / Management
- [x] Group Address Browser
- [x] Two-stage Wizard für KNX Secure (Password → optionaler Keyring)

### Phase 12: Live View Implementation
- [x] SignalR Client Service
- [x] Telegram Model & DTOs
- [x] AG-Grid mit Virtual Scrolling
- [x] Live-Controls (Pause, Resume, Clear)
- [x] Color-Coding nach Message Type
- [x] Auto-Scroll Toggle
- [x] Timestamp Formatting
- [x] Value Decoding Display

### Phase 13: Filter System
- [x] Quick-Filter (free text)
- [x] AG-Grid Floating Filters pro Spalte (Sortierung, Filterung)
- [ ] Filter Chips / Filter Presets / Favoriten — (noch offen, siehe "Nächste Schritte")
- [ ] Wert-Bereich-Filter / Zeitraum-Picker als dedizierte UI — (noch offen)

### Phase 14: History & Search
- [x] History View Component
- [x] Pagination
- [x] Export-Button (CSV/JSON)
- [ ] Detail-Ansicht / Telegramm-Vergleich — noch offen

### Phase 15: Docker & Deployment
- [x] Multi-Stage Dockerfile (Node → SDK → debian:12-slim)
- [x] Docker Build / Image auf DockerHub (`ingel81/knx-ng-monitor`)
- [x] docker-compose.yml
- [x] Volume für Datenbank
- [x] Environment Variables (`ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_URLS`)
- [x] Production Build optimiert (single-file, compressed)
- [ ] Health Check Endpoint — noch offen

### Phase 16: Testing & Dokumentation
- [x] Backend xUnit Tests (Parser-Library: 187 Tests, ~99 % Line Coverage)
- [ ] API Integration Tests (über die Library hinaus) — noch offen
- [ ] Frontend Unit Tests (ng test läuft, aber Coverage wäre auszubauen)
- [ ] E2E Tests (Cypress/Playwright) — noch offen
- [x] API Dokumentation (Scalar/OpenAPI in Development)
- [x] README mit Setup-Anleitung
- [x] CLAUDE.md, PARSER_LIBRARY_PROGRESS, SAMPLE_TESTS aktuell

### Phase 17: Optimierungen & Polish
- [x] Performance-Tuning (Cache, Indizes, Virtual Scrolling)
- [x] Loading States in den meisten Flows
- [ ] Vollständiger Accessibility-Audit
- [ ] Browser-Kompatibilitäts-Matrix dokumentieren
- [ ] Mobile-Ansicht systematisch optimieren
- [ ] Logging cleanup (Serilog-Templates, Entfernen der `Console.WriteLine` aus `ProjectImportService`)

---

## Offene Entscheidungen

### UI Framework
- **Angular Material** (lean, gut integriert)
- **PrimeNG** (feature-reich, professionelle Components)
- **AG-Grid** (extrem performant für große Grids)

**Empfehlung**: Angular Material für Start, bei Bedarf später AG-Grid für Grid

### KNX Library
- **Knx.Net** (aktiv maintained, .NET Core)
- Alternative Evaluierung falls nötig

### Zusätzliche Features (Nice-to-have)
- **Benachrichtigungen** bei bestimmten Telegrammen (z.B. Alarm-Gruppen)
- **Statistiken & Dashboard** (Telegramme pro Stunde, Top-Devices)
- **Telegram senden** (Write-Funktion, nicht nur Monitoring)
- **Multi-User Support** (Rollen: Admin, Viewer)
- **Dunkler/Heller Theme-Toggle**
- **Grafische Darstellung** (Charts für Werte über Zeit)
- **Telegram-Vergleich** (Diff zwischen zwei Zeitpunkten)

---

## Erfolgskriterien

### Funktional
- [ ] KNX Telegramme werden in Echtzeit angezeigt
- [ ] .knxproj kann erfolgreich importiert werden
- [ ] Gruppenadressen werden mit Namen angezeigt
- [ ] Filter funktionieren performant und intuitiv
- [ ] Historie wird persistent gespeichert
- [ ] Authentication funktioniert sicher
- [ ] Docker-Container läuft stabil

### Nicht-funktional
- [ ] **Performance**: 1000+ Telegramme ohne Lag anzeigen
- [ ] **Latenz**: < 100ms vom Bus bis zur UI
- [ ] **Startup-Zeit**: < 10 Sekunden
- [ ] **Memory**: < 500 MB RAM im Betrieb
- [ ] **Container-Größe**: < 300 MB
- [ ] **Responsiveness**: UI reagiert < 100ms auf User-Input

---

## Zeitplan (grobe Schätzung)

- **Phase 1-3**: 1-2 Tage (Setup, Backend-Basis, Auth)
- **Phase 4-7**: 3-5 Tage (KNX, Parser, SignalR)
- **Phase 8-11**: 2-3 Tage (Frontend-Basis, Auth, Settings, Import)
- **Phase 12-14**: 4-6 Tage (Live View, Filter, History)
- **Phase 15-17**: 2-3 Tage (Docker, Testing, Polish)

**Gesamt**: ca. 12-19 Arbeitstage (bei fokussierter Entwicklung)

---

## Risiken & Mitigation

### Risiko: KNX Library Kompatibilität
- **Mitigation**: Früh testen, ggf. alternative Library evaluieren

### Risiko: Performance bei hoher Telegram-Frequenz
- **Mitigation**: Virtual Scrolling, Throttling, Batching

### Risiko: .knxproj Format-Änderungen
- **Mitigation**: Versionserkennung, robuster Parser mit Fallbacks

### Risiko: JWT Security
- **Mitigation**: Best Practices (HTTPS, Secure Storage, kurze Token-Lifetimes)

### Risiko: Docker-Größe
- **Mitigation**: Multi-Stage Build, Alpine-Images, Layer-Optimierung

---

## Nächste Schritte

(Stand 2026-04-13, bezogen auf v0.1.0)

**Parser / KNX Secure**
- Telegramm-Decryption zur Laufzeit mit den abgespeicherten ToolKeys / GA-Keys (KNX Data Secure am Bus)
- Erweiterte Parser-Features: Communication Objects, Topology, Locations, Functions
- Frontend: Keyring-Datei nachträglich pro Projekt hochladen / aktualisieren

**UI / UX**
- Filter-Chips + speicherbare Filter-Presets (Phase 13)
- Telegramm-Detail-Drawer + Vergleich zweier Zeitpunkte (Phase 14)
- Mobile-Ansicht optimieren, Accessibility-Audit (WCAG 2.1 AA)

**Betrieb**
- Health-Check-Endpoint
- Logging-Cleanup (Serilog-Konfiguration vereinheitlichen, `Console.WriteLine` aus
  `ProjectImportService` entfernen)
- API Integration- und E2E-Tests (Cypress/Playwright)

---

**Autor**: Joerg
**Erstellt**: 2025-10-25
**Letzte Aktualisierung**: 2026-04-13 (v0.1.0)
