# KNX Project Parser: Library-Extraktion & Test-Infrastruktur
## Teil 1: Übersicht, Analyse & Architektur

**Version:** 1.0
**Erstellt:** 2025-10-31
**Status:** Ready for Implementation

---

## 1. Executive Summary

### 1.1 Zielsetzung

Extraktion des KNX-Project-Parsers aus der Infrastructure-Schicht in eine dedizierte, wiederverwendbare Library mit umfassender Test-Infrastruktur. Die Library wird die **einzige Parser-Implementation** sein - der alte Code wird nach erfolgreicher Migration vollständig entfernt.

**Kernziele:**
- ✅ **Eine einzige Parser-Implementation** - keine Code-Duplication
- ✅ **Standalone Library** - unabhängig von Infrastructure/EF Core
- ✅ **100% Wizard-Kompatibilität** - bestehende Funktionalität bleibt erhalten
- ✅ **Automatisierte Tests** - reproduzierbar mit allen Sample-Files
- ✅ **ETS 4, 5, 6 Support** - alle Versionen mit/ohne Passwort/Secure
- ✅ **Erweiterbare Architektur** - sauber für neue Features
- ✅ **CLI-Tool** - für manuelle Tests und Entwicklung

### 1.2 Haupt-Testfall: ETS5-Projekt

**Primäre Referenz:** Dein bekanntes ETS5-Projekt
**Warum:** Vollständiges Verständnis der Struktur, bekannte Erwartungen, produktive Nutzung

**Test-Strategie:**
1. **Phase 1:** ETS5-Projekt als erster Testfall (muss 100% funktionieren)
2. **Phase 2:** Weitere ETS5-Samples aus `docs/samples/`
3. **Phase 3:** ETS 4 und ETS 6 Varianten
4. **Phase 4:** Alle Kombinationen (PW, Secure, Addressing-Styles)

### 1.3 Erfolgs-Kriterien

**Funktional:**
- Dein ETS5-Projekt wird perfekt geparst (gleiche Results wie aktuell)
- Wizard funktioniert unverändert (keine Breaking Changes)
- Alle Sample-Files erfolgreich geparst (mind. 12 Files)
- ETS6-Devices-Bug gefixt

**Qualität:**
- Test-Coverage >80% für Library-Code
- Alle xUnit-Tests grün
- CLI Batch-Report ohne kritische Errors
- Dokumentierte Gaps vs. xknxproject

**Architektur:**
- Library standalone nutzbar (NuGet-ready)
- Alter Parser-Code vollständig entfernt
- Klare Extension-Points für neue Features
- Dependency Injection Support

### 1.4 Nicht-Ziele (Out of Scope)

- Frontend-Änderungen am Import-Wizard
- Breaking Changes an bestehenden APIs
- Sofortige Nutzung erweiterter Features (COs, Topology) im Wizard
- 100% Parity mit xknxproject (nur Gap-Analysis)

---

## 2. Aktuelle Situation & Analyse

### 2.1 Bestehende Parser-Implementation

**Location:** `backend/KnxMonitor.Infrastructure/Services/KnxProjectParserService.cs` (532 Zeilen)

**Dependencies:**
- Infrastructure-Layer (fest verdrahtet)
- Entity Framework Core (für Entities)
- Core-Layer (Interfaces, DTOs)

**Unterstützte Features:**
```csharp
public async Task<(List<GroupAddress>, List<Device>)> ParseProjectAsync(
    Stream fileStream,
    string? password = null,
    IProgress<ParserProgress>? progress = null
)
```

**Was funktioniert:**
- ✅ Group Addresses: Address (x/y/z), Name, Description, DatapointType
- ✅ Devices: Name, PhysicalAddress (x.y.z), Manufacturer, ProductName
- ✅ ETS 4, 5, 6 Basis-Detection (via Namespace/ToolVersion)
- ✅ Password-geschützte Projekte (Standard ZIP via SharpZipLib)
- ✅ KNX Secure Detection (Device-Level, HasKnxSecure-Flag)
- ✅ Hardware.xml-Parsing für Manufacturer-Info
- ✅ Progress-Callbacks (OpenZip, ParseGroupAddresses, ParseDevices)
- ✅ Address-Conversion (Integer → x/y/z bzw. x.y.z Format)

**Was NICHT funktioniert (bekannte Bugs):**
- ❌ **ETS6 Devices:** Segment-Tag zwischen Line und DeviceInstance wird nicht berücksichtigt
- ❌ **DPT-Parsing:** Nur String-Speicherung ("DPST-1-1"), keine Main/Sub-Nummer
- ❌ **Namespace-Robustheit:** Hartkodierte Namespace-Checks, nicht flexibel

**Was fehlt (Features):**
- ❌ Communication Objects (CO Flags, DPT-Arrays, GA-Links)
- ❌ Topology-Information (Areas → Lines mit Medium-Type)
- ❌ Locations (Building/Space-Hierarchie)
- ❌ Functions (Funktionale GA-Gruppierungen)
- ❌ Module-Instances (für komplexe Geräte)
- ❌ Channels mit GO-Links
- ❌ Text-Parameter-Substitution

