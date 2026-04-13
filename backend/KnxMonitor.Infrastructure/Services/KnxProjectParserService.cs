using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Core.Models;
using Microsoft.Extensions.Logging;
using LibraryParser = KnxMonitor.ProjectParser.Core.Interfaces.IProjectParser;
using LibraryParserOptions = KnxMonitor.ProjectParser.Core.Models.ParserOptions;
using LibraryParserProgress = KnxMonitor.ProjectParser.Core.Models.ParserProgress;
using LibraryGroupAddress = KnxMonitor.ProjectParser.Core.Models.GroupAddress;
using LibraryDevice = KnxMonitor.ProjectParser.Core.Models.Device;

namespace KnxMonitor.Infrastructure.Services;

/// <summary>
/// Adapter: Delegiert zu ProjectParser Library
/// KEIN Parser-Code mehr hier!
/// </summary>
public class KnxProjectParserService : IKnxProjectParserService
{
    private readonly LibraryParser _parser;
    private readonly ILogger<KnxProjectParserService> _logger;

    public KnxProjectParserService(
        LibraryParser parser,
        ILogger<KnxProjectParserService> logger)
    {
        _parser = parser;
        _logger = logger;
    }

    public async Task<(List<GroupAddress> GroupAddresses, List<Device> Devices)> ParseProjectFileAsync(
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

    public async Task<(List<GroupAddress> GroupAddresses, List<Device> Devices)> ParseProjectFileAsync(
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
            var groupAddresses = result.GroupAddresses
                .Select(dto => MapGroupAddressToEntity(dto, projectId))
                .ToList();

            var devices = result.Devices
                .Select(dto => MapDeviceToEntity(dto, projectId))
                .ToList();

            _logger.LogInformation(
                "Parsed: {GAs} GAs, {Devices} Devices in {Duration}ms",
                groupAddresses.Count,
                devices.Count,
                result.Statistics.Duration.TotalMilliseconds
            );

                return (groupAddresses, devices);
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
