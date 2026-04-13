# KNX Project Parser: Library-Extraktion & Test-Infrastruktur
## Teil 2: Implementierung, CLI-Tool, Testing & Zeitplan

**Version:** 1.0
**Erstellt:** 2025-10-31
**Status:** Ready for Implementation

**Siehe auch:** `PARSER_LIBRARY_PLAN_PART1.md` für Übersicht, Analyse und Architektur

---

## 6. Implementierungs-Phasen (Detailliert)

### Phase 1: Library-Grundgerüst (Tag 1-2)

**Ziel:** Kompilierbares Library-Projekt mit Basis-Struktur

#### 1.1 Projekt erstellen

```bash
cd D:\Source\knx-ng-monitor\backend

# Library erstellen
dotnet new classlib -n KnxMonitor.ProjectParser -f net9.0
dotnet sln add KnxMonitor.ProjectParser/

# Test-Projekt erstellen
dotnet new xunit -n KnxMonitor.ProjectParser.Tests -f net9.0
dotnet sln add KnxMonitor.ProjectParser.Tests/
dotnet add KnxMonitor.ProjectParser.Tests/ reference KnxMonitor.ProjectParser/

# CLI-Tool erstellen
dotnet new console -n KnxMonitor.ParserTool -f net9.0
dotnet sln add KnxMonitor.ParserTool/
dotnet add KnxMonitor.ParserTool/ reference KnxMonitor.ProjectParser/
```

#### 1.2 Dependencies hinzufügen

```bash
# Library
cd KnxMonitor.ProjectParser
dotnet add package System.IO.Compression --version 9.0.0
dotnet add package ICSharpCode.SharpZipLib --version 1.4.2
dotnet add package Microsoft.Extensions.Logging.Abstractions --version 9.0.0

# Test-Projekt
cd ../KnxMonitor.ProjectParser.Tests
dotnet add package xunit --version 2.9.0
dotnet add package xunit.runner.visualstudio --version 2.8.2
dotnet add package FluentAssertions --version 6.12.0
dotnet add package Moq --version 4.20.70

# CLI-Tool
cd ../KnxMonitor.ParserTool
dotnet add package System.CommandLine --version 2.0.0-beta4.22272.1
dotnet add package Microsoft.Extensions.Logging.Console --version 9.0.0
```

#### 1.3 Ordnerstruktur erstellen

```bash
cd KnxMonitor.ProjectParser

mkdir Core
mkdir Core/Interfaces
mkdir Core/Models
mkdir Core/Enums
mkdir Loaders
mkdir Services
mkdir Helpers
```

#### 1.4 Basis-Interfaces definieren

**Core/Interfaces/IProjectParser.cs:**
```csharp
namespace KnxMonitor.ProjectParser.Core.Interfaces;

public interface IProjectParser
{
    Task<Models.ParseResult> ParseAsync(
        Stream stream,
        Models.ParserOptions options,
        IProgress<Models.ParserProgress>? progress = null,
        CancellationToken cancellationToken = default
    );

    Task<Models.ExtendedParseResult> ParseExtendedAsync(
        Stream stream,
        Models.ExtendedParserOptions options,
        IProgress<Models.ParserProgress>? progress = null,
        CancellationToken cancellationToken = default
    );
}
```

**Core/Interfaces/IProjectLoader.cs:**
```csharp
namespace KnxMonitor.ProjectParser.Core.Interfaces;

public interface IProjectLoader
{
    Enums.EtsVersion SupportedVersion { get; }

    Task<Models.ParseResult> LoadAsync(
        System.IO.Compression.ZipArchive archive,
        Models.ParserOptions options,
        IProgress<Models.ParserProgress>? progress = null,
        CancellationToken cancellationToken = default
    );

    bool CanLoad(Models.ProjectFeatures features);
}
```

**Core/Interfaces/IFeatureDetector.cs:**
```csharp
namespace KnxMonitor.ProjectParser.Core.Interfaces;

public interface IFeatureDetector
{
    Task<Models.ProjectFeatures> DetectAsync(
        Stream stream,
        CancellationToken cancellationToken = default
    );
}
```

#### 1.5 Basis-Models erstellen

**Core/Models/ParseResult.cs:**
```csharp
namespace KnxMonitor.ProjectParser.Core.Models;

public class ParseResult
{
    public List<GroupAddress> GroupAddresses { get; set; } = new();
    public List<Device> Devices { get; set; } = new();
    public ProjectFeatures Features { get; set; } = new();
    public ParseStatistics Statistics { get; set; } = new();
}

public class ExtendedParseResult : ParseResult
{
    public List<CommunicationObject> CommunicationObjects { get; set; } = new();
    public Topology? Topology { get; set; }
    public List<Location> Locations { get; set; } = new();
    public List<Function> Functions { get; set; } = new();
}

public class ParseStatistics
{
    public TimeSpan Duration { get; set; }
    public int GroupAddressCount { get; set; }
    public int DeviceCount { get; set; }
    public List<string> Warnings { get; set; } = new();
}
```

**Core/Models/GroupAddress.cs:**
```csharp
namespace KnxMonitor.ProjectParser.Core.Models;

public class GroupAddress
{
    public string Address { get; set; } = string.Empty;  // "1/2/3"
    public int RawAddress { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DptInfo? DatapointType { get; set; }
    public bool DataSecure { get; set; }
    public string? Identifier { get; set; }  // Internal ETS ID
}
```

**Core/Models/Device.cs:**
```csharp
namespace KnxMonitor.ProjectParser.Core.Models;

public class Device
{
    public string PhysicalAddress { get; set; } = string.Empty;  // "1.1.1"
    public int RawAddress { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? ProductName { get; set; }
    public string? ProductRefId { get; set; }
    public string? HardwareProgramRefId { get; set; }
    public string? ApplicationProgramRefId { get; set; }
    public bool HasKnxSecure { get; set; }
}
```

**Core/Models/DptInfo.cs:**
```csharp
namespace KnxMonitor.ProjectParser.Core.Models;

public class DptInfo
{
    public int Main { get; set; }
    public int? Sub { get; set; }
    public string OriginalString { get; set; } = string.Empty;

    public override string ToString() =>
        Sub.HasValue ? $"DPT {Main}.{Sub:D3}" : $"DPT {Main}";

    public static DptInfo? TryParse(string? dptString)
    {
        if (string.IsNullOrWhiteSpace(dptString))
            return null;

        // Implementierung siehe Phase 6
        return new DptInfo { OriginalString = dptString };
    }
}
```

#### 1.6 Enums definieren

**Core/Enums/EtsVersion.cs:**
```csharp
namespace KnxMonitor.ProjectParser.Core.Enums;

public enum EtsVersion
{
    Unknown = 0,
    Ets4 = 4,
    Ets5 = 5,
    Ets6 = 6
}
```

**Core/Enums/ParseStep.cs:**
```csharp
namespace KnxMonitor.ProjectParser.Core.Enums;

public enum ParseStep
{
    OpenZip = 1,
    CheckPassword = 2,
    DetectFeatures = 3,
    ParseGroupAddresses = 4,
    ParseDevices = 5,
    ParseCommunicationObjects = 6,
    ParseTopology = 7,
    Validate = 10,
    Complete = 11
}
```

**Deliverable:** ✅ Kompilierbares Library-Projekt mit Basis-Struktur

---

### Phase 2: Parser-Code migrieren (Tag 3-4)

**Ziel:** Bestehenden Parser-Code in Library überführen, Feature-Parity erreichen

#### 2.1 Bestehenden Code analysieren

Aktueller Code-Umfang:
- `KnxProjectParserService.cs`: 532 Zeilen
- `ProjectFeatureDetector.cs`: 142 Zeilen
- Gesamt: ~674 Zeilen zu migrieren

#### 2.2 FeatureDetector migrieren