**Wie es aktuell funktioniert:**
```
.knxproj (ZIP)
  ↓
ZipArchive.Open (Standard oder mit PW via SharpZipLib)
  ↓
Check for P-xxxx.zip (nested, password-protected)
  ├─ Ja → Extract inner ZIP mit Password
  └─ Nein → Use outer archive
  ↓
Find P-xxxx/0.xml (Projekt-Daten)
  ↓
Parse XML (auto-detect namespace)
  ├─ GroupAddress-Elemente → List<GroupAddress>
  │   ├─ Address (Integer → x/y/z Conversion)
  │   ├─ Name, Description
  │   └─ DatapointType (String, z.B. "DPST-1-1")
  └─ DeviceInstance-Elemente → List<Device>
      ├─ Address (Integer → x.y.z Conversion)
      ├─ Name
      └─ ProductRefId → Hardware.xml Lookup
          ├─ Manufacturer
          └─ ProductName
  ↓
Return (GroupAddresses, Devices)
```

### 2.2 ProjectFeatureDetector

**Location:** `backend/KnxMonitor.Infrastructure/Services/ProjectFeatureDetector.cs` (142 Zeilen)

**Aufgabe:** Pre-Scan des .knxproj Files ohne volles Parsing

**Detektiert:**
```csharp
public class FeatureInfo
{
    public EtsVersion EtsVersion { get; set; }      // 4, 5, 6
    public bool HasPassword { get; set; }           // P-xxxx.zip verschachtelt?
    public bool HasKnxSecure { get; set; }          // Security-Elemente vorhanden?
    public string? ProjectId { get; set; }          // P-xxxx
}
```

**Detection-Logik:**
- **ETS-Version:** ToolVersion-Attribut oder XML-Namespace
  - ETS 4: `ToolVersion="4.x"` oder `http://knx.org/xml/project/14`
  - ETS 5: `ToolVersion="5.x"` oder `http://knx.org/xml/project/20`
  - ETS 6: `ToolVersion="6.x"` oder `http://knx.org/xml/project/21`
- **Password:** Nested `P-xxxx.zip` im Root-Archive
- **KNX Secure:** `<Security>` Elemente in 0.xml

**Nutzung im Wizard:**
1. User wählt .knxproj-Datei
2. Feature-Detection läuft
3. Wizard zeigt Requirements-Dialog (falls PW/Keyring nötig)
4. User gibt Password ein → Parsing startet

### 2.3 Import-Wizard (darf NICHT brechen!)

**Location:** `frontend/src/app/features/projects/import-wizard/import-wizard.component.ts`

**Workflow (6 Steps):**

```typescript
enum ImportStep {
  FileSelection,     // Upload .knxproj
  Analyzing,         // ProjectFeatureDetector läuft
  Requirements,      // Falls nötig: PW, Keyring eingeben
  Importing,         // Eigentliches Parsing
  Complete           // Summary anzeigen
}
```

**Backend-Integration:**
```typescript
// 1. Feature-Detection
this.projectService.analyzeProject(formData).subscribe(features => {
  this.etsVersion = features.etsVersion;
  this.hasPassword = features.hasPassword;
  this.hasKnxSecure = features.hasKnxSecure;

  if (features.hasPassword) {
    this.currentStep = ImportStep.Requirements; // PW-Dialog
  } else {
    this.startImport(); // Direkt zum Parsing
  }
});

// 2. Parsing mit Progress
this.projectService.importProject(formData).subscribe({
  next: (progress: ImportProgress) => {
    // Progress-Events
    switch (progress.step) {
      case 'OpenZip': this.progress = progress.percent; break;
      case 'ParseGroupAddresses': this.progress = 20 + progress.percent * 0.4; break;
      case 'ParseDevices': this.progress = 60 + progress.percent * 0.4; break;
    }
  },
  complete: () => {
    this.currentStep = ImportStep.Complete;
    this.showSummary(result); // GAs, Devices, ETS-Version
  }
});
```

**API-Endpunkte (Backend):**
```csharp
// POST /api/projects/analyze
[HttpPost("analyze")]
public async Task<ActionResult<FeatureInfo>> AnalyzeProject([FromForm] IFormFile file)
{
    await using var stream = file.OpenReadStream();
    var features = await _featureDetector.DetectFeaturesAsync(stream);
    return Ok(features);
}

// POST /api/projects/import
[HttpPost("import")]
public async Task<ActionResult<ImportResult>> ImportProject([FromForm] ImportRequest request)
{
    // Parse mit Progress-Stream (SignalR oder SSE)
    var (groupAddresses, devices) = await _parserService.ParseProjectAsync(
        request.File.OpenReadStream(),
        request.Password,
        progress
    );

    // Save to DB
    await _projectService.SaveProjectAsync(groupAddresses, devices);

    return Ok(new ImportResult {
        GroupAddressCount = groupAddresses.Count,
        DeviceCount = devices.Count
    });
}
```

