# KNX-NG-Monitor - Technische Dokumentation

> **Stand: 2026-04-13 (v0.1.0).** Diese Datei beschreibt den aktuellen Architekturstand. Die ursprüngliche
> Skizze vom 26.10.2025 ist stark erweitert worden — insbesondere wurde der Parser in eine eigenständige
> Library `KnxMonitor.ProjectParser` ausgelagert, KNX Secure (Password + `.knxkeys` Keyring) End-to-End
> implementiert und der Import-Wizard auf einen zweistufigen Flow umgestellt. Die Sammelübersicht der
> Phasen liegt in [`PROJECT_PLAN.md`](PROJECT_PLAN.md), die Implementierungs-Detailgeschichte in
> [`../PARSER_LIBRARY_PROGRESS.md`](../PARSER_LIBRARY_PROGRESS.md).

## Projektübersicht

KNX-NG-Monitor ist ein modernes KNX-Bus-Monitoring-Tool mit Webinterface zur Echtzeitüberwachung, Historisierung und Visualisierung von KNX-Telegrammen. Die Anwendung besteht aus einem .NET 9 Backend (in mehrere Projekte aufgeteilt) und einem Angular Frontend.

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

### Backend (Clean Architecture, mehrere Projekte)

```
backend/
├── KnxMonitor.Api/                   # ASP.NET Core Web API
│   ├── Controllers/                   # AuthController, ProjectsController, KnxController, ...
│   ├── Hubs/TelegramHub.cs            # SignalR (JWT-authentifiziert)
│   ├── Services/TelegramBroadcastService.cs  # Hosted Service, Bridge KnxConnection → SignalR
│   └── Program.cs                     # DI, JWT, Static Files, Migrations on startup
│
├── KnxMonitor.Core/                  # Domain Layer (keine Abhängigkeiten)
│   ├── Entities/                      # Project, GroupAddress, Device, KnxTelegram,
│   │                                  # KnxConfiguration, User, RefreshToken
│   ├── DTOs/                          # ProjectDto, ImportRequirementDto (inkl. IsOptional), ...
│   └── Interfaces/                    # IProjectService, IKnxProjectParserService,
│                                      # IProjectFeatureDetector, IImportJobManager, ...
│
├── KnxMonitor.Infrastructure/        # Implementations
│   ├── Data/                          # ApplicationDbContext + Migrations
│   ├── KnxConnection/                 # KnxConnectionService (Knx.Falcon.Sdk), DptConverter
│   ├── Repositories/                  # ProjectRepository, GroupAddressRepository, ...
│   └── Services/
│       ├── ProjectService.cs           # CRUD + ActivateProject
│       ├── ProjectImportService.cs     # Wizard state machine, two-stage flow, auto-activate
│       ├── ProjectFeatureDetector.cs   # Pre-scan vor dem Library-Detector (für den Wizard)
│       ├── KnxProjectParserService.cs  # **Adapter** zum ProjectParser-Library (kein Parser-Code mehr!)
│       ├── GroupAddressCacheService.cs # ConcurrentDictionary, refresh on project switch
│       └── ImportJobManager.cs         # In-Memory Job-Status, Requirements, Steps
│
├── KnxMonitor.ProjectParser/         # Standalone-Library — die einzige Parser-Implementierung
│   ├── Core/Interfaces/                # IProjectParser, IProjectLoader, IFeatureDetector, IKeyringReader
│   ├── Core/Models/                    # ParserOptions (incl. KeyringStream/-Password),
│   │                                   # ParseResult (incl. Keyring), ProjectFeatures,
│   │                                   # ProjectFileMap (in-memory file map),
│   │                                   # KeyringData / KeyringDevice / KeyringGroupAddress
│   ├── Core/Enums/                     # EtsVersion, AddressingStyle, ParseStep, MediumType
│   ├── Helpers/AddressConverter.cs     # ThreeLevel / TwoLevel / Free
│   ├── Helpers/KeyringCrypto.cs        # PBKDF2-HMAC-SHA256 + AES-128-CBC + ExtractPasswordString
│   ├── Loaders/                        # BaseProjectLoader + Ets4/5/6 Loader
│   └── Services/                       # ProjectParser, FeatureDetector, ZipHandler, KeyringReader
│
├── KnxMonitor.ProjectParser.Tests/   # xUnit (187 Tests, ~99% Line Coverage)
│   ├── Fixtures/                       # SampleFileFixture, TestSamples (Skip-Helper), TestZipBuilder
│   ├── Integration/                    # Ets4/5/6 ProjectTests, KeyringTests
│   └── Unit/                           # Helpers, Services, Loaders, Models
│
└── KnxMonitor.ParserTool/            # CLI: parse / detect, --keyring etc.
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
  - **Reiner Adapter** seit v0.1.0 — delegiert zur Library `KnxMonitor.ProjectParser`.
  - Reicht Project-Password und (optional) Keyring-Stream + Keyring-Password aus dem `ImportContext`
    durch zur Library und mappt das `ParseResult` der Library auf die Infrastructure-Entities
    (`GroupAddress`, `Device`).

- **`KnxMonitor.ProjectParser` Library** (Hauptkomponente für ETS-Parsing):
  - `IProjectParser.ParseAsync(stream, options)` ist der Einstiegspunkt
  - `ZipHandler.LoadAsync` öffnet das äußere ZIP, extrahiert für password-protected
    Projekte das innere `P-XXXX.zip` (für ETS6 mit
    `Convert.ToBase64String(PBKDF2-HMAC-SHA256(UTF-16-LE-pw, salt="21.project.ets.knx.org", 65536, 32))`
    als ZIP-Password) und liefert eine `ProjectFileMap` (in-memory `Dictionary<string, byte[]>`)
  - Drei `IProjectLoader`-Implementierungen für ETS 4 / 5 / 6 (ETS 6 berücksichtigt das
    `<Line>/<Segment>/<DeviceInstance>`-Nesting)
  - `KeyringReader` liest `.knxkeys`-Dateien mit PBKDF2-HMAC-SHA256(UTF-8-pw,
    salt="1.keyring.ets.knx.org", 65536, 16) als Schlüssel, IV = `SHA-256(Created)[:16]`,
    AES-128-CBC pro Wert. Strings (ManagementPassword, Authentication) werden über
    `KeyringCrypto.ExtractPasswordString` ent-padded.
  - `IFeatureDetector.DetectAfterUnlockAsync` ermöglicht den 2-stufigen Wizard-Flow:
    Pre-Detection → Password → Re-Detection im entschlüsselten 0.xml → ggf. Keyring-Step

- **ProjectImportService** (`backend/KnxMonitor.Infrastructure/Services/ProjectImportService.cs`):
  - Hält die Wizard-State-Machine (Status / Requirements / Steps in `IImportJobManager`)
  - Two-stage Flow für KNX Secure: nach Erfüllen des `ProjectPassword`-Requirements wird
    `IFeatureDetector.DetectAfterUnlockAsync` aufgerufen; bei detektiertem KNX Secure werden
    `KeyringFile` + `KeyringPassword` als **optionale** Requirements ergänzt
  - Auto-Activate: das erste importierte Projekt wird sofort aktiv geschaltet

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

**Backend** (`backend/KnxMonitor.Api/Program.cs`, gekürzt):
```csharp
// Repositories + Project-Services
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IProjectRepository, ProjectRepository>();
builder.Services.AddScoped<IGroupAddressRepository, GroupAddressRepository>();