**Services/FeatureDetector.cs:**
```csharp
namespace KnxMonitor.ProjectParser.Services;

public class FeatureDetector : IFeatureDetector
{
    private readonly ILogger<FeatureDetector> _logger;

    public FeatureDetector(ILogger<FeatureDetector> logger)
    {
        _logger = logger;
    }

    public async Task<ProjectFeatures> DetectAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var features = new ProjectFeatures();

        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

            // 1. Find Project ID (P-xxxx)
            var projectEntry = archive.Entries
                .FirstOrDefault(e => e.Name.EndsWith(".signature") && e.Name.StartsWith("P-"));

            if (projectEntry != null)
            {
                features.ProjectId = Path.GetFileNameWithoutExtension(projectEntry.Name);
            }

            // 2. Check for Password (nested P-xxxx.zip)
            var nestedZip = archive.Entries
                .FirstOrDefault(e => e.FullName.EndsWith(".zip") && e.FullName.StartsWith("P-"));
            features.HasPassword = nestedZip != null;

            // 3. Detect ETS Version from knx_master.xml or project.xml
            features.EtsVersion = await DetectEtsVersionAsync(archive);

            // 4. Check for KNX Secure
            features.HasKnxSecure = await CheckKnxSecureAsync(archive, features);

            _logger.LogInformation(
                "Detected: ETS {Version}, Password={HasPw}, Secure={HasSecure}",
                features.EtsVersion, features.HasPassword, features.HasKnxSecure
            );

            return features;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect features");
            throw;
        }
    }

    private async Task<EtsVersion> DetectEtsVersionAsync(ZipArchive archive)
    {
        // Versuche knx_master.xml zu finden
        var knxMasterEntry = archive.Entries
            .FirstOrDefault(e => e.Name.Equals("knx_master.xml", StringComparison.OrdinalIgnoreCase));

        if (knxMasterEntry != null)
        {
            await using var stream = knxMasterEntry.Open();
            using var reader = new StreamReader(stream);

            // Lese erste Zeilen für Namespace-Detection
            for (int i = 0; i < 5; i++)
            {
                var line = await reader.ReadLineAsync();
                if (line == null) break;

                if (line.Contains("http://knx.org/xml/project/14"))
                    return EtsVersion.Ets4;
                if (line.Contains("http://knx.org/xml/project/20"))
                    return EtsVersion.Ets5;
                if (line.Contains("http://knx.org/xml/project/21"))
                    return EtsVersion.Ets6;
            }
        }

        // Fallback: Versuche project.xml und ToolVersion-Attribut
        var projectEntry = archive.Entries
            .FirstOrDefault(e => e.Name.Equals("project.xml", StringComparison.OrdinalIgnoreCase) ||
                                 e.Name.Equals("Project.xml", StringComparison.OrdinalIgnoreCase));

        if (projectEntry != null)
        {
            await using var stream = projectEntry.Open();
            var xml = await XDocument.LoadAsync(stream, LoadOptions.None, default);
            var toolVersion = xml.Root?.Attribute("ToolVersion")?.Value;

            if (toolVersion != null)
            {
                if (toolVersion.StartsWith("4.")) return EtsVersion.Ets4;
                if (toolVersion.StartsWith("5.")) return EtsVersion.Ets5;
                if (toolVersion.StartsWith("6.")) return EtsVersion.Ets6;
            }
        }

        return EtsVersion.Unknown;
    }

    private async Task<bool> CheckKnxSecureAsync(
        ZipArchive archive,
        ProjectFeatures features)
    {
        // Finde 0.xml im Projekt
        var projectFile = $"{features.ProjectId}/0.xml";
        var entry = archive.GetEntry(projectFile);

        if (entry == null)
            return false;

        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        // Schnelle String-Suche nach Security-Elementen
        return content.Contains("<Security") || content.Contains("DataSecurity=\"1\"");
    }
}
```

#### 2.3 Basis-ProjectParser erstellen

**Services/ProjectParser.cs:**
```csharp
namespace KnxMonitor.ProjectParser.Services;

public class ProjectParser : IProjectParser
{
    private readonly IEnumerable<IProjectLoader> _loaders;
    private readonly IFeatureDetector _featureDetector;
    private readonly ILogger<ProjectParser> _logger;

    public ProjectParser(
        IEnumerable<IProjectLoader> loaders,
        IFeatureDetector featureDetector,
        ILogger<ProjectParser> logger)
    {
        _loaders = loaders;
        _featureDetector = featureDetector;
        _logger = logger;
    }

    public async Task<ParseResult> ParseAsync(
        Stream stream,
        ParserOptions options,
        IProgress<ParserProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            // 1. Detect features
            ReportProgress(progress, ParseStep.DetectFeatures, 0);
            var features = await _featureDetector.DetectAsync(stream, cancellationToken);

            // 2. Select appropriate loader
            var loader = _loaders.FirstOrDefault(l => l.CanLoad(features));
            if (loader == null)
            {
                throw new NotSupportedException(
                    $"No loader available for ETS version {features.EtsVersion}"
                );
            }

            _logger.LogInformation("Using loader: {Loader}", loader.GetType().Name);

            // 3. Open ZIP (mit Password falls nötig)
            ReportProgress(progress, ParseStep.OpenZip, 0);
            using var archive = await ZipHandler.OpenAsync(
                stream,
                features,
                options.Password,
                progress
            );

            // 4. Load project data
            var result = await loader.LoadAsync(archive, options, progress, cancellationToken);

            // 5. Statistics
            result.Features = features;
            result.Statistics = new ParseStatistics
            {
                Duration = DateTime.UtcNow - startTime,
                GroupAddressCount = result.GroupAddresses.Count,
                DeviceCount = result.Devices.Count
            };

            ReportProgress(progress, ParseStep.Complete, 100);

            _logger.LogInformation(
                "Parsed successfully: {GAs} GAs, {Devices} Devices in {Duration}ms",
                result.GroupAddresses.Count,
                result.Devices.Count,
                result.Statistics.Duration.TotalMilliseconds
            );

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse project");
            throw;
        }
    }

    public Task<ExtendedParseResult> ParseExtendedAsync(
        Stream stream,
        ExtendedParserOptions options,
        IProgress<ParserProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // TODO: Phase 6 - Erweiterte Features
        throw new NotImplementedException("Extended parsing not yet implemented");
    }

    private void ReportProgress(
        IProgress<ParserProgress>? progress,
        ParseStep step,
        int percent)
    {
        progress?.Report(new ParserProgress
        {
            Step = step,
            PercentComplete = percent
        });
    }
}
```

#### 2.4 ZipHandler erstellen

**Services/ZipHandler.cs:**
```csharp
namespace KnxMonitor.ProjectParser.Services;

public static class ZipHandler
{
    public static async Task<ZipArchive> OpenAsync(
        Stream stream,
        ProjectFeatures features,
        string? password,
        IProgress<ParserProgress>? progress = null)
    {
        // 1. Öffne äußeres Archive
        var outerArchive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        // 2. Wenn kein Passwort nötig → direkt zurückgeben
        if (!features.HasPassword)
        {
            return outerArchive;
        }

        // 3. Mit Passwort: Extrahiere inneres Archive
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException(
                "Project is password-protected but no password provided"
            );
        }

        progress?.Report(new ParserProgress
        {
            Step = ParseStep.CheckPassword,
            PercentComplete = 0,
            Message = "Extracting password-protected archive"
        });

        // Finde P-xxxx.zip
        var nestedZipEntry = outerArchive.Entries
            .FirstOrDefault(e => e.FullName.EndsWith(".zip") && e.FullName.StartsWith("P-"));

        if (nestedZipEntry == null)
        {
            throw new InvalidOperationException("Nested project archive not found");
        }

        // Extrahiere mit SharpZipLib (unterstützt Passwörter)
        await using var nestedStream = nestedZipEntry.Open();
        var memoryStream = new MemoryStream();

        using (var zipInputStream = new ICSharpCode.SharpZipLib.Zip.ZipInputStream(nestedStream))
        {
            zipInputStream.Password = password;

            ICSharpCode.SharpZipLib.Zip.ZipEntry entry;
            while ((entry = zipInputStream.GetNextEntry()) != null)
            {
                if (!entry.IsFile) continue;

                // Schreibe zu Memory-Stream für ZipArchive
                var entryData = new byte[4096];
                using var entryMemStream = new MemoryStream();

                int bytesRead;
                while ((bytesRead = zipInputStream.Read(entryData, 0, entryData.Length)) > 0)
                {
                    entryMemStream.Write(entryData, 0, bytesRead);
                }

                // TODO: Zu neuem Archive hinzufügen
                // (Vereinfachte Version, vollständige Implementierung in Phase 4)
            }
        }

        progress?.Report(new ParserProgress
        {
            Step = ParseStep.CheckPassword,
            PercentComplete = 100
        });

        memoryStream.Position = 0;
        return new ZipArchive(memoryStream, ZipArchiveMode.Read);
    }
}
```

#### 2.5 BaseProjectLoader erstellen

**Loaders/BaseProjectLoader.cs:**
```csharp
namespace KnxMonitor.ProjectParser.Loaders;

public abstract class BaseProjectLoader : IProjectLoader
{
    protected readonly ILogger _logger;

    public abstract EtsVersion SupportedVersion { get; }

    protected BaseProjectLoader(ILogger logger)
    {
        _logger = logger;
    }

    public virtual bool CanLoad(ProjectFeatures features)
    {
        return features.EtsVersion == SupportedVersion;
    }

    public async Task<ParseResult> LoadAsync(
        ZipArchive archive,
        ParserOptions options,
        IProgress<ParserProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ParseResult();

        try
        {
            // 1. Finde Projekt-XML (0.xml)
            var projectXml = await LoadProjectXmlAsync(archive, progress);

            // 2. Parse Group Addresses
            progress?.Report(new ParserProgress
            {
                Step = ParseStep.ParseGroupAddresses,
                PercentComplete = 0
            });

            result.GroupAddresses = await ParseGroupAddressesAsync(
                projectXml,
                cancellationToken
            );

            progress?.Report(new ParserProgress
            {
                Step = ParseStep.ParseGroupAddresses,
                PercentComplete = 100
            });

            // 3. Parse Devices
            progress?.Report(new ParserProgress
            {
                Step = ParseStep.ParseDevices,
                PercentComplete = 0
            });

            result.Devices = await ParseDevicesAsync(
                archive,
                projectXml,
                cancellationToken
            );

            progress?.Report(new ParserProgress
            {
                Step = ParseStep.ParseDevices,
                PercentComplete = 100
            });

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load project");
            throw;
        }
    }

    protected abstract Task<XDocument> LoadProjectXmlAsync(
        ZipArchive archive,
        IProgress<ParserProgress>? progress
    );

    protected abstract Task<List<GroupAddress>> ParseGroupAddressesAsync(
        XDocument projectXml,
        CancellationToken cancellationToken
    );

    protected abstract Task<List<Device>> ParseDevicesAsync(
        ZipArchive archive,
        XDocument projectXml,
        CancellationToken cancellationToken
    );

    protected XNamespace GetNamespace(XDocument xml)
    {
        return xml.Root?.Name.Namespace ?? XNamespace.None;
    }
}
```

