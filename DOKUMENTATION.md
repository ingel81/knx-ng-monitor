# KNX-NG-Monitor - Technische Dokumentation

## Projektübersicht

KNX-NG-Monitor ist ein modernes KNX-Bus-Monitoring-Tool mit Webinterface zur Echtzeitüberwachung, Historisierung und Visualisierung von KNX-Telegrammen. Die Anwendung besteht aus einem .NET 9 Backend und einem Angular Frontend.

## Technologie-Stack

### Backend
- **.NET 9** (ASP.NET Core Web API)
- **Entity Framework Core 9** mit SQLite
- **SignalR** für Echtzeit-Kommunikation
- **Knx.Falcon.Sdk** für KNX-Bus-Integration
- **JWT Authentication** (Access Token + Refresh Token)

### Frontend
- **Angular 20** (neueste Version)
- **Angular Material** für UI-Komponenten
- **AG-Grid Community** für performante Tabellendarstellung
- **RxJS** für reaktive Datenströme
- **SignalR Client** für WebSocket-Verbindung

### Datenbank
- **SQLite** - Embedded-Datenbank für Telegramme, Gruppenadressen und Konfiguration

## Architektur

### Backend (Clean Architecture)

```
backend/
├── KnxMonitor.Api/              # ASP.NET Core Web API
│   ├── Controllers/
│   │   └── ProjectsController.cs
│   ├── Hubs/
│   │   └── TelegramHub.cs
│   ├── Services/
│   │   └── TelegramBroadcastService.cs
│   └── Program.cs
│
├── KnxMonitor.Core/             # Domain Layer
│   ├── Entities/
│   │   ├── Project.cs
│   │   ├── GroupAddress.cs
│   │   ├── Device.cs
│   │   ├── KnxTelegram.cs
│   │   └── KnxConfiguration.cs
│   ├── DTOs/
│   │   ├── ProjectDto.cs
│   │   ├── ProjectDetailsDto.cs
│   │   ├── GroupAddressDto.cs
│   │   └── DeviceDto.cs
│   └── Interfaces/
│       ├── IProjectService.cs
│       ├── IProjectRepository.cs
│       └── IKnxProjectParserService.cs
│
└── KnxMonitor.Infrastructure/   # Infrastructure Layer
    ├── Data/
    │   ├── ApplicationDbContext.cs
    │   └── Migrations/
    ├── KnxConnection/
    │   ├── KnxConnectionService.cs
    │   └── DptConverter.cs
    ├── Repositories/
    │   ├── ProjectRepository.cs
    │   └── GroupAddressRepository.cs
    └── Services/
        ├── ProjectService.cs
        ├── KnxProjectParserService.cs
        └── GroupAddressCacheService.cs
```

### Frontend (Modulare Struktur)

```
frontend/src/app/
├── core/                        # Singleton-Services
│   └── services/
│       ├── signalr.service.ts
│       └── project.service.ts
│
├── features/                    # Feature-Module
│   ├── live-view/
│   │   ├── live-view.component.ts
│   │   ├── live-view.component.html
│   │   └── live-view.component.scss
│   ├── projects/
│   │   ├── projects.component.ts
│   │   ├── projects.component.html
│   │   └── projects.component.scss
│   └── settings/
│       ├── settings.ts
│       └── settings.html
│
└── shared/                      # Gemeinsame Komponenten
    └── layout/
        ├── layout.html
        └── layout.scss
```

## Kernfunktionalitäten

### 1. Projekt-Management

**Zweck**: Verwaltung von ETS-Projekten (.knxproj-Dateien) mit Gruppenadressen und Geräten.

**Backend-Komponenten**:
- **ProjectsController** (`backend/KnxMonitor.Api/Controllers/ProjectsController.cs`):
  - `POST /api/projects/upload` - Upload von .knxproj-Dateien
  - `GET /api/projects` - Liste aller Projekte
  - `GET /api/projects/{id}` - Projektdetails mit Gruppenadressen und Geräten
  - `PUT /api/projects/{id}/activate` - Projekt aktivieren
  - `DELETE /api/projects/{id}` - Projekt löschen