**ImportJob Entity:**
```csharp
public enum ImportJobStep
{
    UploadFile,
    OpenZip,
    DetectFeatures,
    CheckPassword,
    ParseGroupAddresses,
    ParseDevices,
    ParseSecurity,
    Validate,
    Save,
    RefreshCache
}

public class ImportJob
{
    public Guid Id { get; set; }
    public ImportJobStep CurrentStep { get; set; }
    public int PercentComplete { get; set; }
    public string? ErrorMessage { get; set; }
}
```

**Kritische Anforderungen:**
- ✅ Interface `IKnxProjectParserService` darf nicht brechen
- ✅ Progress-Events müssen weiterhin funktionieren
- ✅ FeatureInfo-DTO muss identisch bleiben
- ✅ ImportJobStep-Enum muss kompatibel bleiben

### 2.4 Vorhandene Sample-Files

**Location:** `D:\Source\knx-ng-monitor\docs\samples\`

```
docs/samples/
├── xknxproject/                # Python xknxproject Library Samples
│   ├── xknx_test_project.knxproj             # ETS5, PW="test", Secure
│   ├── test_project-ets4.knxproj             # ETS4, PW="test"
│   ├── test_project-ets4-no_password.knxproj # ETS4, kein PW
│   ├── testprojekt-ets6.knxproj              # ETS6, PW="test"
│   ├── testprojekt-ets6-functions.knxproj    # ETS6, Functions
│   ├── ets6_free.knxproj                     # ETS6, Free Addressing
│   ├── ets6_two_level.knxproj                # ETS6, TwoLevel Addressing
│   ├── module-definition-test.knxproj        # Module-Definitions
│   └── stubs/*.json                          # Reference-Outputs (xknxproject)
└── other/                      # Weitere Samples
    ├── 01, 03, 05 ...                        # Diverse ETS-Projekte
    └── KV (Knx Visu) Beispiele
```

**xknxproject JSON-Stubs:**
Enthalten geparsete Daten als Referenz:
```json
{
  "group_addresses": [
    {
      "identifier": "P-0501-0_GA-1",
      "name": "Schalten",
      "address": 1,
      "raw_address": 2049,
      "project_uid": 123,
      "dpt_main": 1,
      "dpt_sub": 1,
      "data_secure": false
    }
  ],
  "devices": [...],
  "communication_objects": [...],
  "topology": {...},
  "locations": [...],
  "functions": [...]
}
```

**Test-Matrix-Ziel:**
```
          │ Ohne PW  │ Mit PW    │ KNX Secure │ PW + Secure │
─────────────────────────────────────────────────────────────
ETS 4     │ ✓        │ ✓         │ N/A        │ N/A         │
ETS 5     │ ✓        │ ✓         │ ✓          │ ✓           │
ETS 6     │ ✓        │ ✓         │ ✓          │ ✓           │
```

### 2.5 xknxproject-Referenz-Implementation

**Location:** `D:\Source\xknxproject-main\` (parallel zum Projekt)

**Technologie:** Python 3 Library

**Architektur:**
```python
XKNXProj
├── ZIP-Extractor (pyzipper für AES, zipfile für Standard)
├── XML-Parser (ElementTree mit iterparse)
├── Loaders (modular):
│   ├── ProjectLoader (0.xml → Topology, GAs, Devices)
│   ├── HardwareLoader (Hardware.xml → Manufacturer)
│   ├── ApplicationProgramLoader (M-*/P-*.xml → COs)
│   └── KNXMasterLoader (knx_master.xml → Translations)
└── Combination (Cross-References auflösen, DPT-Inference)
```

**Was wir davon lernen können:**
1. **Modular Loader Pattern** - Separation of Concerns
2. **ETS-Version-spezifische Logik** - Strategy Pattern
3. **Segment-Tag-Handling** (ETS6) - Critical Fix!
4. **Module-Support** - Komplexe Geräte
5. **Text-Parameter-Substitution** - Template-Placeholders
6. **Snapshot-Testing** - JSON-Referenzen als Expected-Results

**Was wir NICHT übernehmen:**
- Python-spezifische Implementierungsdetails
- 100% Feature-Parity (Fokus auf unsere Bedürfnisse)
- AES-Encryption für ETS6 (noch nicht nötig, aktuell Standard-ZIP)

---

## 3. Ziel-Architektur

### 3.1 Übersicht

**Prinzip:** Eine einzige Parser-Library, alte Implementation wird gelöscht.

```
backend/
├── KnxMonitor.ProjectParser/              # 🆕 DIE Parser-Library (standalone)
│   ├── Core/                              # Interfaces, Models, Enums
│   ├── Loaders/                           # ETS-Version-spezifische Parser
│   ├── Services/                          # Orchestration, Helpers
│   └── Helpers/                           # Utils (DPT, Address, XML)
│
├── KnxMonitor.ProjectParser.Tests/        # 🆕 xUnit Tests
│   ├── Unit/                              # Loader-, Service-, Helper-Tests
│   ├── Integration/                       # Sample-File-Tests
│   ├── Fixtures/                          # Test-Daten
│   └── TestData/ExpectedResults/          # JSON-Expectations
│
├── KnxMonitor.ParserTool/                 # 🆕 CLI-Tool
│   ├── Commands/                          # parse, batch, compare, detect
│   ├── Output/                            # JSON, CSV, Console Formatters
│   └── Program.cs
│
├── KnxMonitor.Infrastructure/             # ✏️ Minimal angepasst
│   └── Services/
│       └── KnxProjectParserService.cs     # 🔄 Jetzt Adapter zur Library
│           (KEIN alter Parser-Code mehr, nur Delegation!)
│
├── KnxMonitor.Core/                       # ✅ Unverändert
│   └── Interfaces/
│       └── IKnxProjectParserService.cs    # API bleibt identisch
│
└── KnxMonitor.Api/                        # ✅ Unverändert
    └── Controllers/ProjectsController.cs  # Nutzt weiterhin IKnxProjectParserService