#### 2.6 Konkrete Loader erstellen (Ets5 als Hauptfokus)

**Loaders/Ets5ProjectLoader.cs:**
```csharp
namespace KnxMonitor.ProjectParser.Loaders;

public class Ets5ProjectLoader : BaseProjectLoader
{
    public override EtsVersion SupportedVersion => EtsVersion.Ets5;

    public Ets5ProjectLoader(ILogger<Ets5ProjectLoader> logger) : base(logger) { }

    protected override async Task<XDocument> LoadProjectXmlAsync(
        ZipArchive archive,
        IProgress<ParserProgress>? progress)
    {
        // ETS5: Finde P-xxxx/project.xml (lowercase!)
        var projectEntry = archive.Entries
            .FirstOrDefault(e => e.FullName.EndsWith("/0.xml"));

        if (projectEntry == null)
        {
            throw new FileNotFoundException("Project file (0.xml) not found in archive");
        }

        await using var stream = projectEntry.Open();
        return await XDocument.LoadAsync(stream, LoadOptions.None, default);
    }

    protected override async Task<List<GroupAddress>> ParseGroupAddressesAsync(
        XDocument projectXml,
        CancellationToken cancellationToken)
    {
        var ns = GetNamespace(projectXml);
        var groupAddresses = new List<GroupAddress>();

        var gaElements = projectXml.Descendants(ns + "GroupAddress");

        foreach (var element in gaElements)
        {
            var rawAddress = int.Parse(element.Attribute("Address")?.Value ?? "0");
            var dptString = element.Attribute("DatapointType")?.Value;

            var ga = new GroupAddress
            {
                RawAddress = rawAddress,
                Address = AddressConverter.ToGroupAddressString(rawAddress),
                Name = element.Attribute("Name")?.Value ?? string.Empty,
                Description = element.Attribute("Description")?.Value,
                DatapointType = DptInfo.TryParse(dptString),
                Identifier = element.Attribute("Id")?.Value
            };

            groupAddresses.Add(ga);
        }

        _logger.LogInformation("Parsed {Count} group addresses", groupAddresses.Count);
        return groupAddresses;
    }

    protected override async Task<List<Device>> ParseDevicesAsync(
        ZipArchive archive,
        XDocument projectXml,
        CancellationToken cancellationToken)
    {
        var ns = GetNamespace(projectXml);
        var devices = new List<Device>();

        // Hardware.xml für Product-Lookup laden
        var hardwareData = await LoadHardwareDataAsync(archive);

        var deviceElements = projectXml
            .Descendants(ns + "DeviceInstance")
            .Where(d => d.Attribute("Address") != null);

        foreach (var element in deviceElements)
        {
            var rawAddress = int.Parse(element.Attribute("Address")?.Value ?? "0");
            var productRefId = element.Attribute("ProductRefId")?.Value;

            var device = new Device
            {
                RawAddress = rawAddress,
                PhysicalAddress = AddressConverter.ToPhysicalAddressString(rawAddress),
                Name = element.Attribute("Name")?.Value ?? string.Empty,
                ProductRefId = productRefId
            };

            // Lookup Hardware-Info
            if (productRefId != null && hardwareData.TryGetValue(productRefId, out var hwInfo))
            {
                device.Manufacturer = hwInfo.Manufacturer;
                device.ProductName = hwInfo.ProductName;
            }

            devices.Add(device);
        }

        _logger.LogInformation("Parsed {Count} devices", devices.Count);
        return devices;
    }

    private async Task<Dictionary<string, (string Manufacturer, string ProductName)>>
        LoadHardwareDataAsync(ZipArchive archive)
    {
        var hardwareData = new Dictionary<string, (string, string)>();

        var hardwareFiles = archive.Entries
            .Where(e => e.FullName.EndsWith("/Hardware.xml"));

        foreach (var entry in hardwareFiles)
        {
            await using var stream = entry.Open();
            var xml = await XDocument.LoadAsync(stream, LoadOptions.None, default);
            var ns = GetNamespace(xml);

            var manufacturer = xml.Root?
                .Attribute("RefId")?.Value?
                .Split('-').FirstOrDefault() ?? "Unknown";

            var products = xml.Descendants(ns + "Product");
            foreach (var product in products)
            {
                var productRefId = product.Attribute("Id")?.Value;
                var productName = product.Attribute("Text")?.Value ?? "Unknown";

                if (productRefId != null)
                {
                    hardwareData[productRefId] = (manufacturer, productName);
                }
            }
        }

        return hardwareData;
    }
}
```

#### 2.7 Helpers implementieren

**Helpers/AddressConverter.cs:**
```csharp
namespace KnxMonitor.ProjectParser.Helpers;

public static class AddressConverter
{
    public static string ToGroupAddressString(int rawAddress)
    {
        // Format: main/middle/sub
        // Bits: main(5) | middle(3) | sub(8)
        var main = (rawAddress >> 11) & 0x1F;
        var middle = (rawAddress >> 8) & 0x07;
        var sub = rawAddress & 0xFF;

        return $"{main}/{middle}/{sub}";
    }

    public static string ToPhysicalAddressString(int rawAddress)
    {
        // Format: area.line.device
        // Bits: area(4) | line(4) | device(8)
        var area = (rawAddress >> 12) & 0x0F;
        var line = (rawAddress >> 8) & 0x0F;
        var device = rawAddress & 0xFF;

        return $"{area}.{line}.{device}";
    }

    public static int FromGroupAddressString(string address)
    {
        var parts = address.Split('/');
        if (parts.Length != 3) throw new ArgumentException("Invalid group address format");

        var main = int.Parse(parts[0]);
        var middle = int.Parse(parts[1]);
        var sub = int.Parse(parts[2]);

        return (main << 11) | (middle << 8) | sub;
    }

    public static int FromPhysicalAddressString(string address)
    {
        var parts = address.Split('.');
        if (parts.Length != 3) throw new ArgumentException("Invalid physical address format");

        var area = int.Parse(parts[0]);
        var line = int.Parse(parts[1]);
        var device = int.Parse(parts[2]);

        return (area << 12) | (line << 8) | device;
    }
}
```

**Deliverable:** ✅ Feature-Parity mit bestehendem Parser (ETS5 funktioniert)

---

### Phase 3: Adapter-Layer implementieren (Tag 5)

**Ziel:** Infrastructure-Adapter erstellen, Wizard bleibt funktionsfähig

#### 3.1 Infrastructure-Adapter

**KnxMonitor.Infrastructure/Services/KnxProjectParserService.cs:**

```csharp
// ALTER CODE WIRD KOMPLETT GELÖSCHT!
// NEUER CODE (nur Adapter):

using KnxMonitor.ProjectParser.Core.Interfaces;
using KnxMonitor.ProjectParser.Core.Models;
using ParserOptions = KnxMonitor.ProjectParser.Core.Models.ParserOptions;
using ParserProgress = KnxMonitor.ProjectParser.Core.Models.ParserProgress;

namespace KnxMonitor.Infrastructure.Services;

/// <summary>
/// Adapter: Delegiert zu ProjectParser Library
/// KEIN Parser-Code mehr hier!
/// </summary>
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

    public async Task<(List<Core.Entities.GroupAddress>, List<Core.Entities.Device>)>
        ParseProjectAsync(
            Stream fileStream,
            string? password = null,
            IProgress<Infrastructure.Models.ParserProgress>? progress = null)
    {
        try
        {
            _logger.LogInformation("Parsing KNX project via Library");

            // 1. Parse mit Library
            var result = await _parser.ParseAsync(
                fileStream,
                new ParserOptions { Password = password },
                MapProgressToLibrary(progress)
            );

            // 2. Map Library-DTOs zu Infrastructure-Entities
            var groupAddresses = result.GroupAddresses
                .Select(MapGroupAddressToEntity)
                .ToList();

            var devices = result.Devices
                .Select(MapDeviceToEntity)
                .ToList();

            _logger.LogInformation(
                "Parsed: {GAs} GAs, {Devices} Devices",
                groupAddresses.Count,
                devices.Count
            );

            return (groupAddresses, devices);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse project");
            throw;
        }
    }

    // Mapping: Library DTO → Infrastructure Entity
    private Core.Entities.GroupAddress MapGroupAddressToEntity(
        KnxMonitor.ProjectParser.Core.Models.GroupAddress dto)
    {
        return new Core.Entities.GroupAddress
        {
            Address = dto.Address,
            Name = dto.Name,
            Description = dto.Description,
            DatapointType = dto.DatapointType?.OriginalString,
            CreatedAt = DateTime.UtcNow
        };
    }

    private Core.Entities.Device MapDeviceToEntity(
        KnxMonitor.ProjectParser.Core.Models.Device dto)
    {
        return new Core.Entities.Device
        {
            Name = dto.Name,
            PhysicalAddress = dto.PhysicalAddress,
            Manufacturer = dto.Manufacturer,
            ProductName = dto.ProductName
        };
    }

    // Progress-Mapping: Infrastructure → Library
    private IProgress<ParserProgress>? MapProgressToLibrary(
        IProgress<Infrastructure.Models.ParserProgress>? progress)
    {
        if (progress == null) return null;

        return new Progress<ParserProgress>(p =>
        {
            progress.Report(new Infrastructure.Models.ParserProgress
            {
                Step = MapStep(p.Step),
                PercentComplete = p.PercentComplete,
                Message = p.Message
            });
        });
    }

    private Infrastructure.Models.ParseStep MapStep(
        KnxMonitor.ProjectParser.Core.Enums.ParseStep step)
    {
        return step switch
        {
            KnxMonitor.ProjectParser.Core.Enums.ParseStep.OpenZip => Infrastructure.Models.ParseStep.OpenZip,
            KnxMonitor.ProjectParser.Core.Enums.ParseStep.CheckPassword => Infrastructure.Models.ParseStep.CheckPassword,
            KnxMonitor.ProjectParser.Core.Enums.ParseStep.ParseGroupAddresses => Infrastructure.Models.ParseStep.ParseGroupAddresses,
            KnxMonitor.ProjectParser.Core.Enums.ParseStep.ParseDevices => Infrastructure.Models.ParseStep.ParseDevices,
            _ => Infrastructure.Models.ParseStep.OpenZip
        };
    }
}
```

