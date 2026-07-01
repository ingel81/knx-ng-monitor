using System.Xml.Linq;
using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Core.Models;
using KnxMonitor.ProjectParser.Helpers;
using Microsoft.Extensions.Logging;

namespace KnxMonitor.ProjectParser.Loaders;

public class Ets5ProjectLoader : BaseProjectLoader
{
    public override EtsVersion SupportedVersion => EtsVersion.Ets5;

    public Ets5ProjectLoader(ILogger<Ets5ProjectLoader> logger) : base(logger) { }

    protected override Task<XDocument> LoadProjectXmlAsync(
        ProjectFileMap files,
        IProgress<ParserProgress>? progress,
        CancellationToken cancellationToken)
    {
        return LoadProjectXmlFromFilesAsync(files, cancellationToken);
    }

    protected override Task<List<GroupAddress>> ParseGroupAddressesAsync(
        XDocument projectXml,
        AddressingStyle addressingStyle,
        CancellationToken cancellationToken)
    {
        var ns = GetNamespace(projectXml);
        var groupAddresses = new List<GroupAddress>();

        var gaElements = projectXml.Descendants(ns + "GroupAddress");

        foreach (var element in gaElements)
        {
            var addressValue = element.Attribute("Address")?.Value;
            if (!int.TryParse(addressValue, out var rawAddress))
            {
                _logger.LogWarning(
                    "Skipping group address with malformed Address attribute '{Address}' (Id={Id})",
                    addressValue, element.Attribute("Id")?.Value);
                continue;
            }

            var dptString = element.Attribute("DatapointType")?.Value;

            var ga = new GroupAddress
            {
                RawAddress = rawAddress,
                Address = AddressConverter.ToGroupAddressString(rawAddress, addressingStyle),
                Name = element.Attribute("Name")?.Value ?? string.Empty,
                Description = element.Attribute("Description")?.Value,
                DatapointType = DptInfo.TryParse(dptString),
                Identifier = element.Attribute("Id")?.Value
            };

            groupAddresses.Add(ga);
        }

        _logger.LogInformation("Parsed {Count} group addresses", groupAddresses.Count);
        return Task.FromResult(groupAddresses);
    }

    protected override async Task<List<Device>> ParseDevicesAsync(
        ProjectFileMap files,
        XDocument projectXml,
        CancellationToken cancellationToken)
    {
        var ns = GetNamespace(projectXml);
        var devices = new List<Device>();

        var hardwareData = await LoadHardwareDataAsync(files, cancellationToken);

        var deviceElements = projectXml
            .Descendants(ns + "DeviceInstance")
            .Where(d => d.Attribute("Address") != null);

        foreach (var element in deviceElements)
        {
            var rawAddress = ComputeDeviceRawAddress(element, ns);
            var productRefId = element.Attribute("ProductRefId")?.Value;

            var device = new Device
            {
                RawAddress = rawAddress,
                PhysicalAddress = AddressConverter.ToPhysicalAddressString(rawAddress),
                Name = element.Attribute("Name")?.Value ?? string.Empty,
                ProductRefId = productRefId
            };

            if (productRefId != null && hardwareData.TryGetValue(productRefId, out var hwInfo))
            {
                device.Manufacturer = hwInfo.Manufacturer;
                device.ProductName = hwInfo.ProductName;
            }

            EnsureDeviceName(device);

            devices.Add(device);
        }

        _logger.LogInformation("Parsed {Count} devices", devices.Count);
        return devices;
    }
}