```

**Wichtig:** Nach erfolgreicher Migration gibt es NUR NOCH die Library-Implementation!

### 3.2 KnxMonitor.ProjectParser (Library-Details)

#### 3.2.1 Projektstruktur

```
KnxMonitor.ProjectParser/
├── Core/
│   ├── Interfaces/
│   │   ├── IProjectParser.cs               # Main Parser Interface
│   │   ├── IProjectLoader.cs               # Strategy: ETS-Version Loader
│   │   ├── IFeatureDetector.cs             # Feature-Detection
│   │   ├── IDataExtractor.cs               # Extension-Point (optional)
│   │   └── IProgressReporter.cs            # Progress-Events
│   │
│   ├── Models/
│   │   ├── ParseResult.cs                  # GAs + Devices (Wizard-kompatibel)
│   │   ├── ExtendedParseResult.cs          # + COs, Topology, Locations
│   │   ├── ProjectFeatures.cs              # ETS-Version, PW, Secure
│   │   ├── GroupAddress.cs                 # DTO für Group Address
│   │   ├── Device.cs                       # DTO für Device
│   │   ├── CommunicationObject.cs          # 🆕 CO mit Flags
│   │   ├── Topology.cs                     # 🆕 Areas → Lines → Devices
│   │   ├── Location.cs                     # 🆕 Building/Space
│   │   ├── Function.cs                     # 🆕 Functional Grouping
│   │   ├── ParserOptions.cs                # Password, Features, Filters
│   │   └── ParserProgress.cs               # Step, Percent, Message
│   │
│   └── Enums/
│       ├── EtsVersion.cs                   # Ets4, Ets5, Ets6
│       ├── ParseStep.cs                    # OpenZip, Parse*, Validate
│       ├── AddressingStyle.cs              # Free, TwoLevel, ThreeLevel
│       └── MediumType.cs                   # TP, IP, RF, PL
│
├── Loaders/
│   ├── BaseProjectLoader.cs                # Abstract base mit shared logic
│   ├── Ets4ProjectLoader.cs                # ETS4-spezifisch
│   ├── Ets5ProjectLoader.cs                # ETS5-spezifisch
│   ├── Ets6ProjectLoader.cs                # ETS6-spezifisch (Segment-Tag!)
│   ├── HardwareLoader.cs                   # Hardware.xml Parser
│   ├── ApplicationProgramLoader.cs         # 🆕 M-*/P-*.xml für COs
│   └── TopologyLoader.cs                   # 🆕 Areas/Lines extrahieren
│
├── Services/
│   ├── ProjectParser.cs                    # Main Orchestrator
│   ├── FeatureDetector.cs                  # Feature-Detection Service
│   ├── ZipHandler.cs                       # ZIP-Extraction mit PW-Support
│   ├── XmlHelper.cs                        # Namespace-Handling, XPath
│   └── ProgressAggregator.cs               # Progress von mehreren Loadern
│
└── Helpers/
    ├── AddressConverter.cs                 # Integer ↔ x/y/z, x.y.z
    ├── DptParser.cs                        # DPST-1-1 → Main=1, Sub=1
    └── NamespaceResolver.cs                # ETS-Version via Namespace
```

#### 3.2.2 Kern-Interfaces

**IProjectParser (Main Entry Point):**
```csharp
public interface IProjectParser
{
    /// <summary>
    /// Parse .knxproj file (Basis-Features: GAs, Devices)
    /// Kompatibel mit bestehendem Wizard
    /// </summary>
    Task<ParseResult> ParseAsync(
        Stream stream,
        ParserOptions options,
        IProgress<ParserProgress>? progress = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Parse .knxproj mit erweiterten Features (COs, Topology, etc.)
    /// Optional für zukünftige Nutzung
    /// </summary>
    Task<ExtendedParseResult> ParseExtendedAsync(
        Stream stream,
        ExtendedParserOptions options,
        IProgress<ParserProgress>? progress = null,
        CancellationToken cancellationToken = default
    );
}
```

**IProjectLoader (Strategy Pattern für ETS-Versionen):**
```csharp
public interface IProjectLoader
{
    /// <summary>
    /// Supported ETS version
    /// </summary>
    EtsVersion SupportedVersion { get; }