#### 3.2 Dependency Injection anpassen

**KnxMonitor.Api/Program.cs:**

```csharp
// Library-Services registrieren
builder.Services.AddSingleton<KnxMonitor.ProjectParser.Core.Interfaces.IFeatureDetector,
    KnxMonitor.ProjectParser.Services.FeatureDetector>();

builder.Services.AddSingleton<KnxMonitor.ProjectParser.Core.Interfaces.IProjectLoader,
    KnxMonitor.ProjectParser.Loaders.Ets4ProjectLoader>();

builder.Services.AddSingleton<KnxMonitor.ProjectParser.Core.Interfaces.IProjectLoader,
    KnxMonitor.ProjectParser.Loaders.Ets5ProjectLoader>();

builder.Services.AddSingleton<KnxMonitor.ProjectParser.Core.Interfaces.IProjectLoader,
    KnxMonitor.ProjectParser.Loaders.Ets6ProjectLoader>();

builder.Services.AddSingleton<KnxMonitor.ProjectParser.Core.Interfaces.IProjectParser,
    KnxMonitor.ProjectParser.Services.ProjectParser>();

// Adapter (nutzt Library)
builder.Services.AddScoped<KnxMonitor.Core.Interfaces.IKnxProjectParserService,
    KnxMonitor.Infrastructure.Services.KnxProjectParserService>();
```

**Deliverable:** ✅ Wizard funktioniert mit Library über Adapter

---

### Phase 4: CLI-Tool entwickeln (Tag 6-7)

**Ziel:** Kommandozeilen-Tool für manuelle Tests und Batch-Processing

#### 4.1 Program.cs (System.CommandLine)

**KnxMonitor.ParserTool/Program.cs:**

```csharp
using System.CommandLine;
using KnxMonitor.ParserTool.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var rootCommand = new RootCommand("KNX Project Parser CLI Tool");

// Commands
rootCommand.AddCommand(ParseCommand.Create());
rootCommand.AddCommand(BatchCommand.Create());
rootCommand.AddCommand(DetectCommand.Create());
rootCommand.AddCommand(CompareCommand.Create());

return await rootCommand.InvokeAsync(args);
```

#### 4.2 Parse-Command

**Commands/ParseCommand.cs:**

```csharp
namespace KnxMonitor.ParserTool.Commands;

public static class ParseCommand
{
    public static Command Create()
    {
        var command = new Command("parse", "Parse a single .knxproj file");

        var fileArg = new Argument<FileInfo>(
            "file",
            "Path to .knxproj file"
        ).ExistingOnly();

        var passwordOpt = new Option<string?>(
            "--password",
            "Project password (if protected)"
        );

        var outputOpt = new Option<FileInfo?>(
            "--output",
            "Output file path (JSON)"
        );

        var verboseOpt = new Option<bool>(
            "--verbose",
            "Verbose logging"
        );

        command.AddArgument(fileArg);
        command.AddOption(passwordOpt);
        command.AddOption(outputOpt);
        command.AddOption(verboseOpt);

        command.SetHandler(async (file, password, output, verbose) =>
        {
            await ExecuteAsync(file, password, output, verbose);
        }, fileArg, passwordOpt, outputOpt, verboseOpt);

        return command;
    }

    private static async Task ExecuteAsync(
        FileInfo file,
        string? password,
        FileInfo? output,
        bool verbose)
    {
        // Setup DI
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
        });

        // Library-Services
        services.AddSingleton<IFeatureDetector, FeatureDetector>();
        services.AddSingleton<IProjectLoader, Ets4ProjectLoader>();
        services.AddSingleton<IProjectLoader, Ets5ProjectLoader>();
        services.AddSingleton<IProjectLoader, Ets6ProjectLoader>();
        services.AddSingleton<IProjectParser, ProjectParser>();

        var provider = services.BuildServiceProvider();
        var parser = provider.GetRequiredService<IProjectParser>();

        // Parse
        Console.WriteLine($"Parsing: {file.FullName}");

        var progress = new Progress<ParserProgress>(p =>
        {
            Console.WriteLine($"  [{p.Step}] {p.PercentComplete}% {p.Message}");
        });

        await using var stream = file.OpenRead();
        var result = await parser.ParseAsync(
            stream,
            new ParserOptions { Password = password },
            progress
        );

        // Output
        Console.WriteLine("\n=== Results ===");
        Console.WriteLine($"ETS Version: {result.Features.EtsVersion}");
        Console.WriteLine($"Group Addresses: {result.GroupAddresses.Count}");
        Console.WriteLine($"Devices: {result.Devices.Count}");
        Console.WriteLine($"Duration: {result.Statistics.Duration.TotalMilliseconds:F0}ms");

        if (result.Statistics.Warnings.Any())
        {
            Console.WriteLine($"\nWarnings: {result.Statistics.Warnings.Count}");
            foreach (var warning in result.Statistics.Warnings)
            {
                Console.WriteLine($"  - {warning}");
            }
        }

        // JSON-Output
        if (output != null)
        {
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(output.FullName, json);
            Console.WriteLine($"\nSaved to: {output.FullName}");
        }
    }
}
```

#### 4.3 Batch-Command

**Commands/BatchCommand.cs:**

```csharp
public static class BatchCommand
{
    public static Command Create()
    {
        var command = new Command("batch", "Parse multiple .knxproj files");

        var patternArg = new Argument<string>(
            "pattern",
            "File pattern (e.g., \"docs/samples/**/*.knxproj\")"
        );

        var passwordsOpt = new Option<string?>(
            "--passwords",
            "Comma-separated passwords to try"
        );

        var reportOpt = new Option<FileInfo>(
            "--report",
            () => new FileInfo("batch-report.csv"),
            "Report file path (CSV)"
        );

        command.AddArgument(patternArg);
        command.AddOption(passwordsOpt);
        command.AddOption(reportOpt);

        command.SetHandler(async (pattern, passwords, report) =>
        {
            await ExecuteAsync(pattern, passwords, report);
        }, patternArg, passwordsOpt, reportOpt);

        return command;
    }

    private static async Task ExecuteAsync(
        string pattern,
        string? passwords,
        FileInfo report)
    {
        var passwordList = passwords?.Split(',') ?? new[] { (string?)null };

        // Setup Parser
        var services = BuildServiceProvider();
        var parser = services.GetRequiredService<IProjectParser>();

        // Find Files
        var files = FindFiles(pattern);
        Console.WriteLine($"Found {files.Count} files\n");

        // Results
        var results = new List<BatchResult>();

        foreach (var file in files)
        {
            Console.Write($"{Path.GetFileName(file)}... ");

            BatchResult result = null;
            foreach (var pwd in passwordList)
            {
                try
                {
                    await using var stream = File.OpenRead(file);
                    var parseResult = await parser.ParseAsync(
                        stream,
                        new ParserOptions { Password = pwd }
                    );

                    result = new BatchResult
                    {
                        File = file,
                        Success = true,
                        EtsVersion = parseResult.Features.EtsVersion.ToString(),
                        HasPassword = parseResult.Features.HasPassword,
                        HasKnxSecure = parseResult.Features.HasKnxSecure,
                        GroupAddressCount = parseResult.GroupAddresses.Count,
                        DeviceCount = parseResult.Devices.Count
                    };

                    Console.WriteLine($"✓ ({result.EtsVersion}, {result.GroupAddressCount} GAs)");
                    break;
                }
                catch (Exception ex)
                {
                    if (pwd == passwordList.Last())
                    {
                        result = new BatchResult
                        {
                            File = file,
                            Success = false,
                            Error = ex.Message
                        };
                        Console.WriteLine($"✗ {ex.Message}");
                    }
                }
            }

            results.Add(result);
        }

        // Write CSV Report
        WriteCsvReport(report, results);
        Console.WriteLine($"\nReport saved to: {report.FullName}");
    }

    private static void WriteCsvReport(FileInfo file, List<BatchResult> results)
    {
        using var writer = new StreamWriter(file.FullName);
        writer.WriteLine("File,EtsVersion,HasPassword,HasKnxSecure,GroupAddresses,Devices,Success,Error");

        foreach (var result in results)
        {
            writer.WriteLine(
                $"{Path.GetFileName(result.File)}," +
                $"{result.EtsVersion}," +
                $"{result.HasPassword}," +
                $"{result.HasKnxSecure}," +
                $"{result.GroupAddressCount}," +
                $"{result.DeviceCount}," +
                $"{result.Success}," +
                $"\"{result.Error}\""
            );
        }
    }
}
```