- **ProjectService** (`backend/KnxMonitor.Infrastructure/Services/ProjectService.cs`):
  - Verarbeitet Projekt-Upload und koordiniert Parser
  - Verwaltet Projekt-Aktivierung (nur ein Projekt kann aktiv sein)
  - Triggert Cache-Refresh bei Projektwechsel

- **KnxProjectParserService** (`backend/KnxMonitor.Infrastructure/Services/KnxProjectParserService.cs`):
  - Parst .knxproj-Dateien (ZIP-Archive mit XML)
  - Extrahiert Gruppenadressen mit DPT-Typen
  - Extrahiert Geräte mit Hersteller- und Produktinformationen
  - Konvertiert KNX-Adressen in lesbare Formate:
    - Gruppenaddresse: `x/y/z` (z.B. `1/2/3`)
    - Physikalische Adresse: `x.y.z` (z.B. `1.1.5`)
  - Unterstützt verschiedene ETS-Versionen durch Namespace-Erkennung

**Frontend-Komponenten**:
- **ProjectsComponent** (`frontend/src/app/features/projects/projects.component.ts`):
  - Datei-Upload für .knxproj-Dateien
  - Tabelle mit allen Projekten (Name, Dateiname, Import-Datum, Statistiken)
  - Projekt-Aktivierung per Slide-Toggle
  - Detailansicht mit Gruppenadressen und Geräten
  - Projekt-Löschung mit Bestätigung

**Datenmodell**:
```typescript
Project {
  Id: number
  Name: string
  FileName: string
  ImportDate: DateTime
  IsActive: boolean
  GroupAddresses: List<GroupAddress>
  Devices: List<Device>
}

GroupAddress {
  Id: number
  ProjectId: number
  Address: string          // Format: x/y/z
  Name: string
  Description: string
  DatapointType: string    // z.B. "DPT 1.001"
  CreatedAt: DateTime
}

Device {
  Id: number
  ProjectId: number
  Name: string
  PhysicalAddress: string  // Format: x.y.z
  Manufacturer: string
  ProductName: string
}
```

### 2. Gruppenadressen-Cache

**Zweck**: Performance-Optimierung durch In-Memory-Caching der Gruppenadressen des aktiven Projekts.

**Implementierung** (`backend/KnxMonitor.Infrastructure/Services/GroupAddressCacheService.cs`):
- **ConcurrentDictionary** für thread-safe Zugriff
- Lädt nur Gruppenadressen des aktiven Projekts
- Refresh bei Projektwechsel oder Löschung
- Singleton-Service, registriert in `Program.cs`

**Vorteile**:
- Keine wiederholten Datenbank-Abfragen bei Telegram-Verarbeitung
- Deutlich verbesserte Performance bei hoher Telegram-Frequenz
- Cache-Invalidierung nur bei Projektwechsel

### 3. KNX-Verbindung und Telegram-Empfang

**Backend-Komponenten**:
- **KnxConnectionService** (`backend/KnxMonitor.Infrastructure/KnxConnection/KnxConnectionService.cs`):
  - Verbindung zu KNX-Interface über Knx.Falcon.Sdk
  - Empfang von KNX-Telegrammen
  - Dekodierung mit DptConverter
  - Anreicherung mit Gruppenadressen-Namen aus Cache
  - Speicherung in Datenbank
  - Broadcasting über SignalR

- **DptConverter** (`backend/KnxMonitor.Infrastructure/KnxConnection/DptConverter.cs`):
  - Konvertierung von KNX-Rohdaten in lesbare Formate
  - Unterstützte DPT-Typen:
    - **DPT 1**: Boolean (On/Off)
    - **DPT 2**: 1-bit controlled
    - **DPT 3**: 3-bit controlled (Dimmen, Jalousien)
    - **DPT 5**: 8-bit unsigned (0-255, Prozent)
    - **DPT 6**: 8-bit signed (-128 bis 127)
    - **DPT 7**: 16-bit unsigned
    - **DPT 8**: 16-bit signed
    - **DPT 9**: 16-bit float (Temperatur, Helligkeit)
    - **DPT 10**: Zeit (HH:MM:SS)
    - **DPT 11**: Datum (DD.MM.YYYY)
    - **DPT 12**: 32-bit unsigned
    - **DPT 13**: 32-bit signed
    - **DPT 14**: 32-bit float (IEEE 754)
    - **DPT 16**: String (14 Zeichen)
    - **DPT 17**: Szenen-Nummer
    - **DPT 18**: Szenen-Steuerung
    - **DPT 19**: Datum/Zeit
    - **DPT 20**: 8-bit Enumeration
  - Fallback auf Hex-Darstellung bei unbekannten Typen