    /// <summary>
    /// Load project data from ZIP archive
    /// </summary>
    Task<ParseResult> LoadAsync(
        ZipArchive archive,
        ParserOptions options,
        IProgress<ParserProgress>? progress = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Check if loader can handle this project
    /// </summary>
    bool CanLoad(ProjectFeatures features);
}
```

**IFeatureDetector:**
```csharp
public interface IFeatureDetector
{
    /// <summary>
    /// Detect project features without full parsing
    /// Fast pre-scan for Wizard Requirements-Dialog
    /// </summary>
    Task<ProjectFeatures> DetectAsync(
        Stream stream,
        CancellationToken cancellationToken = default
    );
}
```

**IDataExtractor (Extension Point):**
```csharp
public interface IDataExtractor
{
    /// <summary>
    /// Extract custom data from project
    /// Allows plugins/extensions
    /// </summary>
    Task<object?> ExtractAsync(
        ZipArchive archive,
        ParseResult baseResult,
        CancellationToken cancellationToken = default
    );
}
```

#### 3.2.3 Kern-Models

**ParseResult (Wizard-kompatibel):**
```csharp
public class ParseResult
{
    public List<GroupAddress> GroupAddresses { get; set; } = new();
    public List<Device> Devices { get; set; } = new();
    public ProjectFeatures Features { get; set; } = new();
    public ParseStatistics Statistics { get; set; } = new();
}

public class GroupAddress
{
    public string Address { get; set; }        // "1/2/3"
    public int RawAddress { get; set; }        // Integer-Form
    public string Name { get; set; }
    public string? Description { get; set; }
    public DptInfo? DatapointType { get; set; }
    public bool DataSecure { get; set; }       // KNX Secure per GA
}

public class Device
{
    public string PhysicalAddress { get; set; }  // "1.2.3"
    public int RawAddress { get; set; }          // Integer-Form
    public string Name { get; set; }
    public string? Manufacturer { get; set; }
    public string? ProductName { get; set; }
    public string? ProductRefId { get; set; }
    public string? HardwareProgramRefId { get; set; }
}

public class DptInfo
{
    public int Main { get; set; }              // 1
    public int? Sub { get; set; }              // 1 (nullable)
    public string OriginalString { get; set; } // "DPST-1-1"

    public override string ToString() =>
        Sub.HasValue ? $"DPT {Main}.{Sub:D3}" : $"DPT {Main}";
}
```

**ExtendedParseResult (für zukünftige Features):**
```csharp
public class ExtendedParseResult : ParseResult
{
    public List<CommunicationObject> CommunicationObjects { get; set; } = new();
    public Topology? Topology { get; set; }
    public List<Location> Locations { get; set; } = new();
    public List<Function> Functions { get; set; } = new();
}

public class CommunicationObject
{
    public int Number { get; set; }
    public string Name { get; set; }
    public string? Text { get; set; }
    public string? FunctionText { get; set; }

    // Flags
    public bool ReadFlag { get; set; }
    public bool WriteFlag { get; set; }
    public bool CommunicationFlag { get; set; }
    public bool TransmitFlag { get; set; }
    public bool UpdateFlag { get; set; }
    public bool ReadOnInitFlag { get; set; }

    public List<DptInfo> DatapointTypes { get; set; } = new();
    public string? ObjectSize { get; set; }
    public List<string> LinkedGroupAddresses { get; set; } = new();
}

public class Topology
{
    public List<Area> Areas { get; set; } = new();
}

public class Area
{
    public int Address { get; set; }
    public string Name { get; set; }
    public List<Line> Lines { get; set; } = new();
}

public class Line
{
    public int Address { get; set; }
    public string Name { get; set; }
    public MediumType MediumType { get; set; }
    public List<Device> Devices { get; set; } = new();
}
```

**ParserOptions:**
```csharp
public class ParserOptions
{
    public string? Password { get; set; }
    public bool ValidateStructure { get; set; } = true;
    public bool IncludeEmptyDevices { get; set; } = false;
    public bool ThrowOnWarnings { get; set; } = false;
}

public class ExtendedParserOptions : ParserOptions
{
    public bool LoadCommunicationObjects { get; set; } = false;
    public bool LoadTopology { get; set; } = false;
    public bool LoadLocations { get; set; } = false;
    public bool LoadFunctions { get; set; } = false;
}
```

**ParserProgress:**
```csharp
public class ParserProgress
{
    public ParseStep Step { get; set; }
    public int PercentComplete { get; set; }  // 0-100
    public string? Message { get; set; }
    public int? ItemsProcessed { get; set; }
    public int? TotalItems { get; set; }
}

public enum ParseStep
{
    OpenZip = 1,
    CheckPassword = 2,
    DetectFeatures = 3,
    ParseGroupAddresses = 4,
    ParseDevices = 5,
    ParseCommunicationObjects = 6,
    ParseTopology = 7,
    ParseLocations = 8,
    ParseFunctions = 9,
    Validate = 10,
    Complete = 11
}
```

### 3.3 Kompatibilitäts-Layer (Adapter)

**Infrastructure-Adapter (dünn, nur Delegation!):**

```csharp
// KnxMonitor.Infrastructure/Services/KnxProjectParserService.cs
// KEIN Parser-Code mehr, nur Wrapper!

public class KnxProjectParserService : IKnxProjectParserService
{
    private readonly IProjectParser _parser;
    private readonly ILogger<KnxProjectParserService> _logger;

