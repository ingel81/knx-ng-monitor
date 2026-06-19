using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Core.Models;
using Microsoft.Extensions.Logging;
using LibraryParser = KnxMonitor.ProjectParser.Core.Interfaces.IProjectParser;
using LibraryKeyringReader = KnxMonitor.ProjectParser.Core.Interfaces.IKeyringReader;
using LibraryParserOptions = KnxMonitor.ProjectParser.Core.Models.ParserOptions;
using LibraryParserProgress = KnxMonitor.ProjectParser.Core.Models.ParserProgress;
using LibraryGroupAddress = KnxMonitor.ProjectParser.Core.Models.GroupAddress;
using LibraryDevice = KnxMonitor.ProjectParser.Core.Models.Device;
using LibraryLocation = KnxMonitor.ProjectParser.Core.Models.Location;
using LibraryComObject = KnxMonitor.ProjectParser.Core.Models.ComObject;
using LibraryGroupRange = KnxMonitor.ProjectParser.Core.Models.GroupRange;
using LibraryKeyringData = KnxMonitor.ProjectParser.Core.Models.KeyringData;

namespace KnxMonitor.Infrastructure.Services;

/// <summary>
/// Adapter: Delegiert zu ProjectParser Library
/// KEIN Parser-Code mehr hier!
/// </summary>
public class KnxProjectParserService : IKnxProjectParserService
{
    private readonly LibraryParser _parser;
    private readonly LibraryKeyringReader _keyringReader;
    private readonly ILogger<KnxProjectParserService> _logger;

    public KnxProjectParserService(
        LibraryParser parser,
        LibraryKeyringReader keyringReader,
        ILogger<KnxProjectParserService> logger)
    {
        _parser = parser;
        _keyringReader = keyringReader;
        _logger = logger;
    }

    public async Task<ParsedProjectData> ParseProjectFileAsync(
        Stream fileStream,
        int projectId)
    {
        // Legacy method - calls new method without context
        var context = new ImportContext
        {
            FileName = "project.knxproj",
            ProjectPassword = null
        };

        return await ParseProjectFileAsync(fileStream, projectId, context);
    }

