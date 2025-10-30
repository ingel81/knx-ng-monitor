using System.Xml.Linq;
using KnxMonitor.Core.Interfaces;
using SharpZipLib = ICSharpCode.SharpZipLib.Zip;

namespace KnxMonitor.Infrastructure.Services;

public class KnxSecureService : IKnxSecureService
{
    public async Task<Dictionary<string, string>> ParseKeyringAsync(Stream keyringStream, string password)
    {
        var deviceKeys = new Dictionary<string, string>();

        try
        {
            // Keyring files are typically ZIP archives containing XML
            var zipFile = new SharpZipLib.ZipFile(keyringStream);
            zipFile.Password = password;

            foreach (SharpZipLib.ZipEntry entry in zipFile)
            {
                if (!entry.IsFile || !entry.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;

                using var entryStream = zipFile.GetInputStream(entry);
                var doc = await XDocument.LoadAsync(entryStream, LoadOptions.None, CancellationToken.None);

                // Parse keyring XML structure
                // This is a simplified implementation - actual KNX keyring format may vary
                var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

                var deviceEntries = doc.Descendants(ns + "Device");
                foreach (var device in deviceEntries)
                {
                    var individualAddress = device.Attribute("IndividualAddress")?.Value;
                    var toolKey = device.Attribute("ToolKey")?.Value;

                    if (!string.IsNullOrEmpty(individualAddress) && !string.IsNullOrEmpty(toolKey))
                    {
                        deviceKeys[individualAddress] = toolKey;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse keyring file: {ex.Message}", ex);
        }

        return deviceKeys;
    }
}