#### 4.4 Detect-Command

**Commands/DetectCommand.cs:**

```csharp
public static class DetectCommand
{
    public static Command Create()
    {
        var command = new Command("detect", "Detect project features without parsing");

        var fileArg = new Argument<FileInfo>("file", "Path to .knxproj file").ExistingOnly();
        command.AddArgument(fileArg);

        command.SetHandler(async (file) =>
        {
            await ExecuteAsync(file);
        }, fileArg);

        return command;
    }

    private static async Task ExecuteAsync(FileInfo file)
    {
        var services = BuildServiceProvider();
        var detector = services.GetRequiredService<IFeatureDetector>();

        Console.WriteLine($"Analyzing: {file.Name}\n");

        await using var stream = file.OpenRead();
        var features = await detector.DetectAsync(stream);

        Console.WriteLine("=== Features ===");
        Console.WriteLine($"Project ID: {features.ProjectId}");
        Console.WriteLine($"ETS Version: {features.EtsVersion}");
        Console.WriteLine($"Password Protected: {features.HasPassword}");
        Console.WriteLine($"KNX Secure: {features.HasKnxSecure}");
    }
}
```

#### 4.5 Compare-Command (mit xknxproject Stubs)

**Commands/CompareCommand.cs:**

```csharp
public static class CompareCommand
{
    public static Command Create()
    {
        var command = new Command("compare", "Compare parse result with reference");

        var fileArg = new Argument<FileInfo>("file", "Path to .knxproj file").ExistingOnly();
        var referenceArg = new Argument<FileInfo>("reference", "Path to reference JSON").ExistingOnly();
        var passwordOpt = new Option<string?>("--password", "Project password");
        var diffOpt = new Option<bool>("--diff", "Show differences");

        command.AddArgument(fileArg);
        command.AddArgument(referenceArg);
        command.AddOption(passwordOpt);
        command.AddOption(diffOpt);

        command.SetHandler(async (file, reference, password, diff) =>
        {
            var exitCode = await ExecuteAsync(file, reference, password, diff);
            Environment.Exit(exitCode);
        }, fileArg, referenceArg, passwordOpt, diffOpt);

        return command;
    }

    private static async Task<int> ExecuteAsync(
        FileInfo file,
        FileInfo reference,
        string? password,
        bool showDiff)
    {
        // Parse mit Library
        var services = BuildServiceProvider();
        var parser = services.GetRequiredService<IProjectParser>();

        await using var stream = file.OpenRead();
        var result = await parser.ParseAsync(
            stream,
            new ParserOptions { Password = password }
        );

        // Load xknxproject Reference
        var referenceJson = await File.ReadAllTextAsync(reference.FullName);
        var expected = JsonSerializer.Deserialize<XknxprojectStub>(referenceJson);

        // Compare
        var differences = new List<string>();

        if (result.GroupAddresses.Count != expected.GroupAddresses.Count)
        {
            differences.Add(
                $"GA Count: Expected {expected.GroupAddresses.Count}, Got {result.GroupAddresses.Count}"
            );
        }

        if (result.Devices.Count != expected.Devices.Count)
        {
            differences.Add(
                $"Device Count: Expected {expected.Devices.Count}, Got {result.Devices.Count}"
            );
        }

        // Detailed GA comparison
        foreach (var expectedGa in expected.GroupAddresses)
        {
            var actualGa = result.GroupAddresses
                .FirstOrDefault(ga => ga.RawAddress == expectedGa.RawAddress);

            if (actualGa == null)
            {
                differences.Add($"Missing GA: {expectedGa.Address} ({expectedGa.Name})");
            }
            else if (actualGa.Name != expectedGa.Name)
            {
                differences.Add(
                    $"GA Name Mismatch: {expectedGa.Address} - " +
                    $"Expected '{expectedGa.Name}', Got '{actualGa.Name}'"
                );
            }
        }

        // Output
        if (differences.Any())
        {
            Console.WriteLine($"✗ {differences.Count} differences found");

            if (showDiff)
            {
                foreach (var diff in differences)
                {
                    Console.WriteLine($"  - {diff}");
                }
            }

            return 1; // Exit code: Failure
        }
        else
        {
            Console.WriteLine("✓ Results match reference");
            return 0; // Exit code: Success
        }
    }
}
```

**Deliverable:** ✅ Funktionstüchtiges CLI-Tool mit 4 Commands

---

### Phase 5: xUnit Test-Suite (Tag 8-10)

**Ziel:** Umfassende Tests mit >80% Coverage

#### 5.1 Test-Fixtures erstellen

**Fixtures/SampleFileFixture.cs:**

```csharp
namespace KnxMonitor.ProjectParser.Tests.Fixtures;

public class SampleFileFixture : IDisposable
{
    public IProjectParser Parser { get; }
    public IFeatureDetector FeatureDetector { get; }

    public SampleFileFixture()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IFeatureDetector, FeatureDetector>();
        services.AddSingleton<IProjectLoader, Ets4ProjectLoader>();
        services.AddSingleton<IProjectLoader, Ets5ProjectLoader>();
        services.AddSingleton<IProjectLoader, Ets6ProjectLoader>();
        services.AddSingleton<IProjectParser, ProjectParser>();

        var provider = services.BuildServiceProvider();

        Parser = provider.GetRequiredService<IProjectParser>();
        FeatureDetector = provider.GetRequiredService<IFeatureDetector>();
    }

    public void Dispose()
    {
        // Cleanup if needed
    }
}

public class SampleFileInfo
{
    public string Name { get; set; }
    public string FilePath { get; set; }
    public string? Password { get; set; }
    public EtsVersion ExpectedEtsVersion { get; set; }
    public bool HasReferenceStub { get; set; }
    public string? StubPath { get; set; }
    public int? ExpectedGroupAddressCount { get; set; }
    public int? ExpectedDeviceCount { get; set; }
}
```

#### 5.2 Sample-File-Tests (parametrisiert)

**Integration/SampleFileTests.cs:**

```csharp
namespace KnxMonitor.ProjectParser.Tests.Integration;

public class SampleFileTests : IClassFixture<SampleFileFixture>
{
    private readonly IProjectParser _parser;

    public SampleFileTests(SampleFileFixture fixture)
    {
        _parser = fixture.Parser;
    }

    [Theory]
    [MemberData(nameof(GetAllSampleFiles))]
    public async Task ParseProject_SampleFile_Success(SampleFileInfo sample)
    {
        // Arrange
        await using var stream = File.OpenRead(sample.FilePath);
        var options = new ParserOptions { Password = sample.Password };

        // Act
        var result = await _parser.ParseAsync(stream, options);

        // Assert
        result.Should().NotBeNull();
        result.GroupAddresses.Should().NotBeEmpty();
        result.Features.EtsVersion.Should().Be(sample.ExpectedEtsVersion);

        if (sample.ExpectedGroupAddressCount.HasValue)
        {
            result.GroupAddresses.Count.Should().Be(sample.ExpectedGroupAddressCount.Value);
        }

        if (sample.ExpectedDeviceCount.HasValue)
        {
            result.Devices.Count.Should().Be(sample.ExpectedDeviceCount.Value);
        }

        // Validate addresses
        foreach (var ga in result.GroupAddresses)
        {
            ga.Address.Should().MatchRegex(@"^\d{1,2}/\d{1,2}/\d{1,3}$");
            ga.Name.Should().NotBeNullOrEmpty();
        }

        foreach (var device in result.Devices)
        {
            device.PhysicalAddress.Should().MatchRegex(@"^\d{1,2}\.\d{1,2}\.\d{1,3}$");
        }
    }

    public static IEnumerable<object[]> GetAllSampleFiles()
    {
        // DEIN ETS5-Projekt (Hauptreferenz) - ZUERST!
        yield return new object[] { new SampleFileInfo {
            Name = "Your ETS5 Project (Main Reference)",
            FilePath = "TestData/your-ets5-project.knxproj",
            Password = "your-password",
            ExpectedEtsVersion = EtsVersion.Ets5,
            ExpectedGroupAddressCount = 42,  // deine bekannte Anzahl
            ExpectedDeviceCount = 8
        }};

        // xknxproject ETS5 Samples
        yield return new object[] { new SampleFileInfo {
            Name = "xknxproject ETS5 Test Project",
            FilePath = "../../docs/samples/xknxproject/xknx_test_project.knxproj",
            Password = "test",
            ExpectedEtsVersion = EtsVersion.Ets5,
            HasReferenceStub = true,
            StubPath = "../../docs/samples/xknxproject/stubs/xknx_test_project.json"
        }};

        // ETS4 Samples
        yield return new object[] { new SampleFileInfo {
            Name = "ETS4 with Password",
            FilePath = "../../docs/samples/xknxproject/test_project-ets4.knxproj",
            Password = "test",
            ExpectedEtsVersion = EtsVersion.Ets4
        }};

        yield return new object[] { new SampleFileInfo {
            Name = "ETS4 without Password",
            FilePath = "../../docs/samples/xknxproject/test_project-ets4-no_password.knxproj",
            Password = null,
            ExpectedEtsVersion = EtsVersion.Ets4
        }};

        // ETS6 Samples
        yield return new object[] { new SampleFileInfo {
            Name = "ETS6 Standard",
            FilePath = "../../docs/samples/xknxproject/testprojekt-ets6.knxproj",
            Password = "test",
            ExpectedEtsVersion = EtsVersion.Ets6
        }};

        yield return new object[] { new SampleFileInfo {
            Name = "ETS6 Free Addressing",
            FilePath = "../../docs/samples/xknxproject/ets6_free.knxproj",
            Password = null,
            ExpectedEtsVersion = EtsVersion.Ets6
        }};

        yield return new object[] { new SampleFileInfo {
            Name = "ETS6 TwoLevel Addressing",
            FilePath = "../../docs/samples/xknxproject/ets6_two_level.knxproj",
            Password = null,
            ExpectedEtsVersion = EtsVersion.Ets6
        }};

        // ... weitere Files
    }
}
```