- **TelegramBroadcastService** (`backend/KnxMonitor.Api/Services/TelegramBroadcastService.cs`):
  - Hintergrund-Service für Telegram-Broadcasting
  - Verwendet SignalR TelegramHub
  - Entkopplung von KnxConnectionService

**SignalR-Hub**:
- **TelegramHub**: Echtzeit-Übertragung von Telegrammen an verbundene Clients

### 4. Live-View (Frontend)

**Komponente** (`frontend/src/app/features/live-view/live-view.component.ts`):

**Features**:
- **AG-Grid Integration**:
  - Hochperformante Tabellendarstellung
  - Virtual Scrolling für große Datenmengen
  - Spalten: Zeit, Quelle, Ziel, GA-Name, Typ, Wert, Rohdaten
  - Floating Filter für schnelle Suche
  - Sortierung und Filterung pro Spalte
  - Spaltenbreiten-Anpassung

- **Echtzeit-Updates**:
  - SignalR-Verbindung für Live-Telegramme
  - Automatische Grid-Aktualisierung
  - Pause-Funktion zum Anhalten der Updates
  - Auto-Scroll zur neuesten Zeile (deaktivierbar)

- **Farbcodierung** (Message-Type):
  - **Blau**: Read (Leseanfrage)
  - **Grün**: Write (Schreibbefehl)
  - **Orange**: Response (Antwort auf Read)

- **Steuerung**:
  - Verbinden/Trennen zur KNX-Verbindung
  - Pause/Resume für Live-Updates
  - Clear zum Löschen aller Telegramme
  - Quick-Filter für Textsuche über alle Spalten
  - Auto-Scroll Toggle

- **Tastatur-Shortcuts**:
  - `Space`: Pause/Resume
  - `/`: Fokus auf Quick-Filter

**Design**:
- Dark Theme optimiert für Monitoring
- Kompakte Zeilenhöhe (35px) für mehr Übersicht
- Responsive Layout
- Material Design Icons

### 5. Settings-Seite

**Komponente** (`frontend/src/app/features/settings/settings.ts`):
- KNX-Konfiguration (IP-Adresse, Port, Physikalische Adresse)
- Formular mit Validierung
- Speichern/Laden der Konfiguration
- Material Design UI

### 6. Datenbank-Schema

**Hauptentitäten**:
```sql
Projects
  - Id (PK)
  - Name
  - FileName
  - ImportDate
  - IsActive

GroupAddresses
  - Id (PK)
  - ProjectId (FK -> Projects)
  - Address
  - Name
  - Description
  - DatapointType
  - CreatedAt

Devices
  - Id (PK)
  - ProjectId (FK -> Projects)
  - Name
  - PhysicalAddress
  - Manufacturer
  - ProductName

KnxTelegrams
  - Id (PK)
  - Timestamp
  - SourceAddress
  - DestinationAddress
  - MessageType (Read/Write/Response)
  - RawValue
  - DecodedValue
  - GroupAddressName

KnxConfigurations
  - Id (PK)
  - IpAddress
  - Port
  - PhysicalAddress
```

**Migrations**:
- `20251025213540_AddIsActiveToProject`: Fügt `IsActive`-Flag zu Project-Entität hinzu

## Dependency Injection

