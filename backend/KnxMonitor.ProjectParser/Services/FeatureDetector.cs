using System.IO.Compression;
using System.Xml.Linq;
using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Core.Interfaces;
using KnxMonitor.ProjectParser.Core.Models;
using Microsoft.Extensions.Logging;

namespace KnxMonitor.ProjectParser.Services;

public class FeatureDetector : IFeatureDetector
{
    private readonly ILogger<FeatureDetector> _logger;

    public FeatureDetector(ILogger<FeatureDetector> logger)
    {
        _logger = logger;
    }

    public async Task<ProjectFeatures> DetectAfterUnlockAsync(
        Stream stream,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var features = await DetectAsync(stream, cancellationToken);

        if (!features.HasPassword || string.IsNullOrEmpty(password))
        {
            return features;
        }

        // Re-scan inside the decrypted archive: KNX Secure indicators live in the inner 0.xml
        stream.Position = 0;
        var files = await ZipHandler.LoadAsync(stream, features, password, cancellationToken: cancellationToken);

        var projectXmlPath = files.FindFirstByName("0.xml");
        if (projectXmlPath == null) return features;

        try
        {
            await using var xmlStream = files.OpenRead(projectXmlPath);
            var xml = await XDocument.LoadAsync(xmlStream, LoadOptions.None, cancellationToken);
            var ns = xml.Root?.Name.Namespace ?? XNamespace.None;

            var secureGAs = xml.Descendants(ns + "GroupAddress")
                .Any(ga => ga.Attribute("DataSecurity")?.Value == "1");

            if (secureGAs)
            {
                features.HasKnxSecure = true;
                return features;
            }

            // ETS keeps Security blocks (ToolKey / DeviceAuthenticationCode / ManagementPassword)
            // anywhere under DeviceInstance / Line / Project. Any such block with attributes
            // means the project carries KNX Secure material.
            features.HasKnxSecure = xml.Descendants(ns + "Security")
                .Any(s => s.HasAttributes);
        }
        catch
        {
            // best-effort; preserve previous flag
        }

        return features;
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
            features.EtsVersion = await DetectEtsVersionAsync(archive, cancellationToken);

            // 4. Check for KNX Secure
            features.HasKnxSecure = await CheckKnxSecureAsync(archive, features, cancellationToken);

            // 5. Detect Addressing Style
            features.AddressingStyle = await DetectAddressingStyleAsync(archive, features, cancellationToken);

            _logger.LogInformation(
                "Detected: ETS {Version}, Password={HasPw}, Secure={HasSecure}, Style={Style}",
                features.EtsVersion, features.HasPassword, features.HasKnxSecure, features.AddressingStyle
            );

            return features;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect features");
            throw;
        }
    }

    private async Task<EtsVersion> DetectEtsVersionAsync(ZipArchive archive, CancellationToken cancellationToken)
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
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line == null) break;

                if (line.Contains("http://knx.org/xml/project/11"))
                    return EtsVersion.Ets4;
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
            var xml = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
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
        ProjectFeatures features,
        CancellationToken cancellationToken)
    {
        // Finde 0.xml im Projekt
        var projectFile = $"{features.ProjectId}/0.xml";
        var entry = archive.GetEntry(projectFile);

        if (entry == null)
            return false;

        try
        {
            await using var stream = entry.Open();
            var xml = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
            var ns = xml.Root?.Name.Namespace ?? XNamespace.None;

            // Check for Group Addresses with DataSecurity enabled
            var secureGAs = xml.Descendants(ns + "GroupAddress")
                .Any(ga => ga.Attribute("DataSecurity")?.Value == "1");

            if (secureGAs)
                return true;

            // Check for Devices with Security attributes
            var secureDevices = xml.Descendants(ns + "DeviceInstance")
                .Any(d => d.Attribute("IsCoupler")?.Value == "true" &&
                         d.Parent?.Elements(ns + "Security").Any() == true);

            return secureDevices;
        }
        catch
        {
            // If XML parsing fails, fall back to conservative approach
            return false;
        }
    }

    private async Task<AddressingStyle> DetectAddressingStyleAsync(
        ZipArchive archive,
        ProjectFeatures features,
        CancellationToken cancellationToken)
    {
        // Finde project.xml im Projekt
        var projectFile = $"{features.ProjectId}/project.xml";
        var entry = archive.GetEntry(projectFile);

        if (entry == null)
        {
            // Fallback: Default to ThreeLevel
            return AddressingStyle.ThreeLevel;
        }

        try
        {
            await using var stream = entry.Open();
            var xml = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);

            // Look for GroupAddressStyle attribute in Project element
            // Examples: GroupAddressStyle="ThreeLevel", "TwoLevel", or "Free"
            var styleAttr = xml.Root?.Attribute("GroupAddressStyle")?.Value;

            if (string.IsNullOrEmpty(styleAttr))
            {
                // Default to ThreeLevel if not specified
                return AddressingStyle.ThreeLevel;
            }

            // Map XML value to enum
            return styleAttr.ToLowerInvariant() switch
            {
                "threelevel" or "3level" => AddressingStyle.ThreeLevel,
                "twolevel" or "2level" => AddressingStyle.TwoLevel,
                "free" or "freelevel" => AddressingStyle.Free,
                _ => AddressingStyle.ThreeLevel // Default fallback
            };
        }
        catch
        {
            // If XML parsing fails, fall back to ThreeLevel
            return AddressingStyle.ThreeLevel;
        }
    }
}