#### 5.3 Unit-Tests

**Unit/Helpers/AddressConverterTests.cs:**

```csharp
public class AddressConverterTests
{
    [Theory]
    [InlineData(2049, "1/0/1")]
    [InlineData(6657, "3/2/1")]
    [InlineData(0, "0/0/0")]
    [InlineData(65535, "31/7/255")]
    public void ToGroupAddressString_ValidInt_ReturnsCorrectFormat(
        int rawAddress,
        string expected)
    {
        var result = AddressConverter.ToGroupAddressString(rawAddress);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(4353, "1.1.1")]
    [InlineData(8448, "2.1.0")]
    public void ToPhysicalAddressString_ValidInt_ReturnsCorrectFormat(
        int rawAddress,
        string expected)
    {
        var result = AddressConverter.ToPhysicalAddressString(rawAddress);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("1/0/1", 2049)]
    [InlineData("3/2/1", 6657)]
    public void FromGroupAddressString_ValidString_ReturnsCorrectInt(
        string address,
        int expected)
    {
        var result = AddressConverter.FromGroupAddressString(address);
        result.Should().Be(expected);
    }
}
```

**Unit/Helpers/DptParserTests.cs:**

```csharp
public class DptParserTests
{
    [Theory]
    [InlineData("DPST-1-1", 1, 1)]
    [InlineData("DPST-5-001", 5, 1)]
    [InlineData("DPT-9", 9, null)]
    [InlineData("DPST-14-056", 14, 56)]
    public void Parse_ValidDptString_ReturnsCorrectInfo(
        string input,
        int expectedMain,
        int? expectedSub)
    {
        var result = DptParser.Parse(input);

        result.Should().NotBeNull();
        result.Main.Should().Be(expectedMain);
        result.Sub.Should().Be(expectedSub);
        result.OriginalString.Should().Be(input);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsNull()
    {
        var result = DptInfo.TryParse("");
        result.Should().BeNull();
    }
}
```

**Deliverable:** ✅ 50+ Unit-Tests, 15+ Integration-Tests, >80% Coverage

---

### Phase 6: Parser-Erweiterungen & Bug-Fixes (Tag 11-14)

**Ziel:** ETS6-Bug fixen, DPT-Parsing verbessern, optional COs/Topology

#### 6.1 ETS6 Segment-Tag-Fix

**Loaders/Ets6ProjectLoader.cs:**

```csharp
public class Ets6ProjectLoader : BaseProjectLoader
{
    public override EtsVersion SupportedVersion => EtsVersion.Ets6;

    protected override async Task<List<Device>> ParseDevicesAsync(
        ZipArchive archive,
        XDocument projectXml,
        CancellationToken cancellationToken)
    {
        var ns = GetNamespace(projectXml);
        var devices = new List<Device>();

        // ETS6 CRITICAL: Segment-Tag zwischen Line und DeviceInstance!
        var deviceElements = projectXml
            .Descendants(ns + "Line")
            .Descendants(ns + "Segment")  // ← WICHTIG!
            .Descendants(ns + "DeviceInstance");

        // Fallback: Ohne Segment (ältere ETS6-Versionen?)
        if (!deviceElements.Any())
        {
            deviceElements = projectXml
                .Descendants(ns + "Line")
                .Descendants(ns + "DeviceInstance");
        }

        // Rest wie ETS5...
        foreach (var element in deviceElements.Where(d => d.Attribute("Address") != null))
        {
            // ... wie ETS5Loader
        }

        return devices;
    }
}
```

#### 6.2 DPT-Parser robust implementieren

**Helpers/DptParser.cs:**

```csharp
public static class DptParser
{
    private static readonly Regex DptRegex = new Regex(
        @"DPST?-(\d+)(?:-0*(\d+))?",
        RegexOptions.Compiled
    );

    public static DptInfo? Parse(string? dptString)
    {
        if (string.IsNullOrWhiteSpace(dptString))
            return null;

        var match = DptRegex.Match(dptString);
        if (!match.Success)
        {
            // Log warning, return mit OriginalString
            return new DptInfo { OriginalString = dptString };
        }

        var main = int.Parse(match.Groups[1].Value);
        var sub = match.Groups[2].Success
            ? int.Parse(match.Groups[2].Value)
            : (int?)null;

        return new DptInfo
        {
            Main = main,
            Sub = sub,
            OriginalString = dptString
        };
    }
}
```

#### 6.3 Communication Objects (optional, für später)

**Loaders/ApplicationProgramLoader.cs:**

```csharp
public class ApplicationProgramLoader
{
    public async Task<List<CommunicationObject>> LoadAsync(
        ZipArchive archive,
        string applicationProgramRefId)
    {
        // M-<Manufacturer>/P-<ProductRef>.xml finden
        var appProgFile = FindApplicationProgramFile(archive, applicationProgramRefId);
        if (appProgFile == null)
        {
            return new List<CommunicationObject>();
        }

        await using var stream = appProgFile.Open();
        var xml = await XDocument.LoadAsync(stream, LoadOptions.None, default);
        var ns = GetNamespace(xml);

        var comObjects = xml.Descendants(ns + "ComObject")
            .Select(co => new CommunicationObject
            {
                Number = int.Parse(co.Attribute("Number")?.Value ?? "0"),
                Name = co.Attribute("Name")?.Value ?? string.Empty,
                Text = co.Attribute("Text")?.Value,
                FunctionText = co.Attribute("FunctionText")?.Value,
                ReadFlag = ParseFlag(co.Attribute("ReadFlag")),
                WriteFlag = ParseFlag(co.Attribute("WriteFlag")),
                CommunicationFlag = ParseFlag(co.Attribute("CommunicationFlag")),
                TransmitFlag = ParseFlag(co.Attribute("TransmitFlag")),
                UpdateFlag = ParseFlag(co.Attribute("UpdateFlag")),
                ReadOnInitFlag = ParseFlag(co.Attribute("ReadOnInitFlag")),
                DatapointType = co.Attribute("DatapointType")?.Value,
                ObjectSize = co.Attribute("ObjectSize")?.Value
            })
            .ToList();

        return comObjects;
    }

    private bool ParseFlag(XAttribute? attribute)
    {
        var value = attribute?.Value;
        return value == "Enabled" || value == "1" || value == "true";
    }
}
```

**Deliverable:** ✅ ETS6-Devices funktionieren, DPT-Parsing robust, Basis für COs

---

### Phase 7: Integration & Validation (Tag 15-16)

**Ziel:** Alles testen, Wizard validieren, Batch-Report erstellen

#### 7.1 CLI Batch-Test

```bash
cd backend/KnxMonitor.ParserTool

dotnet run -- batch "../../docs/samples/**/*.knxproj" \
    --passwords "test" \
    --report batch-report.csv
```

**Erwartetes Ergebnis:**
- Mind. 12 Files erfolgreich geparst
- Keine kritischen Errors
- Report mit allen Details

#### 7.2 Wizard End-to-End-Test

**Manuelle Tests:**

1. **Dein ETS5-Projekt:**
   - Upload → Analyze → Enter Password → Import
   - Verify: Korrekte Anzahl GAs/Devices
   - Compare mit vorherigem Stand

2. **Verschiedene ETS-Versionen:**
   - ETS4 ohne PW
   - ETS5 mit PW + Secure
   - ETS6 mit PW

3. **Progress-Events:**
   - Prüfen dass alle Steps durchlaufen werden
   - Prozent-Anzeige funktioniert

#### 7.3 Performance-Test

```bash
# Großes Projekt mehrmals parsen
dotnet run -- parse large-project.knxproj --password test

# Erwartung: <5s für normale Projekte
```

#### 7.4 Alter Parser LÖSCHEN!

**Wenn alles grün:**

```bash
# Infrastructure: Alten Parser-Code entfernen
# NUR Adapter bleibt!

# Vorher: ~674 Zeilen Parser-Code in Infrastructure
# Nachher: ~150 Zeilen Adapter-Code

# Library ist jetzt DIE EINZIGE Parser-Implementation!
```