    public async Task<ParsedProjectData> ParseProjectFileAsync(
        Stream fileStream,
        int projectId,
        ImportContext context)
    {
        try
        {
            _logger.LogInformation("Parsing KNX project via Library (Project ID: {ProjectId})", projectId);

            // 1. Parse mit Library (inkl. optionalem Keyring)
            MemoryStream? keyringStream = null;
            if (context.KeyringFile != null && context.KeyringFile.Length > 0)
            {
                keyringStream = new MemoryStream(context.KeyringFile, writable: false);
            }

            try
            {
                var result = await _parser.ParseAsync(
                    fileStream,
                    new LibraryParserOptions
                    {
                        Password = context.ProjectPassword,
                        KeyringStream = keyringStream,
                        KeyringPassword = context.KeyringPassword,
                        ValidateStructure = true,
                        IncludeEmptyDevices = false,
                        ThrowOnWarnings = false
                    },
                    MapProgressToLibrary(context.ProgressCallback)
                );

                // 2. Map Library-DTOs zu Infrastructure-Entities
                var parsed = new ParsedProjectData
                {
                    GroupAddresses = result.GroupAddresses
                        .Select(dto => MapGroupAddressToEntity(dto, projectId))
                        .ToList(),
                    Devices = result.Devices
                        .Select(dto => MapDeviceToEntity(dto, projectId))
                        .ToList(),
                    Locations = result.Locations
                        .Select(dto => MapLocationToEntity(dto, projectId))
                        .ToList(),
                    CommunicationObjects = result.CommunicationObjects
                        .SelectMany(dto => MapComObjectToEntities(dto, projectId))
                        .ToList(),
                    GroupRanges = result.GroupRanges
                        .Select(dto => MapGroupRangeToEntity(dto, projectId))
                        .ToList(),
                    KeyringKeys = result.Keyring != null
                        ? MapKeyringToEntities(result.Keyring, projectId)
                        : new List<ProjectKeyringKey>()
                };

                _logger.LogInformation(
                    "Parsed: {GAs} GAs, {Devices} Devices, {Locations} Locations, {ComObjects} ComObjects, {GroupRanges} GroupRanges in {Duration}ms",
                    parsed.GroupAddresses.Count,
                    parsed.Devices.Count,
                    parsed.Locations.Count,
                    parsed.CommunicationObjects.Count,
                    parsed.GroupRanges.Count,
                    result.Statistics.Duration.TotalMilliseconds
                );

                return parsed;
            }
            finally
            {
                keyringStream?.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse project");
            throw new InvalidOperationException($"Failed to parse .knxproj file: {ex.Message}", ex);
        }
    }

    public async Task<List<ProjectKeyringKey>> ParseKeyringAsync(
        byte[] keyringData,
        string keyringPassword,
        int projectId)
    {
        try
        {
            using var stream = new MemoryStream(keyringData, writable: false);
            var keyring = await _keyringReader.ReadAsync(stream, keyringPassword);
            return MapKeyringToEntities(keyring, projectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse keyring for project {ProjectId}", projectId);
            throw new InvalidOperationException($"Failed to parse keyring file: {ex.Message}", ex);
        }
    }

    // Mapping: Library DTO → Infrastructure Entity
    private GroupAddress MapGroupAddressToEntity(LibraryGroupAddress dto, int projectId)
    {
        return new GroupAddress
        {
            ProjectId = projectId,
            Address = dto.Address,
            Name = dto.Name,
            Description = dto.Description,
            DatapointType = dto.DatapointType?.OriginalString,
            CreatedAt = DateTime.UtcNow
        };
    }

    private Device MapDeviceToEntity(LibraryDevice dto, int projectId)
    {
        return new Device
        {
            ProjectId = projectId,
            Name = dto.Name,
            PhysicalAddress = dto.PhysicalAddress,
            Manufacturer = dto.Manufacturer,
            ProductName = dto.ProductName
        };
    }

    private Location MapLocationToEntity(LibraryLocation dto, int projectId)
    {
        return new Location
        {
            ProjectId = projectId,
            ExternalId = dto.Id,
            Name = dto.Name,
            Type = dto.Type,
            ParentExternalId = dto.ParentId,
            DeviceAddresses = dto.DeviceRefIds.Count > 0 ? string.Join(",", dto.DeviceRefIds) : null,
            GroupAddresses = dto.GroupAddressRefIds.Count > 0 ? string.Join(",", dto.GroupAddressRefIds) : null
        };
    }

    // One library ComObject can link multiple GAs; store one row per (device, GA-link) pair.
    private IEnumerable<CommunicationObject> MapComObjectToEntities(LibraryComObject dto, int projectId)
    {
        foreach (var link in dto.GroupAddressLinks)
        {
            yield return new CommunicationObject
            {
                ProjectId = projectId,
                DeviceAddress = dto.DeviceAddress,
                Number = dto.Number,
                Name = dto.Name,
                FunctionText = dto.FunctionText,
                GroupAddressLink = link,
                DatapointType = dto.DatapointType,
                Flags = dto.Flags
            };
        }
    }

    private GroupRange MapGroupRangeToEntity(LibraryGroupRange dto, int projectId)
    {
        return new GroupRange
        {
            ProjectId = projectId,
            Name = dto.Name,
            RangeStart = dto.RangeStart,
            RangeEnd = dto.RangeEnd
        };
    }

    private List<ProjectKeyringKey> MapKeyringToEntities(LibraryKeyringData keyring, int projectId)
    {
        var keys = new List<ProjectKeyringKey>();

        if (keyring.BackboneKey != null)
        {
            keys.Add(new ProjectKeyringKey
            {
                ProjectId = projectId,
                KeyType = "Backbone",
                IndividualAddress = keyring.BackboneMulticastAddress,
                KeyBase64 = Convert.ToBase64String(keyring.BackboneKey)
            });
        }

        foreach (var device in keyring.Devices.Where(d => d.ToolKey != null))
        {
            keys.Add(new ProjectKeyringKey
            {
                ProjectId = projectId,
                KeyType = "ToolKey",
                IndividualAddress = device.IndividualAddress,
                KeyBase64 = Convert.ToBase64String(device.ToolKey!)
            });
        }

        foreach (var ga in keyring.GroupAddresses.Where(g => g.Key != null))
        {
            keys.Add(new ProjectKeyringKey
            {
                ProjectId = projectId,
                KeyType = "GroupAddress",
                GroupAddress = ga.Address,
                KeyBase64 = Convert.ToBase64String(ga.Key!)
            });
        }

        return keys;
    }

    // Progress-Mapping: Infrastructure → Library
    private IProgress<LibraryParserProgress>? MapProgressToLibrary(
        Action<string, int>? progressCallback)
    {
        if (progressCallback == null) return null;

        return new Progress<LibraryParserProgress>(p =>
        {
            var stepName = MapStepToString(p.Step);
            progressCallback(stepName, p.PercentComplete);
        });
    }

    private string MapStepToString(KnxMonitor.ProjectParser.Core.Enums.ParseStep step)
    {
        return step switch
        {
            KnxMonitor.ProjectParser.Core.Enums.ParseStep.OpenZip => "OpenZip",
            KnxMonitor.ProjectParser.Core.Enums.ParseStep.CheckPassword => "CheckPassword",
            KnxMonitor.ProjectParser.Core.Enums.ParseStep.DetectFeatures => "DetectFeatures",
            KnxMonitor.ProjectParser.Core.Enums.ParseStep.ParseGroupAddresses => "ParseGroupAddresses",
            KnxMonitor.ProjectParser.Core.Enums.ParseStep.ParseDevices => "ParseDevices",
            KnxMonitor.ProjectParser.Core.Enums.ParseStep.ParseCommunicationObjects => "ParseCommunicationObjects",
            KnxMonitor.ProjectParser.Core.Enums.ParseStep.ParseTopology => "ParseTopology",
            KnxMonitor.ProjectParser.Core.Enums.ParseStep.ParseLocations => "ParseLocations",
            KnxMonitor.ProjectParser.Core.Enums.ParseStep.ParseFunctions => "ParseFunctions",
            KnxMonitor.ProjectParser.Core.Enums.ParseStep.Validate => "Validate",
            KnxMonitor.ProjectParser.Core.Enums.ParseStep.Complete => "Complete",
            _ => "Unknown"
        };
    }
}