// Parser-Library (Singleton, weil zustandslos)
builder.Services.AddSingleton<IFeatureDetector, FeatureDetector>();
builder.Services.AddSingleton<IKeyringReader, KeyringReader>();
builder.Services.AddSingleton<IProjectLoader, Ets4ProjectLoader>();
builder.Services.AddSingleton<IProjectLoader, Ets5ProjectLoader>();
builder.Services.AddSingleton<IProjectLoader, Ets6ProjectLoader>();
builder.Services.AddSingleton<IProjectParser, ProjectParser>();

// Adapter zur Library
builder.Services.AddScoped<IKnxProjectParserService, KnxProjectParserService>();

// Import-Wizard + Cache + KNX-Bus
builder.Services.AddSingleton<IImportJobManager, ImportJobManager>();
builder.Services.AddScoped<IProjectFeatureDetector, ProjectFeatureDetector>();
builder.Services.AddScoped<ProjectImportService>();
builder.Services.AddSingleton<IGroupAddressCacheService, GroupAddressCacheService>();
builder.Services.AddSingleton<IKnxConnectionService, KnxConnectionService>();
builder.Services.AddHostedService<TelegramBroadcastService>();
```

**Wichtig**:
- **Singleton**: Library-Services (zustandslos), `KnxConnectionService`,
  `GroupAddressCacheService`, `ImportJobManager`
- **Scoped**: Repositories, `ProjectService`, `KnxProjectParserService`-Adapter,
  `ProjectImportService`, `ProjectFeatureDetector`
- **HostedService**: `TelegramBroadcastService`

`IKnxSecureService` und seine Implementierung `KnxSecureService` (alter Versuch
auf Infrastructure-Ebene das Keyring als verschlüsseltes ZIP zu lesen) wurden
mit v0.1.0 entfernt — der Keyring wird jetzt vollständig in der Library
behandelt.

## API-Endpunkte

### Auth
- `POST /api/auth/login` / `POST /api/auth/refresh` / `POST /api/auth/logout`

### Projekte
- `POST /api/projects/import` - .knxproj-Datei hochladen (multipart) → liefert `ImportJobDto` zurück
- `GET /api/projects/import-status/{jobId}` - Polling-Status für den Wizard
- `POST /api/projects/import/{jobId}/provide-input` - Password / Keyring nachreichen
- `GET /api/projects` - Alle Projekte abrufen
- `GET /api/projects/{id}` - Projektdetails abrufen
- `PUT /api/projects/{id}/activate` - Projekt aktivieren
- `DELETE /api/projects/{id}` - Projekt löschen

### SignalR
- `/hubs/telegram` - WebSocket-Hub für Echtzeit-Telegramme (JWT-authentifiziert)
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

Konsolidierter Stand siehe [`PROJECT_PLAN.md`](PROJECT_PLAN.md) → "Nächste Schritte". Highlights:

### Parser / KNX Secure
- Telegramm-Decryption zur Laufzeit mit den abgespeicherten ToolKeys / GA-Keys (KNX Data Secure am Bus)
- Communication Objects, Topology, Locations, Functions noch nicht geparst
- Frontend: Keyring-Datei nachträglich pro Projekt hochladen / aktualisieren

### Logging
- Backend: Serilog-Templates vereinheitlichen
- `Console.WriteLine`-Statements aus `ProjectImportService` durch ILogger ersetzen
- Frontend: Aufräumen vorhandener Console-Logs

### Tests / Qualität
- API-Integration- und E2E-Tests (Cypress/Playwright)
- Frontend Unit-Test-Coverage ausbauen
- Health-Check-Endpoint
- Mobile-Ansicht systematisch optimieren, Accessibility-Audit

## Lizenz

MIT

## Autor

Jörg

---

**Stand**: 13. April 2026 (v0.1.0)
