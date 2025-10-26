using System.IO.Compression;
using System.Xml.Linq;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;

namespace KnxMonitor.Infrastructure.Services;

public class KnxProjectParserService : IKnxProjectParserService
{
    public async Task<(List<GroupAddress> GroupAddresses, List<Device> Devices)> ParseProjectFileAsync(Stream fileStream, int projectId)
    {
        var groupAddresses = new List<GroupAddress>();
        var devices = new List<Device>();

        try
        {
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);

            // Find the project file (P-xxxx/0.xml contains group addresses and topology)
            var projectEntry = archive.Entries.FirstOrDefault(e =>
                e.FullName.Contains("/0.xml") && e.FullName.StartsWith("P-"));

            if (projectEntry == null)
            {
                throw new InvalidOperationException("Invalid .knxproj file: No project data found");
            }

            using var stream = projectEntry.Open();
            var doc = await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);

            // Define XML namespaces (works for both ETS5 and ETS6)
            XNamespace knx = "http://knx.org/xml/project/20";

            // Try alternative namespace if first one doesn't work
            if (doc.Root?.Name.Namespace.NamespaceName != knx.NamespaceName)
            {
                knx = doc.Root?.Name.Namespace ?? knx;
            }

            // Parse Group Addresses
            var groupAddressElements = doc.Descendants(knx + "GroupAddress");

            foreach (var gaElement in groupAddressElements)
            {
                var address = gaElement.Attribute("Address")?.Value;
                var name = gaElement.Attribute("Name")?.Value;
                var description = gaElement.Attribute("Description")?.Value;
                var dptType = gaElement.Attribute("DatapointType")?.Value;

                if (!string.IsNullOrEmpty(address) && !string.IsNullOrEmpty(name))
                {
                    // Convert KNX address format (integer) to readable format (x/y/z)
                    var formattedAddress = FormatKnxAddress(address);

                    groupAddresses.Add(new GroupAddress
                    {
                        ProjectId = projectId,
                        Address = formattedAddress,
                        Name = name,
                        Description = description,
                        DatapointType = ParseDptType(dptType),
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            // Parse Devices
            var deviceElements = doc.Descendants(knx + "DeviceInstance");

            foreach (var deviceElement in deviceElements)
            {
                var name = deviceElement.Attribute("Name")?.Value;
                var address = deviceElement.Attribute("Address")?.Value;
                var productRefId = deviceElement.Attribute("ProductRefId")?.Value;

                if (!string.IsNullOrEmpty(address) && !string.IsNullOrEmpty(name))
                {
                    // Find product information
                    var productName = string.Empty;
                    var manufacturer = string.Empty;

                    if (!string.IsNullOrEmpty(productRefId))
                    {
                        // Try to find product details in manufacturer data
                        var manufacturerEntry = archive.Entries.FirstOrDefault(e =>
                            e.FullName.Contains("M-") && e.FullName.EndsWith("/Hardware.xml"));

                        if (manufacturerEntry != null)
                        {
                            using var mStream = manufacturerEntry.Open();
                            var mDoc = await XDocument.LoadAsync(mStream, LoadOptions.None, CancellationToken.None);
                            var mKnx = mDoc.Root?.Name.Namespace ?? knx;

                            var product = mDoc.Descendants(mKnx + "Product")
                                .FirstOrDefault(p => p.Attribute("Id")?.Value == productRefId);

                            if (product != null)
                            {
                                productName = product.Attribute("Text")?.Value ?? string.Empty;

                                var manufacturerElement = mDoc.Descendants(mKnx + "Manufacturer").FirstOrDefault();
                                manufacturer = manufacturerElement?.Attribute("Name")?.Value ?? string.Empty;
                            }
                        }
                    }

                    devices.Add(new Device
                    {
                        ProjectId = projectId,
                        Name = name,
                        PhysicalAddress = FormatPhysicalAddress(address),
                        Manufacturer = manufacturer,
                        ProductName = productName
                    });
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse .knxproj file: {ex.Message}", ex);
        }

        return (groupAddresses, devices);
    }

    private static string FormatKnxAddress(string address)
    {
        if (int.TryParse(address, out var intAddress))
        {
            // Convert integer to x/y/z format
            var main = (intAddress >> 11) & 0x1F;
            var middle = (intAddress >> 8) & 0x07;
            var sub = intAddress & 0xFF;
            return $"{main}/{middle}/{sub}";
        }
        return address;
    }

    private static string FormatPhysicalAddress(string address)
    {
        if (int.TryParse(address, out var intAddress))
        {
            // Convert integer to x.y.z format
            var area = (intAddress >> 12) & 0x0F;
            var line = (intAddress >> 8) & 0x0F;
            var device = intAddress & 0xFF;
            return $"{area}.{line}.{device}";
        }
        return address;
    }

    private static string? ParseDptType(string? dptType)
    {
        if (string.IsNullOrEmpty(dptType))
            return null;

        // Extract DPT number from various formats
        // Examples: "DPT-1", "DPST-1-1", "1.001"
        if (dptType.Contains("DPST-"))
        {
            var parts = dptType.Replace("DPST-", "").Split('-');
            if (parts.Length >= 2)
                return $"DPT {parts[0]}.{parts[1].PadLeft(3, '0')}";
        }
        else if (dptType.Contains("DPT-"))
        {
            return $"DPT {dptType.Replace("DPT-", "")}";
        }
        else if (dptType.Contains("."))
        {
            return $"DPT {dptType}";
        }

        return dptType;
    }
}