**Backend** (`backend/KnxMonitor.Api/Program.cs`):
```csharp
// Services
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IProjectRepository, ProjectRepository>();
builder.Services.AddScoped<IGroupAddressRepository, GroupAddressRepository>();
builder.Services.AddScoped<IKnxProjectParserService, KnxProjectParserService>();

// Singleton Services
builder.Services.AddSingleton<IGroupAddressCacheService, GroupAddressCacheService>();
builder.Services.AddHostedService<TelegramBroadcastService>();
builder.Services.AddSingleton<KnxConnectionService>();
```

**Wichtig**:
- **Singleton**: KnxConnectionService, GroupAddressCacheService (Zustandsbehaftet)
- **Scoped**: Repositories, ProjectService (pro HTTP-Request)
- **HostedService**: TelegramBroadcastService (Hintergrund-Worker)

## API-Endpunkte

### Projekte
- `POST /api/projects/upload` - .knxproj-Datei hochladen
- `GET /api/projects` - Alle Projekte abrufen
- `GET /api/projects/{id}` - Projektdetails abrufen
- `PUT /api/projects/{id}/activate` - Projekt aktivieren
- `DELETE /api/projects/{id}` - Projekt löschen

### SignalR
- `/hubs/telegram` - WebSocket-Hub für Echtzeit-Telegramme
  - Event: `ReceiveTelegram` - Neues KNX-Telegram

## Frontend-Services

### SignalrService
**Pfad**: `frontend/src/app/core/services/signalr.service.ts`

**Funktionen**:
- Verbindungsmanagement zu SignalR-Hub
- Observable für eingehende Telegramme
- Automatische Reconnect-Logik
- Connection State Management

### ProjectService
**Pfad**: `frontend/src/app/core/services/project.service.ts`

**Funktionen**:
- HTTP-Calls zu Project-API
- Upload von .knxproj-Dateien (FormData)
- CRUD-Operationen für Projekte
- Typensichere DTOs

## Styling und Design

**Global Styles** (`frontend/src/styles.scss`):
- Dark Theme als Basis
- Material Design Palette
- Responsive Breakpoints
- AG-Grid Theme-Anpassungen

**Design-Prinzipien**:
- Monospace-Font für Adressen und Werte (bessere Lesbarkeit)
- Kompakte Darstellung für viele Daten
- Klare visuelle Hierarchie
- Konsistente Farbcodierung

## Performance-Optimierungen

1. **GroupAddressCacheService**: In-Memory-Cache statt wiederholter DB-Queries
2. **AG-Grid**: Virtual Scrolling für große Telegram-Listen
3. **SignalR**: Effiziente Echtzeit-Kommunikation ohne Polling
4. **OnPush Change Detection**: Optimierte Angular Change Detection
5. **Indexed DB-Queries**: Indizes auf `Timestamp`, `DestinationAddress`

## Entwicklungs-Workflow

### Backend starten
```bash
cd backend
dotnet restore
dotnet run --project KnxMonitor.Api
```

### Frontend starten
```bash
cd frontend
npm install
npm start
```

### Migration erstellen
```bash
cd backend
dotnet ef migrations add <MigrationName> \
  --project KnxMonitor.Infrastructure \
  --startup-project KnxMonitor.Api
```

### Datenbank aktualisieren
```bash
dotnet ef database update \
  --project KnxMonitor.Infrastructure \
  --startup-project KnxMonitor.Api
```

## Konfiguration

**Backend** (`appsettings.json`):
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=../data/knxmonitor.db"
  },
  "Jwt": {
    "SecretKey": "...",
    "Issuer": "knx-ng-monitor",
    "Audience": "knx-ng-monitor-client"
  }
}
```

**Frontend** (`environment.ts`):
```typescript
export const environment = {
  production: false,
  apiUrl: 'http://localhost:5000'
};
```

## Bekannte Offene Punkte

Aus `TODO.md`:

### Logging
- Backend: Integration von Serilog mit Console Sink und präzisem Template
- Backend: Aufräumen vorhandener Log-Statements
- Frontend: Aufräumen vorhandener Console-Logs

### Optimierungen
- DB-Zugriffe bei Mapping-Auflösungen sind implementiert (GroupAddressCacheService)

## Lizenz

[Lizenz-Information hier einfügen]

## Autor

Jörg

---

**Stand**: 26. Oktober 2025