    public KnxProjectParserService(
        IProjectParser parser,
        ILogger<KnxProjectParserService> logger)
    {
        _parser = parser;
        _logger = logger;
    }

    // Bestehende API - delegiert an Library
    public async Task<(List<Core.Entities.GroupAddress>, List<Core.Entities.Device>)>
        ParseProjectAsync(
            Stream fileStream,
            string? password = null,
            IProgress<ParserProgress>? progress = null)
    {
        try
        {
            // Parse mit Library
            var result = await _parser.ParseAsync(
                fileStream,
                new ProjectParser.ParserOptions { Password = password },
                MapProgress(progress)
            );

            // Map Library-DTOs zu Infrastructure-Entities
            var groupAddresses = result.GroupAddresses
                .Select(MapToEntity)
                .ToList();

            var devices = result.Devices
                .Select(MapToEntity)
                .ToList();

            return (groupAddresses, devices);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse KNX project");
            throw;
        }
    }

    // Mapping-Helpers
    private Core.Entities.GroupAddress MapToEntity(ProjectParser.GroupAddress dto)
    {
        return new Core.Entities.GroupAddress
        {
            Address = dto.Address,
            Name = dto.Name,
            Description = dto.Description,
            DatapointType = dto.DatapointType?.OriginalString
        };
    }

    private Core.Entities.Device MapToEntity(ProjectParser.Device dto)
    {
        return new Core.Entities.Device
        {
            Name = dto.Name,
            PhysicalAddress = dto.PhysicalAddress,
            Manufacturer = dto.Manufacturer,
            ProductName = dto.ProductName
        };
    }

    private IProgress<ProjectParser.ParserProgress> MapProgress(
        IProgress<ParserProgress>? progress)
    {
        if (progress == null) return null;

        return new Progress<ProjectParser.ParserProgress>(p =>
        {
            progress.Report(new ParserProgress
            {
                Step = MapStep(p.Step),
                PercentComplete = p.PercentComplete,
                Message = p.Message
            });
        });
    }
}
```

**Dependency Injection Setup:**

```csharp
// Program.cs oder Startup.cs

// Library-Services registrieren
services.AddSingleton<IFeatureDetector, FeatureDetector>();
services.AddSingleton<IProjectLoader, Ets4ProjectLoader>();
services.AddSingleton<IProjectLoader, Ets5ProjectLoader>();
services.AddSingleton<IProjectLoader, Ets6ProjectLoader>();
services.AddSingleton<IProjectParser, ProjectParser>();

// Adapter für bestehende API
services.AddScoped<IKnxProjectParserService, KnxProjectParserService>();

// Optional: Logging
services.AddLogging(builder => {
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});
```

**Result:** Wizard-Code benötigt KEINE Änderungen!

---

## 4. Migration-Strategie (Kein Duplikat!)

### 4.1 Migrations-Phasen

**Phase 1: Library erstellen (parallel)**
- Neue Library-Projekt anlegen
- Interfaces definieren
- Models erstellen
- **Alter Parser läuft weiter** (noch keine Änderung)

**Phase 2: Parser-Code migrieren**
- Bestehenden Parser-Code in Library kopieren
- Refactoring für Library (EF-Dependencies entfernen)
- ETS-Version-spezifische Loader erstellen
- **Alter Parser läuft weiter** (Sicherheit)

**Phase 3: Adapter implementieren**
- Infrastructure-Adapter erstellen
- DI-Setup anpassen
- **Feature-Flag:** Umschaltbar zwischen alt/neu
- Parallele Tests (alter vs. neuer Parser)

**Phase 4: Validierung**
- Alle Sample-Files mit neuem Parser testen
- Wizard-Integration testen
- Performance-Vergleich
- **Noch beide Parser vorhanden**

**Phase 5: Cut-Over**
- Feature-Flag auf "Library" setzen
- Produktions-Tests
- **Wenn alles OK:**
  - ✅ Alter Parser-Code LÖSCHEN
  - ✅ Nur noch Library-Implementation
  - ✅ Kein Duplikat mehr!

### 4.2 Schrittweiser Übergang (Zero-Downtime)

```
Woche 1:
├─ Library-Grundgerüst erstellen
├─ Parser-Code migrieren
└─ Status: Alter Parser läuft (keine Auswirkung)

Woche 2:
├─ Adapter implementieren
├─ DI mit Feature-Flag
└─ Status: Beide Parser vorhanden, alt=default

Woche 3:
├─ Tests mit neuem Parser
├─ Wizard-Validation
├─ Feature-Flag auf "neu" setzen
└─ Status: Neuer Parser default, alter als Fallback

Woche 4:
├─ Production-Validation (1 Woche)
├─ Monitoring
└─ Wenn OK: ALTER PARSER LÖSCHEN
    Status: ✅ NUR NOCH LIBRARY!
```

### 4.3 Rollback-Plan

Falls Probleme auftreten während Validation:
1. Feature-Flag zurück auf "alt" setzen
2. Library hat keine Auswirkung
3. Alter Parser übernimmt wieder
4. Debugging der Library ohne Zeitdruck

**Nach erfolgreicher Validation:** Rollback nicht mehr möglich (alter Code gelöscht)

---

## 5. Test-Strategie

### 5.1 Test-Pyramide

```
                  ╱╲
                 ╱  ╲
                ╱ E2E╲         2 Tests  (Wizard-Integration)
               ╱──────╲
              ╱        ╲
             ╱   Intg   ╲      15 Tests (Sample-Files)
            ╱────────────╲
           ╱              ╲
          ╱      Unit      ╲   50 Tests (Loaders, Helpers)
         ╱──────────────────╲
```

### 5.2 Primärer Testfall: Dein ETS5-Projekt

**Phase 1: ETS5-Projekt als Referenz**

1. Aktuellen Parser mit deinem Projekt laufen lassen
2. Results als "Expected"-Baseline speichern (JSON)
3. Neuen Parser mit gleichem Projekt testen
4. Diff: Müssen identisch sein!

**Expected Result erstellen:**
```bash
# Mit altem Parser (vor Migration)
dotnet run --project ParserTool -- parse "dein-ets5-projekt.knxproj" \
    --password "dein-pw" \
    --output baseline/ets5-reference.json

# Später mit neuem Parser vergleichen
dotnet run --project ParserTool -- compare "dein-ets5-projekt.knxproj" \
    baseline/ets5-reference.json \
    --password "dein-pw" \
    --diff
```

**xUnit-Test:**
```csharp
[Fact]
public async Task ParseProject_YourEts5Project_MatchesBaseline()
{
    // Arrange
    var projectPath = "TestData/dein-ets5-projekt.knxproj";
    var expectedPath = "TestData/baseline/ets5-reference.json";
    var expected = LoadExpectedResult(expectedPath);

    // Act
    await using var stream = File.OpenRead(projectPath);
    var result = await _parser.ParseAsync(stream, new ParserOptions {
        Password = "dein-pw"
    });

    // Assert
    Assert.Equal(expected.GroupAddresses.Count, result.GroupAddresses.Count);
    Assert.Equal(expected.Devices.Count, result.Devices.Count);

    foreach (var expectedGa in expected.GroupAddresses)
    {
        var actualGa = result.GroupAddresses
            .FirstOrDefault(ga => ga.Address == expectedGa.Address);

        Assert.NotNull(actualGa);
        Assert.Equal(expectedGa.Name, actualGa.Name);
        Assert.Equal(expectedGa.DatapointType, actualGa.DatapointType);
    }

    // ... Devices analog
}
```

### 5.3 Alle Sample-Files (parametrisiert)

**SampleFileTests.cs:**
```csharp
public class SampleFileTests : IClassFixture<SampleFileFixture>
{
    private readonly IProjectParser _parser;