**Deliverable:** ✅ Alle Tests grün, Wizard funktioniert, alter Code gelöscht

---

### Phase 8: Dokumentation (Tag 17)

**Ziel:** Vollständige Dokumentation erstellen

#### 8.1 Library README.md

**backend/KnxMonitor.ProjectParser/README.md:**

```markdown
# KnxMonitor.ProjectParser

Standalone .NET 9 library for parsing KNX project files (.knxproj).

## Features

- ✅ Supports ETS 4, 5, 6
- ✅ Password-protected projects
- ✅ KNX Secure detection
- ✅ Group Addresses with DPT parsing
- ✅ Devices with manufacturer info
- ✅ Progress reporting
- ✅ Extensible architecture
- ✅ NuGet-ready

## Installation

```bash
dotnet add package KnxMonitor.ProjectParser
```

## Usage

```csharp
// Setup DI
services.AddSingleton<IFeatureDetector, FeatureDetector>();
services.AddSingleton<IProjectLoader, Ets4ProjectLoader>();
services.AddSingleton<IProjectLoader, Ets5ProjectLoader>();
services.AddSingleton<IProjectLoader, Ets6ProjectLoader>();
services.AddSingleton<IProjectParser, ProjectParser>();

// Parse project
var parser = serviceProvider.GetRequiredService<IProjectParser>();

await using var stream = File.OpenRead("project.knxproj");
var result = await parser.ParseAsync(stream, new ParserOptions {
    Password = "test"
});

Console.WriteLine($"Found {result.GroupAddresses.Count} group addresses");
Console.WriteLine($"Found {result.Devices.Count} devices");
```

## Extension Points

### Custom Loader for new ETS version

```csharp
public class Ets7ProjectLoader : BaseProjectLoader
{
    public override EtsVersion SupportedVersion => EtsVersion.Ets7;
    // ...
}

services.AddSingleton<IProjectLoader, Ets7ProjectLoader>();
```

### Custom Data Extractor

```csharp
public class MyCustomExtractor : IDataExtractor
{
    public async Task<object?> ExtractAsync(
        ZipArchive archive,
        ParseResult baseResult,
        CancellationToken cancellationToken)
    {
        // Custom logic
    }
}
```

## License

MIT
```

#### 8.2 CLI-Tool README

**backend/KnxMonitor.ParserTool/README.md:**

```markdown
# KnxMonitor.ParserTool

Command-line tool for testing and analyzing KNX project files.

## Commands

### parse

Parse a single .knxproj file.

```bash
dotnet run -- parse sample.knxproj --password test --output result.json --verbose
```

Options:
- `--password`: Project password (if protected)
- `--output`: Save results to JSON file
- `--verbose`: Enable debug logging

### batch

Parse multiple files and generate a report.

```bash
dotnet run -- batch "docs/samples/**/*.knxproj" --passwords "test,pw123" --report report.csv
```

Options:
- `--passwords`: Comma-separated passwords to try
- `--report`: Output CSV file path

### detect

Detect project features without full parsing.

```bash
dotnet run -- detect sample.knxproj
```

Output: ETS version, password protection, KNX Secure status.

### compare

Compare parse results with reference data.

```bash
dotnet run -- compare sample.knxproj reference.json --password test --diff
```

Options:
- `--diff`: Show detailed differences
- Exit code: 0=match, 1=differences found

## Examples

```bash
# Test all samples
dotnet run -- batch "../../docs/samples/**/*.knxproj" --passwords "test" --report all-samples.csv

# Quick feature check
dotnet run -- detect my-project.knxproj

# Validate against baseline
dotnet run -- compare my-project.knxproj baseline.json --password mypass --diff
```
```

#### 8.3 Test-Report

**docs/PARSER_TEST_REPORT.md:**

```markdown
# KNX Project Parser - Test Report

**Date:** 2025-10-31
**Version:** 1.0

## Summary

- **Total Sample Files:** 15
- **Successful:** 14 (93%)
- **Failed:** 1 (known limitation)
- **Test Coverage:** 87%
- **Performance:** <3s average parse time

## Sample File Results

| File | ETS | PW | Secure | GAs | Devices | Status | Notes |
|------|-----|-----|--------|-----|---------|--------|-------|
| your-ets5-project.knxproj | 5 | ✓ | ✓ | 42 | 8 | ✅ | Main reference - 100% match |
| xknx_test_project.knxproj | 5 | ✓ | ✓ | 19 | 5 | ✅ | Full match with stub |
| test_project-ets4.knxproj | 4 | ✓ | ✗ | 12 | 3 | ✅ | - |
| test_project-ets4-no_password.knxproj | 4 | ✗ | ✗ | 12 | 3 | ✅ | - |
| testprojekt-ets6.knxproj | 6 | ✓ | ✗ | 24 | 8 | ✅ | Segment-Tag fix verified |
| ets6_free.knxproj | 6 | ✗ | ✗ | 15 | 6 | ✅ | Free addressing |
| ets6_two_level.knxproj | 6 | ✗ | ✗ | 18 | 7 | ✅ | TwoLevel addressing |
| testprojekt-ets6-functions.knxproj | 6 | ✗ | ✗ | 15 | 6 | ⚠️ | Functions not parsed (future) |
| ... | | | | | | | |

## Gap Analysis vs. xknxproject

### Currently Supported ✅

- Group Addresses (Address, Name, Description, DPT with Main/Sub)
- Devices (Name, PhysicalAddress, Manufacturer, ProductName)
- ETS 4, 5, 6 detection and parsing
- Password-protected projects (Standard ZIP)
- KNX Secure detection
- All addressing styles (Free, TwoLevel, ThreeLevel)

### Not Yet Implemented ⚠️

- Communication Objects (parsed but not exposed)
- Topology details (Areas/Lines structure)
- Locations (Building/Space hierarchy)
- Functions (functional groupings)
- Module instances
- Text parameter substitution
- ETS6 AES encryption (not seen in samples yet)

### Known Limitations

- Functions parsing: Detection works, full parsing not implemented
- Large projects (>1000 devices): Not tested yet
- Non-UTF8 encodings: May cause issues

## Performance Benchmarks

- Small project (<50 GAs): ~500ms
- Medium project (200 GAs): ~2s
- Large project (1000 GAs): ~8s (estimated)

## Recommendations

1. ✅ **Production-Ready** for basic parsing (GAs, Devices)
2. 🔄 **Phase 2:** Expose Communication Objects in Wizard
3. 🔄 **Phase 3:** Topology visualization
4. 🔄 **Phase 4:** Functions/Locations UI

## Test Coverage Details

- Unit Tests: 52 tests, 100% pass
- Integration Tests: 15 tests, 93% pass (1 expected failure)
- Total Coverage: 87% (target: >80%)

## Conclusion

**Library is production-ready** for replacing the old Infrastructure parser. All critical functionality verified, Wizard compatibility ensured, no regressions detected.
```

**Deliverable:** ✅ Vollständige Dokumentation, Test-Report, Recommendations

---

## 7. Zeitplan & Meilensteine

### 7.1 Zeitplan (17 Arbeitstage)

| Phase | Tage | Beschreibung | Meilenstein |
|-------|------|--------------|-------------|
| **1. Library-Grundgerüst** | 1-2 | Projekt setup, Interfaces, Models | ✅ Kompiliert |
| **2. Parser migrieren** | 3-4 | Code-Migration, Feature-Parity | ✅ ETS5 funktioniert |
| **3. Adapter-Layer** | 5 | Infrastructure-Wrapper | ✅ Wizard kompatibel |
| **4. CLI-Tool** | 6-7 | Commands implementieren | ✅ Manuelle Tests möglich |
| **5. xUnit Tests** | 8-10 | Test-Suite aufbauen | ✅ >80% Coverage |
| **6. Erweiterungen** | 11-14 | Bugs fixen, neue Features | ✅ ETS6 gefixt |
| **7. Validation** | 15-16 | Integration, Batch-Test | ✅ Alle Tests grün |
| **8. Dokumentation** | 17 | README, Reports | ✅ Doku komplett |

**Puffer:** +2 Tage für unvorhergesehene Probleme

**Gesamtdauer:** ~3,5 Wochen

### 7.2 Meilenstein-Definition

**M1: Library-Grundgerüst (Tag 2)**
- ✅ Projekt kompiliert
- ✅ Interfaces definiert
- ✅ Basis-Models erstellt
- ✅ DI-Setup funktioniert

**M2: Feature-Parity (Tag 4)**
- ✅ Bestehender Parser-Code in Library
- ✅ ETS5-Projekt erfolgreich geparst
- ✅ Gleiche Results wie alter Parser

**M3: Wizard-Integration (Tag 5)**
- ✅ Adapter implementiert
- ✅ DI konfiguriert
- ✅ Wizard funktioniert unverändert

**M4: CLI-Tool (Tag 7)**
- ✅ 4 Commands implementiert
- ✅ Batch-Test läuft
- ✅ JSON/CSV-Output

**M5: Tests (Tag 10)**
- ✅ 50+ Unit-Tests
- ✅ 15+ Integration-Tests
- ✅ >80% Coverage

**M6: Production-Ready (Tag 14)**
- ✅ ETS6-Bug gefixt
- ✅ DPT-Parsing robust
- ✅ Alle Sample-Files grün

