using System.IO.Compression;
using System.Xml.Linq;
using KnxMonitor.Core.Enums;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Core.Models;

namespace KnxMonitor.Infrastructure.Services;

public class ProjectFeatureDetector : IProjectFeatureDetector
{
    public async Task<ProjectFeatures> DetectFeaturesAsync(Stream fileStream)
    {
        var features = new ProjectFeatures
        {
            EtsVersion = EtsVersion.Unknown,
            IsPasswordProtected = false,
            HasKnxSecureDevices = false,
            RequiresKeyring = false
        };

        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: true);

        // Check for nested ZIP (P-xxxx.zip) which indicates password protection
        var innerZipEntry = archive.Entries.FirstOrDefault(e =>
            e.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
            e.Name.StartsWith("P-", StringComparison.OrdinalIgnoreCase));

        if (innerZipEntry != null)
        {
            features.IsPasswordProtected = true;
            features.InnerZipFileName = innerZipEntry.Name;

            // Try to detect ETS version from the outer archive's knx_master.xml if available
            var masterXmlEntry = archive.Entries.FirstOrDefault(e => e.Name == "knx_master.xml");
            if (masterXmlEntry != null)
            {
                using var masterStream = masterXmlEntry.Open();
                features.EtsVersion = await DetectEtsVersionFromXmlAsync(masterStream);
            }

            // Check for signature files (indicates secure/signed project)
            var hasSignature = archive.Entries.Any(e => e.Name.EndsWith(".signature"));
            var hasCertificate = archive.Entries.Any(e => e.Name.EndsWith(".certificate"));

            if (hasSignature || hasCertificate)
            {
                // This is a secure/signed project, might need keyring later
                features.HasKnxSecureDevices = true;
                features.RequiresKeyring = true;
            }
        }
        else
        {
            // No nested ZIP, try to find project XML directly (P-xxxx/0.xml)
            var projectEntry = archive.Entries.FirstOrDefault(e =>
                e.FullName.Contains("/0.xml") && e.FullName.StartsWith("P-"));

            if (projectEntry != null)
            {
                // Read into memory stream to allow multiple reads
                using var memoryStream = new MemoryStream();
                using (var stream = projectEntry.Open())
                {
                    await stream.CopyToAsync(memoryStream);
                }
                memoryStream.Position = 0;

                features.EtsVersion = await DetectEtsVersionFromXmlAsync(memoryStream);

                // Reset stream and check for KNX Secure devices
                memoryStream.Position = 0;
                features.HasKnxSecureDevices = await DetectKnxSecureDevicesAsync(memoryStream);
            }
        }

        return features;
    }

    private static async Task<EtsVersion> DetectEtsVersionFromXmlAsync(Stream xmlStream)
    {
        try
        {
            var doc = await XDocument.LoadAsync(xmlStream, LoadOptions.None, CancellationToken.None);

            // Check ToolVersion attribute
            var toolVersion = doc.Root?.Attribute("ToolVersion")?.Value;
            if (!string.IsNullOrEmpty(toolVersion))
            {
                if (toolVersion.StartsWith("4."))
                    return EtsVersion.Ets4;
                if (toolVersion.StartsWith("5."))
                    return EtsVersion.Ets5;
                if (toolVersion.StartsWith("6."))
                    return EtsVersion.Ets6;
            }

            // Check CreatedBy attribute
            var createdBy = doc.Root?.Attribute("CreatedBy")?.Value;
            if (!string.IsNullOrEmpty(createdBy))
            {
                if (createdBy.Contains("ETS4"))
                    return EtsVersion.Ets4;
                if (createdBy.Contains("ETS5"))
                    return EtsVersion.Ets5;
                if (createdBy.Contains("ETS6"))
                    return EtsVersion.Ets6;
            }

            // Check XML namespace
            var ns = doc.Root?.Name.Namespace.NamespaceName;
            if (ns == "http://knx.org/xml/project/20")
                return EtsVersion.Ets5; // ETS5 and ETS6 use this namespace

            if (ns == "http://knx.org/xml/project/14")
                return EtsVersion.Ets4;
        }
        catch
        {
            // If parsing fails, return Unknown
        }

        return EtsVersion.Unknown;
    }

    private static async Task<bool> DetectKnxSecureDevicesAsync(Stream xmlStream)
    {
        try
        {
            var doc = await XDocument.LoadAsync(xmlStream, LoadOptions.None, CancellationToken.None);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

            // Check if any DeviceInstance has a Security element
            var hasSecurityElement = doc.Descendants(ns + "DeviceInstance")
                .Any(d => d.Element(ns + "Security") != null);

            return hasSecurityElement;
        }
        catch
        {
            return false;
        }
    }
}