    [Theory]
    [MemberData(nameof(GetAllSampleFiles))]
    public async Task ParseProject_SampleFile_Success(SampleFileInfo sample)
    {
        // Arrange
        await using var stream = File.OpenRead(sample.FilePath);
        var options = new ParserOptions { Password = sample.Password };

        // Act
        var result = await _parser.ParseAsync(stream, options);

        // Assert - Basic Success
        Assert.NotNull(result);
        Assert.NotEmpty(result.GroupAddresses);
        Assert.Equal(sample.ExpectedEtsVersion, result.Features.EtsVersion);

        // Optional: Compare with xknxproject stub if available
        if (sample.HasReferenceStub)
        {
            var expected = LoadXknxprojectStub(sample.StubPath);
            AssertGroupAddressesMatch(expected, result);
            AssertDevicesMatch(expected, result);
        }
    }

    public static IEnumerable<object[]> GetAllSampleFiles()
    {
        // ETS 5 (Haupt-Testfälle zuerst!)
        yield return new object[] { new SampleFileInfo {
            Name = "DEIN ETS5 Projekt (Hauptreferenz)",
            FilePath = "TestData/dein-ets5-projekt.knxproj",
            Password = "dein-pw",
            ExpectedEtsVersion = EtsVersion.Ets5,
            HasReferenceStub = true,
            StubPath = "TestData/baseline/ets5-reference.json"
        }};

        yield return new object[] { new SampleFileInfo {
            Name = "xknxproject ETS5 Test Project",
            FilePath = "docs/samples/xknxproject/xknx_test_project.knxproj",
            Password = "test",
            ExpectedEtsVersion = EtsVersion.Ets5,
            HasReferenceStub = true,
            StubPath = "docs/samples/xknxproject/stubs/xknx_test_project.json"
        }};

        // ETS 4
        yield return new object[] { new SampleFileInfo {
            Name = "ETS4 with Password",
            FilePath = "docs/samples/xknxproject/test_project-ets4.knxproj",
            Password = "test",
            ExpectedEtsVersion = EtsVersion.Ets4,
            HasReferenceStub = true
        }};

        yield return new object[] { new SampleFileInfo {
            Name = "ETS4 without Password",
            FilePath = "docs/samples/xknxproject/test_project-ets4-no_password.knxproj",
            Password = null,
            ExpectedEtsVersion = EtsVersion.Ets4,
            HasReferenceStub = false
        }};

        // ETS 6
        yield return new object[] { new SampleFileInfo {
            Name = "ETS6 Standard",
            FilePath = "docs/samples/xknxproject/testprojekt-ets6.knxproj",
            Password = "test",
            ExpectedEtsVersion = EtsVersion.Ets6,
            HasReferenceStub = true
        }};

        yield return new object[] { new SampleFileInfo {
            Name = "ETS6 Free Addressing",
            FilePath = "docs/samples/xknxproject/ets6_free.knxproj",
            Password = null,
            ExpectedEtsVersion = EtsVersion.Ets6,
            HasReferenceStub = false
        }};

        yield return new object[] { new SampleFileInfo {
            Name = "ETS6 TwoLevel Addressing",
            FilePath = "docs/samples/xknxproject/ets6_two_level.knxproj",
            Password = null,
            ExpectedEtsVersion = EtsVersion.Ets6,
            HasReferenceStub = false
        }};

        // ... weitere Files aus docs/samples/other/
    }
}
```

### 5.4 Unit-Tests für kritische Komponenten

**Ets6ProjectLoader (Segment-Tag-Fix):**
```csharp
[Fact]
public async Task LoadAsync_Ets6WithSegmentTag_ParsesDevices()
{
    // Arrange: Minimales ETS6-XML mit Segment
    var xmlContent = CreateMinimalEts6Xml(withSegmentTag: true);
    var loader = new Ets6ProjectLoader();

    // Act
    var devices = await loader.ParseDevicesFromXml(xmlContent);

    // Assert
    Assert.Single(devices);
    Assert.Equal("TestDevice", devices[0].Name);
    Assert.Equal("1.1.1", devices[0].PhysicalAddress);
}
```

**DptParser:**
```csharp
[Theory]
[InlineData("DPST-1-1", 1, 1)]
[InlineData("DPST-5-001", 5, 1)]
[InlineData("DPT-9", 9, null)]
[InlineData("", 0, null)]
public void Parse_ValidDptString_ReturnsCorrectInfo(
    string input, int expectedMain, int? expectedSub)
{
    var result = DptParser.Parse(input);
    Assert.Equal(expectedMain, result.Main);
    Assert.Equal(expectedSub, result.Sub);
}
```

**AddressConverter:**
```csharp
[Theory]
[InlineData(2049, "1/0/1")]       // (1 << 11) | (0 << 8) | 1
[InlineData(6657, "3/2/1")]       // (3 << 11) | (2 << 8) | 1
public void ToGroupAddressString_ValidInt_ReturnsCorrectFormat(
    int rawAddress, string expected)
{
    var result = AddressConverter.ToGroupAddressString(rawAddress);
    Assert.Equal(expected, result);
}
```

### 5.5 Wizard-Kompatibilitäts-Tests

```csharp
[Fact]
public async Task InfrastructureAdapter_DelegatesToLibrary_MapsCorrectly()
{
    // Arrange: Mock Library-Parser
    var mockParser = new Mock<IProjectParser>();
    mockParser.Setup(p => p.ParseAsync(
        It.IsAny<Stream>(),
        It.IsAny<ParserOptions>(),
        It.IsAny<IProgress<ProjectParser.ParserProgress>>(),
        It.IsAny<CancellationToken>()
    )).ReturnsAsync(new ParseResult {
        GroupAddresses = new List<ProjectParser.GroupAddress> {
            new() { Address = "1/2/3", Name = "Test GA" }
        },
        Devices = new List<ProjectParser.Device> {
            new() { PhysicalAddress = "1.1.1", Name = "Test Device" }
        }
    });

    var adapter = new KnxProjectParserService(mockParser.Object, _logger);

    // Act
    var (gas, devices) = await adapter.ParseProjectAsync(
        Stream.Null,
        password: "test"
    );

    // Assert: Typen sind Infrastructure-Entities
    Assert.IsType<List<Infrastructure.Entities.GroupAddress>>(gas);
    Assert.IsType<List<Infrastructure.Entities.Device>>(devices);
    Assert.Single(gas);
    Assert.Equal("1/2/3", gas[0].Address);
}

[Fact]
public async Task ProgressEvents_MappedCorrectly_FromLibraryToInfrastructure()
{
    // Arrange
    var receivedSteps = new List<string>();
    var progress = new Progress<Infrastructure.ParserProgress>(p =>
        receivedSteps.Add(p.Step.ToString())
    );

    // Act
    await _adapter.ParseProjectAsync(
        TestHelper.GetSampleStream("xknx_test_project.knxproj"),
        password: "test",
        progress: progress
    );

    // Assert: Alle erwarteten Steps wurden gemeldet
    Assert.Contains("OpenZip", receivedSteps);
    Assert.Contains("ParseGroupAddresses", receivedSteps);
    Assert.Contains("ParseDevices", receivedSteps);
}
```

---

**Ende Teil 1**

Fortsetzung in Teil 2: Implementierungsphasen, CLI-Tool, Zeitplan, Risiken