**M7: Alter Code gelöscht (Tag 16)**
- ✅ Infrastructure-Parser entfernt
- ✅ NUR Library-Implementation
- ✅ Keine Code-Duplication

**M8: Dokumentiert (Tag 17)**
- ✅ README geschrieben
- ✅ Test-Report erstellt
- ✅ Migration-Guide verfügbar

---

## 8. Risiko-Management

### 8.1 Risiko-Matrix

| Risiko | Wahrscheinlichkeit | Impact | Mitigation |
|--------|-------------------|--------|------------|
| Wizard bricht | Niedrig | Hoch | Adapter-Pattern, Feature-Flag, Tests |
| Sample-Files fehlen | Mittel | Mittel | xknxproject-Samples, eigene Projekte |
| ETS-Varianten | Mittel | Mittel | Strategy-Pattern, Fallbacks |
| Performance-Regression | Niedrig | Mittel | Performance-Tests, Profiling |
| Zeitüberschreitung | Mittel | Niedrig | Puffer eingeplant, Phasen priorisiert |

### 8.2 Risiken im Detail

#### Risiko 1: Wizard-Breaking-Changes
**Wahrscheinlichkeit:** Niedrig
**Impact:** Hoch (Produktions-Feature)

**Mitigation:**
- ✅ Adapter-Pattern: Interface bleibt identisch
- ✅ Parallele Implementation: Alter Parser als Fallback
- ✅ Feature-Flag: Schrittweiser Rollout
- ✅ Integration-Tests: Wizard-Flow testen
- ✅ Manual E2E-Tests: Vor Cut-Over

**Rollback-Plan:**
- Feature-Flag zurücksetzen
- Alter Parser übernimmt
- Library-Code hat keine Auswirkung

#### Risiko 2: Sample-Files unvollständig
**Wahrscheinlichkeit:** Mittel
**Impact:** Mittel (Test-Coverage)

**Mitigation:**
- ✅ xknxproject-Samples als Basis (bewährt)
- ✅ Dein ETS5-Projekt als Hauptreferenz
- ✅ Batch-Test zeigt Coverage-Gaps
- ✅ Iterativ weitere Samples hinzufügen

**Fallback:**
- Fokus auf vorhandene Samples
- Gap-Analysis dokumentieren
- Schrittweise Erweiterung

#### Risiko 3: ETS-Version-Varianten
**Wahrscheinlichkeit:** Mittel
**Impact:** Mittel (Kompatibilität)

**Mitigation:**
- ✅ Strategy-Pattern: Einfache Erweiterung
- ✅ Namespace-Detection: Robust
- ✅ Fallback-Mechanismen
- ✅ Logging für unbekannte Strukturen

**Handling:**
- Unknown-Version: Warnung, Best-Effort-Parse
- Neue ETS-Version: Neue Loader-Klasse

#### Risiko 4: Performance-Regression
**Wahrscheinlichkeit:** Niedrig
**Impact:** Mittel (User-Experience)

**Mitigation:**
- ✅ Performance-Tests mit CLI-Tool
- ✅ Profiling (vor/nach)
- ✅ Streaming-Parsing-Optionen
- ✅ Hardware.xml-Caching

**Benchmark-Targets:**
- Small project (<50 GAs): <1s
- Medium project (200 GAs): <3s
- Large project (1000 GAs): <10s

#### Risiko 5: Zeitüberschreitung
**Wahrscheinlichkeit:** Mittel
**Impact:** Niedrig (Plan hat Puffer)

**Mitigation:**
- ✅ Puffer: +2 Tage eingeplant
- ✅ Phasen priorisiert (Critical Path zuerst)
- ✅ Optional-Features klar markiert
- ✅ Daily Stand-Up für Tracking

**Kritischer Pfad:**
1. Library-Grundgerüst (MUSS)
2. Parser migrieren (MUSS)
3. Adapter-Layer (MUSS)
4. Validation (MUSS)

Optional (kann verschoben werden):
- CLI-Tool (Nice-to-Have)
- Erweiterte Features (COs, Topology)
- Umfassende Doku

---

## 9. Erfolgs-Kriterien

### 9.1 Funktionale Kriterien

**MUSS (Critical):**
- ✅ Dein ETS5-Projekt wird perfekt geparst (gleiche Results)
- ✅ Wizard funktioniert unverändert (keine Breaking Changes)
- ✅ Mind. 12 Sample-Files erfolgreich geparst
- ✅ ETS 4, 5, 6 Support mit/ohne Passwort
- ✅ ETS6-Devices-Bug gefixt
- ✅ Alter Parser-Code vollständig entfernt

**SOLL (Important):**
- ✅ CLI-Tool mit Batch-Command funktioniert
- ✅ Test-Coverage >80%
- ✅ Gap-Report vs. xknxproject erstellt
- ✅ Performance mindestens gleich oder besser

**KANN (Nice-to-Have):**
- ⚠️ Communication Objects geparst (nicht im Wizard genutzt)
- ⚠️ Topology extrahiert (optional)
- ⚠️ NuGet-Package erstellt

### 9.2 Qualitäts-Kriterien

**Code-Qualität:**
- ✅ Keine Code-Duplication (alter Parser gelöscht)
- ✅ Klare Separation of Concerns
- ✅ SOLID-Prinzipien eingehalten
- ✅ Logging an kritischen Stellen

**Test-Qualität:**
- ✅ Unit-Tests: >50 Tests
- ✅ Integration-Tests: >15 Tests
- ✅ Coverage: >80% für Library-Code
- ✅ Parametrisierte Tests für alle ETS-Versionen

**Architektur-Qualität:**
- ✅ Library standalone nutzbar (kein EF-Dependency)
- ✅ Erweiterbar: Neue ETS-Version = neue Loader-Klasse
- ✅ Dependency Injection Support
- ✅ Dokumentierte Extension-Points

### 9.3 Dokumentations-Kriterien

**MUSS:**
- ✅ Library README.md (Usage, Installation)
- ✅ CLI-Tool README.md (Commands, Examples)
- ✅ Test-Report mit Results

**SOLL:**
- ✅ Migration-Guide (alter → neuer Parser)
- ✅ Gap-Analysis vs. xknxproject
- ✅ Architecture-Diagramm

**KANN:**
- ⚠️ API-Dokumentation (XML-Comments)
- ⚠️ NuGet-Package-Beschreibung

---

## 10. Nächste Schritte

### 10.1 Sofort nach Plan-Approval

1. **Feature-Branch erstellen**
   ```bash
   git checkout -b feature/parser-library-extraction
   ```

2. **Phase 1 starten: Library-Projekt**
   ```bash
   cd backend
   dotnet new classlib -n KnxMonitor.ProjectParser -f net9.0
   ```

3. **Todo-List erstellen** (für Tracking)

4. **Daily Stand-Up** einrichten (kurzes Update)

### 10.2 Nach jeder Phase

- ✅ **Code-Review** (selbst oder Team)
- ✅ **Tests ausführen** (grün?)
- ✅ **Commit** mit aussagekräftiger Message
- ✅ **Status-Update** (welche Phase abgeschlossen)

### 10.3 Nach Phase 7 (Validation)

**Go/No-Go-Entscheidung:**

**GO wenn:**
- Dein ETS5-Projekt 100% funktioniert
- Wizard E2E-Tests erfolgreich
- Mind. 80% Sample-Files grün
- Keine kritischen Bugs

**NO-GO wenn:**
- Kritische Bugs
- Wizard-Regression
- Performance-Probleme

**Bei GO:**
- ✅ Alter Parser-Code LÖSCHEN
- ✅ Merge nach main
- ✅ Deployment

**Bei NO-GO:**
- 🔧 Bugs fixen
- 🔄 Erneut validieren
- ⏱️ Ggf. Timeline anpassen

---

## 11. Anhang

### 11.1 Referenzen

- **xknxproject:** https://github.com/XKNX/xknxproject
- **Sample-Files:** `D:\Source\knx-ng-monitor\docs\samples\`
- **xknxproject-main:** `D:\Source\xknxproject-main\`
- **ETS Specs:** (intern)

### 11.2 Bestehende Files

- **Parser:** `backend/KnxMonitor.Infrastructure/Services/KnxProjectParserService.cs`
- **Detector:** `backend/KnxMonitor.Infrastructure/Services/ProjectFeatureDetector.cs`
- **Wizard:** `frontend/src/app/features/projects/import-wizard/`
- **API:** `backend/KnxMonitor.Api/Controllers/ProjectsController.cs`

### 11.3 Tools & Dependencies

**Library:**
- .NET 9
- System.IO.Compression
- ICSharpCode.SharpZipLib (PW-Support)
- Microsoft.Extensions.Logging.Abstractions

**Tests:**
- xUnit
- FluentAssertions
- Moq

**CLI-Tool:**
- System.CommandLine
- Microsoft.Extensions.Logging.Console

### 11.4 Glossar

- **GA:** Group Address
- **DPT:** Datapoint Type (KNX Datentyp)
- **CO:** Communication Object
- **ETS:** Engineering Tool Software (KNX-Projektierungs-Tool)
- **Secure:** KNX Secure (verschlüsselte KNX-Kommunikation)
- **Topology:** Hierarchische Struktur (Area → Line → Device)

---

**Plan erstellt:** 2025-10-31
**Version:** 1.0
**Status:** ✅ Ready for Implementation

**Let's build it! 🚀**
