using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Core.Interfaces;
using KnxMonitor.ProjectParser.Core.Models;
using KnxMonitor.ProjectParser.Helpers;
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
            features.HasKnxSecure = await CheckKnxSecureAsync(archive, cancellationToken);

            // 5. Detect Addressing Style
            features.AddressingStyle = await DetectAddressingStyleAsync(archive, cancellationToken);

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
        // 1. knx_master.xml — the only version marker that is readable in EVERY project, including
        //    password-protected ones (project.xml / 0.xml live inside the encrypted P-XXXX.zip).
        //    Its root namespace carries the schema number, which is mapped by numeric range so new
        //    ETS releases (schema 22, 23, …) keep working instead of falling through to Unknown.
        var knxMasterEntry = archive.Entries
            .FirstOrDefault(e => e.Name.Equals("knx_master.xml", StringComparison.OrdinalIgnoreCase));

        if (knxMasterEntry != null)
        {
            await using var stream = knxMasterEntry.Open();
            var rootNamespace = await ReadRootNamespaceAsync(stream, cancellationToken);
            var version = EtsSchemaVersion.FromNamespace(rootNamespace);

            if (version != EtsVersion.Unknown)
            {
                _logger.LogInformation(
                    "ETS version {Version} detected from knx_master.xml (schema {Schema})",
                    version, EtsSchemaVersion.ParseSchemaVersion(rootNamespace));
                return version;
            }
        }

        // 2. Fallback: project.xml, then 0.xml — namespace first, then the ToolVersion / CreatedBy
        //    attributes (both only exist on the project files, never on knx_master.xml).
        foreach (var entry in EnumerateVersionCandidates(archive))
        {
            try
            {
                await using var stream = entry.Open();
                var xml = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);

                var version = ProjectXmlMetadata.ReadEtsVersion(xml.Root);
                if (version != EtsVersion.Unknown)
                {
                    _logger.LogInformation(
                        "ETS version {Version} detected from {Entry}", version, entry.FullName);
                    return version;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read ETS version from {Entry}", entry.FullName);
            }
        }

        _logger.LogWarning("Could not determine ETS version from archive");
        return EtsVersion.Unknown;
    }

    private static IEnumerable<ZipArchiveEntry> EnumerateVersionCandidates(ZipArchive archive)
    {
        return archive.Entries
            .Where(e => e.Name.Equals("project.xml", StringComparison.OrdinalIgnoreCase))
            .Concat(archive.Entries
                .Where(e => e.Name.Equals("0.xml", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Read only the namespace of the document's root element. knx_master.xml is several MB — parsing
    /// it into an XDocument just to look at the root would be wasteful, so this stops after the first
    /// element node. Returns null for non-XML / truncated content.
    /// </summary>
    private static async Task<string?> ReadRootNamespaceAsync(Stream stream, CancellationToken cancellationToken)
    {
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            IgnoreProcessingInstructions = true,
            CloseInput = false
        };

        try
        {
            using var reader = XmlReader.Create(stream, settings);
            while (await reader.ReadAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType == XmlNodeType.Element)
                    return reader.NamespaceURI;
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Malformed knx_master.xml — fall through to the project.xml / 0.xml candidates.
        }

        return null;
    }

    private static async Task<bool> CheckKnxSecureAsync(
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        // Look up 0.xml by entry name instead of the "{ProjectId}/0.xml" path: ZipArchive.GetEntry is
        // case-sensitive and ProjectId is null when the archive carries no P-XXXX.signature, which
        // would skip the KNX Secure check altogether.
        var entry = archive.Entries
            .FirstOrDefault(e => e.Name.Equals("0.xml", StringComparison.OrdinalIgnoreCase));

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
        CancellationToken cancellationToken)
    {
        // project.xml first, then 0.xml. Looked up by entry NAME rather than by the
        // "{ProjectId}/project.xml" path: ETS4 writes "P-XXXX/Project.xml" with a capital P, and
        // ZipArchive.GetEntry is case-sensitive, so the old path lookup missed those projects
        // entirely and silently returned the ThreeLevel default.
        foreach (var entry in EnumerateVersionCandidates(archive))
        {
            try
            {
                await using var stream = entry.Open();
                var xml = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);

                var style = ProjectXmlMetadata.ReadAddressingStyle(xml.Root);
                if (style.HasValue)
                {
                    _logger.LogInformation(
                        "Addressing style {Style} detected from {Entry}", style.Value, entry.FullName);
                    return style.Value;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read addressing style from {Entry}", entry.FullName);
            }
        }

        // Nothing readable (e.g. password-protected project) — ETS's own default. ProjectParser
        // re-reads the style from the decrypted files before the loader converts any address.
        return AddressingStyle.ThreeLevel;
    }
}
