using System.Xml.Linq;
using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Core.Interfaces;
using KnxMonitor.ProjectParser.Core.Models;
using Microsoft.Extensions.Logging;

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
        ProjectFileMap files,
        ProjectFeatures features,
        ParserOptions options,
        IProgress<ParserProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ParseResult();

        try
        {
            var projectXml = await LoadProjectXmlAsync(files, progress, cancellationToken);

            progress?.Report(new ParserProgress
            {
                Step = ParseStep.ParseGroupAddresses,
                PercentComplete = 0
            });

            result.GroupAddresses = await ParseGroupAddressesAsync(
                projectXml,
                features.AddressingStyle,
                cancellationToken
            );

            result.GroupRanges = await ParseGroupRangesAsync(
                projectXml,
                cancellationToken
            );

            progress?.Report(new ParserProgress
            {
                Step = ParseStep.ParseGroupAddresses,
                PercentComplete = 100
            });

            progress?.Report(new ParserProgress
            {
                Step = ParseStep.ParseDevices,
                PercentComplete = 0
            });

            result.Devices = await ParseDevicesAsync(
                files,
                projectXml,
                cancellationToken
            );

            progress?.Report(new ParserProgress
            {
                Step = ParseStep.ParseDevices,
                PercentComplete = 100
            });

            // Build Id → resolved-address maps once, reused by Locations + ComObjects.
            var deviceAddressMap = BuildDeviceAddressMap(projectXml);
            var groupAddressMap = BuildGroupAddressMap(result.GroupAddresses, projectXml);

            progress?.Report(new ParserProgress
            {
                Step = ParseStep.ParseCommunicationObjects,
                PercentComplete = 0
            });

            result.CommunicationObjects = await ParseCommunicationObjectsAsync(
                projectXml,
                deviceAddressMap,
                groupAddressMap,
                cancellationToken
            );

            progress?.Report(new ParserProgress
            {
                Step = ParseStep.ParseCommunicationObjects,
                PercentComplete = 100
            });

            progress?.Report(new ParserProgress
            {
                Step = ParseStep.ParseLocations,
                PercentComplete = 0
            });

            result.Locations = await ParseLocationsAsync(
                projectXml,
                deviceAddressMap,
                groupAddressMap,
                cancellationToken
            );

            progress?.Report(new ParserProgress
            {
                Step = ParseStep.ParseLocations,
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
        ProjectFileMap files,
        IProgress<ParserProgress>? progress,
        CancellationToken cancellationToken
    );

    protected abstract Task<List<GroupAddress>> ParseGroupAddressesAsync(
        XDocument projectXml,
        AddressingStyle addressingStyle,
        CancellationToken cancellationToken
    );

    protected abstract Task<List<Device>> ParseDevicesAsync(
        ProjectFileMap files,
        XDocument projectXml,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Parse the main/middle group ranges (&lt;GroupRange&gt; elements). These carry the human-readable
    /// section names of the group-address tree (e.g. "Heating", "Wohnzimmer"). They are nested — a main
    /// group's GroupRange contains its middle groups' GroupRanges — so we flatten ALL of them via
    /// Descendants, capturing Name + RangeStart + RangeEnd. The layout is identical across ETS 4/5/6,
    /// so the base implementation serves all loaders. Returns an empty list when none are present.
    /// </summary>
    protected virtual Task<List<GroupRange>> ParseGroupRangesAsync(
        XDocument projectXml,
        CancellationToken cancellationToken)
    {
        var ns = GetNamespace(projectXml);
        var ranges = new List<GroupRange>();

        foreach (var element in projectXml.Descendants(ns + "GroupRange"))
        {
            var startAttr = element.Attribute("RangeStart")?.Value;
            var endAttr = element.Attribute("RangeEnd")?.Value;
            if (!int.TryParse(startAttr, out var start) || !int.TryParse(endAttr, out var end))
                continue;

            ranges.Add(new GroupRange
            {
                Name = element.Attribute("Name")?.Value ?? string.Empty,
                RangeStart = start,
                RangeEnd = end
            });
        }

        _logger.LogInformation("Parsed {Count} group ranges", ranges.Count);
        return Task.FromResult(ranges);
    }

    /// <summary>
    /// Parse the building/location hierarchy. Handles both the ETS4 &lt;Buildings&gt;/&lt;BuildingPart&gt;
    /// layout and the ETS5/6 &lt;Locations&gt;/&lt;Space&gt; layout (the element names differ, the structure
    /// is otherwise identical). Returns an empty list when the project has no locations.
    /// </summary>
    protected virtual Task<List<Location>> ParseLocationsAsync(
        XDocument projectXml,
        IReadOnlyDictionary<string, string> deviceAddressMap,
        IReadOnlyDictionary<string, string> groupAddressMap,
        CancellationToken cancellationToken)
    {
        var ns = GetNamespace(projectXml);
        var locations = new List<Location>();

        // Root containers: <Buildings> (ETS4) and <Locations> (ETS5/6).
        var roots = projectXml.Descendants(ns + "Buildings")
            .Concat(projectXml.Descendants(ns + "Locations"));

        foreach (var root in roots)
        {
            // Direct children are the top-level nodes (BuildingPart / Space).
            foreach (var node in root.Elements())
            {
                if (node.Name.LocalName is "BuildingPart" or "Space")
                {
                    WalkLocationNode(node, parentId: null, deviceAddressMap, groupAddressMap, locations);
                }
            }
        }

        _logger.LogInformation("Parsed {Count} locations", locations.Count);
        return Task.FromResult(locations);
    }

    /// <summary>
    /// Parse communication objects from every &lt;DeviceInstance&gt;. Supports both linking styles:
    /// ETS4 nests &lt;Connectors&gt;/&lt;Send&gt;/&lt;Receive GroupAddressRefId="..."&gt;, while ETS5/6 use a
    /// space-separated @Links attribute. Only com objects with at least one resolvable GA link are kept.
    /// </summary>
    protected virtual Task<List<ComObject>> ParseCommunicationObjectsAsync(
        XDocument projectXml,
        IReadOnlyDictionary<string, string> deviceAddressMap,
        IReadOnlyDictionary<string, string> groupAddressMap,
        CancellationToken cancellationToken)
    {
        var ns = GetNamespace(projectXml);
        var comObjects = new List<ComObject>();

        var deviceElements = projectXml
            .Descendants(ns + "DeviceInstance")
            .Where(d => d.Attribute("Address") != null);

        foreach (var device in deviceElements)
        {
            var deviceId = device.Attribute("Id")?.Value;
            if (deviceId == null || !deviceAddressMap.TryGetValue(deviceId, out var deviceAddress))
                continue;

            foreach (var comRef in device.Descendants(ns + "ComObjectInstanceRef"))
            {
                var links = ResolveComObjectLinks(comRef, ns, groupAddressMap);
                if (links.Count == 0)
                    continue; // only keep com objects that are actually linked to a GA

                comObjects.Add(new ComObject
                {
                    DeviceAddress = deviceAddress,
                    Number = ExtractComObjectNumber(comRef.Attribute("RefId")?.Value),
                    Name = comRef.Attribute("Text")?.Value,
                    FunctionText = comRef.Attribute("Description")?.Value,
                    GroupAddressLinks = links,
                    DatapointType = comRef.Attribute("DatapointType")?.Value,
                    Flags = BuildComObjectFlags(comRef, ns)
                });
            }
        }

        _logger.LogInformation("Parsed {Count} communication objects", comObjects.Count);
        return Task.FromResult(comObjects);
    }

    private void WalkLocationNode(
        XElement node,
        string? parentId,
        IReadOnlyDictionary<string, string> deviceAddressMap,
        IReadOnlyDictionary<string, string> groupAddressMap,
        List<Location> sink)
    {
        var id = node.Attribute("Id")?.Value ?? string.Empty;
        var location = new Location
        {
            Id = id,
            Name = node.Attribute("Name")?.Value ?? string.Empty,
            Type = node.Attribute("Type")?.Value ?? string.Empty,
            ParentId = parentId
        };

        foreach (var child in node.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "BuildingPart":
                case "Space":
                    WalkLocationNode(child, id, deviceAddressMap, groupAddressMap, sink);
                    break;
                case "DeviceInstanceRef":
                    var devRef = child.Attribute("RefId")?.Value;
                    if (devRef != null && deviceAddressMap.TryGetValue(devRef, out var devAddr))
                        location.DeviceRefIds.Add(devAddr);
                    break;
                case "GroupAddressRef":
                    var gaRef = child.Attribute("RefId")?.Value;
                    if (gaRef != null && groupAddressMap.TryGetValue(gaRef, out var gaAddr))
                        location.GroupAddressRefIds.Add(gaAddr);
                    break;
            }
        }

        sink.Add(location);
    }

    private static List<string> ResolveComObjectLinks(
        XElement comRef,
        XNamespace ns,
        IReadOnlyDictionary<string, string> groupAddressMap)
    {
        var resolved = new List<string>();

        // Style A (ETS5/6): space-separated @Links attribute (bare GA ids, e.g. "GA-2 GA-6").
        var linksAttr = comRef.Attribute("Links")?.Value;
        if (!string.IsNullOrWhiteSpace(linksAttr))
        {
            foreach (var token in linksAttr.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (groupAddressMap.TryGetValue(token, out var addr) && !resolved.Contains(addr))
                    resolved.Add(addr);
            }
        }

        // Style B (ETS4): <Connectors><Send|Receive GroupAddressRefId="..." />
        foreach (var connector in comRef.Descendants(ns + "Send")
                     .Concat(comRef.Descendants(ns + "Receive")))
        {
            var refId = connector.Attribute("GroupAddressRefId")?.Value;
            if (refId != null && groupAddressMap.TryGetValue(refId, out var addr) && !resolved.Contains(addr))
                resolved.Add(addr);
        }

        return resolved;
    }

    private static string? BuildComObjectFlags(XElement comRef, XNamespace ns)
    {
        var hasSend = comRef.Descendants(ns + "Send").Any();
        var hasReceive = comRef.Descendants(ns + "Receive").Any();

        if (hasSend && hasReceive) return "Send,Receive";
        if (hasSend) return "Send";
        if (hasReceive) return "Receive";

        // ETS5/6 expose explicit flag attributes instead of Send/Receive connectors.
        var flags = new List<string>();
        foreach (var name in new[] { "ReadFlag", "WriteFlag", "CommunicationFlag", "TransmitFlag", "UpdateFlag", "ReadOnInitFlag" })
        {
            var value = comRef.Attribute(name)?.Value;
            if (string.Equals(value, "Enabled", StringComparison.OrdinalIgnoreCase))
                flags.Add(name.Replace("Flag", string.Empty));
        }

        return flags.Count > 0 ? string.Join(",", flags) : null;
    }

    /// <summary>
    /// Extract the communication object number from a ComObject RefId. RefIds look like
    /// "O-71_R-1506" (non-module) or "MD-2_M-1_MI-1_O-2-9_R-4" (module objects, where the "O-2-9"
    /// segment is channel-2 / object-9). The object number is the LAST numeric segment of the
    /// "O-..." token — i.e. the part after the final '-' before the "_R" separator (or end of string).
    /// So "O-71_R-1506" → 71 and "O-2-9_R-4" → 9. Returns 0 when no numeric object id is present.
    /// </summary>
    internal static int ExtractComObjectNumber(string? refId)
    {
        if (string.IsNullOrEmpty(refId))
            return 0;

        var idx = refId.LastIndexOf("O-", StringComparison.Ordinal);
        if (idx < 0)
            return 0;

        // The "O-" token runs from here up to the "_R" separator (or to the end of the string).
        var rest = refId[(idx + 2)..];
        var underscore = rest.IndexOf('_');
        var token = underscore >= 0 ? rest[..underscore] : rest; // e.g. "71" or "2-9"

        // The object number is the segment after the last '-' (the channel prefix, if any, precedes it).
        var lastDash = token.LastIndexOf('-');
        var numberPart = lastDash >= 0 ? token[(lastDash + 1)..] : token;

        return int.TryParse(numberPart, out var n) ? n : 0;
    }

    /// <summary>
    /// Build a map from a device's internal ETS Id to its resolved physical address. Keyed by both
    /// the full Id (e.g. "P-0600-0_DI-3") and the bare "DI-3" suffix so refs of either form resolve.
    /// The address is computed identically to ParseDevicesAsync to stay consistent with stored devices.
    /// </summary>
    /// <summary>
    /// Full raw physical address of a DeviceInstance = area·line·device. The DeviceInstance
    /// <c>Address</c> attribute is ONLY the device part (0–255); the area and line come from the
    /// enclosing <c>&lt;Area&gt;</c> / <c>&lt;Line&gt;</c> topology nodes. Walking the ancestors
    /// works for ETS4/5 (Area→Line→DeviceInstance) and ETS6 (Area→Line→Segment→DeviceInstance).
    /// Without this, devices in different lines collapse onto the same a.l.d string (e.g. two
    /// devices with @Address=19 both become "0.0.19"), corrupting device lookups and the
    /// communication-object "used by" join.
    /// </summary>
    protected static int ComputeDeviceRawAddress(XElement deviceInstance, XNamespace ns)
    {
        static int Parse(string? v) => int.TryParse(v, out var n) ? n : 0;

        var device = Parse(deviceInstance.Attribute("Address")?.Value);
        var line = Parse(deviceInstance.Ancestors(ns + "Line").FirstOrDefault()?.Attribute("Address")?.Value);
        var area = Parse(deviceInstance.Ancestors(ns + "Area").FirstOrDefault()?.Attribute("Address")?.Value);
        return ((area & 0x0F) << 12) | ((line & 0x0F) << 8) | (device & 0xFF);
    }

    protected Dictionary<string, string> BuildDeviceAddressMap(XDocument projectXml)
    {
        var ns = GetNamespace(projectXml);
        var map = new Dictionary<string, string>();

        foreach (var device in projectXml.Descendants(ns + "DeviceInstance"))
        {
            var id = device.Attribute("Id")?.Value;
            if (id == null || device.Attribute("Address") == null)
                continue;

            var address = Helpers.AddressConverter.ToPhysicalAddressString(ComputeDeviceRawAddress(device, ns));
            map[id] = address;
            AddSuffixKey(map, id, address);
        }

        return map;
    }

    /// <summary>
    /// Build a map from a group address's internal ETS Id to its resolved m/m/s string. Keyed by both
    /// the full Id (e.g. "P-0600-0_GA-1") and the bare "GA-1" suffix (used by the @Links attribute).
    /// </summary>
    protected Dictionary<string, string> BuildGroupAddressMap(List<GroupAddress> groupAddresses, XDocument projectXml)
    {
        var ns = GetNamespace(projectXml);

        // The parsed GroupAddress DTOs carry both the internal Id (Identifier) and resolved Address;
        // index those directly, then add bare-suffix aliases for the @Links style.
        var map = new Dictionary<string, string>();
        foreach (var ga in groupAddresses)
        {
            if (string.IsNullOrEmpty(ga.Identifier))
                continue;
            map[ga.Identifier] = ga.Address;
            AddSuffixKey(map, ga.Identifier, ga.Address);
        }

        return map;
    }

    private static void AddSuffixKey(Dictionary<string, string> map, string fullId, string value)
    {
        // Internal ids look like "P-0600-0_GA-1"; the @Links/RefId short form is the part after the
        // last underscore ("GA-1"). Add it as an alias without clobbering an existing distinct entry.
        var underscore = fullId.LastIndexOf('_');
        if (underscore < 0 || underscore == fullId.Length - 1)
            return;

        var suffix = fullId[(underscore + 1)..];
        if (!map.ContainsKey(suffix))
            map[suffix] = value;
    }

    /// <summary>
    /// Guarantee a non-empty display name for a device. Some DeviceInstance elements carry no @Name,
    /// which would leave the UI blank. Fall back to the resolved product name, then the physical
    /// address. An already-present @Name is left untouched.
    /// </summary>
    protected static void EnsureDeviceName(Device device)
    {
        if (!string.IsNullOrWhiteSpace(device.Name))
            return;

        if (!string.IsNullOrWhiteSpace(device.ProductName))
            device.Name = device.ProductName!;
        else if (!string.IsNullOrWhiteSpace(device.PhysicalAddress))
            device.Name = device.PhysicalAddress;
    }

    protected XNamespace GetNamespace(XDocument xml)
    {
        return xml.Root?.Name.Namespace ?? XNamespace.None;
    }

    protected static async Task<XDocument> LoadProjectXmlFromFilesAsync(
        ProjectFileMap files,
        CancellationToken cancellationToken)
    {
        var path = files.FindFirstByName("0.xml")
                   ?? throw new FileNotFoundException("Project file (0.xml) not found in archive");

        await using var stream = files.OpenRead(path);
        return await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
    }

    protected async Task<Dictionary<string, (string Manufacturer, string ProductName)>>
        LoadHardwareDataAsync(ProjectFileMap files, CancellationToken cancellationToken)
    {
        var hardwareData = new Dictionary<string, (string, string)>();

        // Resolve real manufacturer names (M-XXXX -> "ABB") from knx_master.xml once.
        var manufacturerNames = await LoadManufacturerNamesAsync(files, cancellationToken);

        foreach (var path in files.FindAllByName("Hardware.xml"))
        {
            await using var stream = files.OpenRead(path);
            var xml = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
            var ns = GetNamespace(xml);

            // The manufacturer id lives on <ManufacturerData><Manufacturer RefId="M-XXXX">
            // (Hardware.xml's root is <KNX>, so the old xml.Root?.Attribute("RefId") was always null,
            // and even when present an id like "M-0083" split on '-' only yields the literal "M").
            var manufacturerId = xml.Descendants(ns + "Manufacturer")
                .Select(m => m.Attribute("RefId")?.Value)
                .FirstOrDefault(v => !string.IsNullOrEmpty(v));

            string manufacturer;
            if (manufacturerId != null && manufacturerNames.TryGetValue(manufacturerId, out var name))
            {
                manufacturer = name;
            }
            else if (!string.IsNullOrEmpty(manufacturerId))
            {
                // Known id but no name in knx_master.xml — keep the raw id rather than faking one.
                manufacturer = manufacturerId;
                _logger.LogWarning(
                    "No manufacturer name found for id {ManufacturerId} in knx_master.xml (Hardware.xml: {Path})",
                    manufacturerId, path);
            }
            else
            {
                manufacturer = "Unknown";
                _logger.LogWarning("No manufacturer id found in Hardware.xml: {Path}", path);
            }

            foreach (var product in xml.Descendants(ns + "Product"))
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

    /// <summary>
    /// Parse <c>knx_master.xml</c> into a map of manufacturer id (e.g. "M-0002") to its display name
    /// (e.g. "ABB"). The entries live under <c>&lt;MasterData&gt;&lt;Manufacturers&gt;&lt;Manufacturer Id="M-XXXX"
    /// Name="..."/&gt;</c>. Returns an empty map when knx_master.xml is missing or has no manufacturer list.
    /// </summary>
    protected async Task<Dictionary<string, string>> LoadManufacturerNamesAsync(
        ProjectFileMap files, CancellationToken cancellationToken)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var path = files.FindFirstByName("knx_master.xml");
        if (path == null)
        {
            _logger.LogWarning("knx_master.xml not found in archive; manufacturer names cannot be resolved");
            return names;
        }

        await using var stream = files.OpenRead(path);
        var xml = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        var ns = GetNamespace(xml);

        foreach (var manufacturer in xml.Descendants(ns + "Manufacturer"))
        {
            var id = manufacturer.Attribute("Id")?.Value;
            var name = manufacturer.Attribute("Name")?.Value;
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
            {
                names[id] = name;
            }
        }

        _logger.LogInformation("Loaded {Count} manufacturer names from knx_master.xml", names.Count);
        return names;
    }
}
