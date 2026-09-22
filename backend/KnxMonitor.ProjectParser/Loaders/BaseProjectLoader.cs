using System.Xml.Linq;
using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Core.Interfaces;
using KnxMonitor.ProjectParser.Core.Models;
using KnxMonitor.ProjectParser.Helpers;
using KnxMonitor.ProjectParser.Services;
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

            // The manufacturer catalog carries the datapoint types and flags the project file leaves
            // out. Read once here and used by both the com objects and the group-address cascade.
            var catalog = await BuildApplicationProgramCatalogAsync(files, projectXml, cancellationToken);

            ApplyDatapointTypeCascade(projectXml, result.GroupAddresses, groupAddressMap, catalog);

            result.CommunicationObjects = await ParseCommunicationObjectsAsync(
                projectXml,
                deviceAddressMap,
                groupAddressMap,
                catalog,
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
    private protected virtual Task<List<ComObject>> ParseCommunicationObjectsAsync(
        XDocument projectXml,
        IReadOnlyDictionary<string, string> deviceAddressMap,
        IReadOnlyDictionary<string, string> groupAddressMap,
        ApplicationProgramCatalog catalog,
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

                var catalogEntry = catalog.Find(deviceId, comRef.Attribute("RefId")?.Value);

                comObjects.Add(new ComObject
                {
                    DeviceAddress = deviceAddress,
                    Number = ExtractComObjectNumber(comRef.Attribute("RefId")?.Value),
                    Name = comRef.Attribute("Text")?.Value,
                    FunctionText = comRef.Attribute("Description")?.Value,
                    GroupAddressLinks = links,
                    DatapointType = ResolveDeclaredDatapointType(comRef, catalogEntry),
                    Flags = BuildComObjectFlags(comRef, ns, catalogEntry)
                });
            }
        }

        _logger.LogInformation("Parsed {Count} communication objects", comObjects.Count);
        return Task.FromResult(comObjects);
    }

    /// <summary>
    /// Read the manufacturer catalog entries for every communication object of the project. Failing
    /// here costs datapoint types and flags, never the import, so any error is logged and swallowed.
    /// </summary>
    private async Task<ApplicationProgramCatalog> BuildApplicationProgramCatalogAsync(
        ProjectFileMap files,
        XDocument projectXml,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ApplicationProgramCatalog.BuildAsync(files, projectXml, _logger, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Application program catalog unavailable; continuing without it");
            return ApplicationProgramCatalog.Empty;
        }
    }

    /// <summary>
    /// Fill in the datapoint type of every group address that carries none (issue #24). ETS 4 never
    /// writes <c>GroupAddress/@DatapointType</c> and migrating a project to ETS 5/6 does not add it,
    /// so without this the value column and the charts stay empty for those projects.
    /// <para>
    /// Each communication object linked to the address contributes one candidate, resolved through
    /// <see cref="DatapointTypeCascade"/>; the candidates are then folded together. An address that
    /// already has a type is never touched, and an unresolvable one keeps none.
    /// </para>
    /// </summary>
    private protected void ApplyDatapointTypeCascade(
        XDocument projectXml,
        List<GroupAddress> groupAddresses,
        IReadOnlyDictionary<string, string> groupAddressMap,
        ApplicationProgramCatalog catalog)
    {
        // Keyed by address because that is what the links resolve to. A list per address, not a
        // single entry: should a project ever carry the same address twice (several installations,
        // or a malformed file), keeping only the last one would leave the others silently untyped
        // even though they are persisted just the same.
        var pending = new Dictionary<string, List<GroupAddress>>(StringComparer.Ordinal);
        foreach (var ga in groupAddresses)
        {
            if (ga.DatapointType != null || string.IsNullOrEmpty(ga.Address)) continue;

            if (!pending.TryGetValue(ga.Address, out var sameAddress))
            {
                pending[ga.Address] = sameAddress = new List<GroupAddress>(1);
            }
            sameAddress.Add(ga);
        }

        if (pending.Count == 0) return;

        var ns = GetNamespace(projectXml);
        var candidates = new Dictionary<string, List<DatapointCandidate>>(StringComparer.Ordinal);

        foreach (var device in projectXml.Descendants(ns + "DeviceInstance"))
        {
            var deviceId = device.Attribute("Id")?.Value;
            if (string.IsNullOrEmpty(deviceId)) continue;

            foreach (var comRef in device.Descendants(ns + "ComObjectInstanceRef"))
            {
                var links = ResolveComObjectLinks(comRef, ns, groupAddressMap);
                if (links.Count == 0) continue;

                List<string>? targets = null;
                foreach (var link in links)
                {
                    if (pending.ContainsKey(link)) (targets ??= new List<string>()).Add(link);
                }
                if (targets == null) continue;

                var candidate = ResolveComObjectDatapointType(comRef, deviceId, catalog);
                if (candidate.Dpt == null) continue;

                foreach (var target in targets)
                {
                    if (!candidates.TryGetValue(target, out var list))
                    {
                        candidates[target] = list = new List<DatapointCandidate>();
                    }
                    list.Add(candidate);
                }
            }
        }

        var filled = 0;
        foreach (var (address, list) in candidates)
        {
            var combined = DatapointTypeCascade.Combine(list);
            if (combined == null)
            {
                // Only reachable when the linked objects name different MAIN types; a subtype
                // disagreement still yields the main type. Logged because the address then shows no
                // type at all and the reason is invisible from the outside.
                _logger.LogDebug(
                    "No datapoint type for {Address}: linked objects disagree ({Candidates})",
                    address,
                    string.Join(", ", list.Select(c => c.Inferred ? $"{c.Dpt} (from size)" : $"{c.Dpt}")));
                continue;
            }

            foreach (var ga in pending[address])
            {
                ga.DatapointType = combined;
                filled++;
            }
        }

        if (filled > 0)
        {
            _logger.LogInformation(
                "Datapoint type resolved from the device catalog for {Filled} of {Pending} group addresses without one",
                filled, pending.Sum(p => p.Value.Count));
        }
    }

    /// <summary>
    /// One communication object's contribution to the cascade:
    /// <c>ComObjectInstanceRef/@DatapointType</c> → <c>ComObjectRef/@DatapointType</c> →
    /// <c>ComObject/@DatapointType</c> → <c>ObjectSize</c>. The middle two come pre-merged in the
    /// catalog entry (the ref wins over the object), which is the same precedence.
    /// An empty attribute counts as absent — <c>DatapointType=""</c> occurs in real projects.
    /// </summary>
    private static DatapointCandidate ResolveComObjectDatapointType(
        XElement comRef,
        string deviceId,
        ApplicationProgramCatalog catalog)
    {
        var fromInstance = DptInfo.TryParse(comRef.Attribute("DatapointType")?.Value);
        if (DatapointTypeCascade.IsUsable(fromInstance)) return new DatapointCandidate(fromInstance, false);

        var entry = catalog.Find(deviceId, comRef.Attribute("RefId")?.Value);
        if (entry == null) return default;

        var fromCatalog = DptInfo.TryParse(entry.DatapointType);
        if (DatapointTypeCascade.IsUsable(fromCatalog)) return new DatapointCandidate(fromCatalog, false);

        return new DatapointCandidate(DatapointTypeCascade.FromObjectSize(entry.ObjectSize), true);
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

    /// <summary>
    /// Resolve the six communication flags from the two levels that actually carry them:
    /// the manufacturer catalog holds the factory setting (<c>&lt;ComObject&gt;</c>, refined by the
    /// <c>&lt;ComObjectRef&gt;</c>), and the project file carries ONLY those flags the integrator
    /// changed in ETS, as attributes on the <c>&lt;ComObjectInstanceRef&gt;</c>. The instance wins,
    /// per attribute rather than per element, so an unchanged flag keeps the catalog value.
    /// <para>
    /// ETS 4 has no flag attributes in the project file at all, only <c>&lt;Send&gt;</c> /
    /// <c>&lt;Receive&gt;</c> connectors — and those are NOT flags. They say which group address is
    /// the object's primary link and which ones it additionally listens to. Reporting them as flags
    /// made every ETS 4 object read as "Transmit": sample <c>ets 03-de.knxproj</c> has 29 Send and 0
    /// Receive connectors, while the catalog behind them says e.g. TransmitFlag="Disabled",
    /// WriteFlag="Enabled" — a receive-only object shown as transmitting. So the connectors are used
    /// only when no flag information exists anywhere, which is the one case they still say something.
    /// </para>
    /// </summary>
    private static string? BuildComObjectFlags(
        XElement comRef,
        XNamespace ns,
        ComObjectCatalogEntry? catalogEntry)
    {
        var names = ComObjectFlagNames.All;
        var values = new string?[names.Length];
        if (catalogEntry != null)
        {
            Array.Copy(catalogEntry.Flags, values, names.Length);
        }

        var hasFlagData = false;

        for (var i = 0; i < names.Length; i++)
        {
            var value = comRef.Attribute(names[i])?.Value;
            if (!string.IsNullOrEmpty(value)) values[i] = value;
            if (!string.IsNullOrEmpty(values[i])) hasFlagData = true;
        }

        if (hasFlagData)
        {
            // Something declared a flag, so the connectors have nothing to add. The result may still
            // come out empty — an object with every flag disabled is an answer, not a gap — and a
            // catalog entry that names only some of the six is taken at its word for the rest rather
            // than reopening the Send/Receive guess for them.
            var flags = new List<string>();
            for (var i = 0; i < names.Length; i++)
            {
                if (string.Equals(values[i], "Enabled", StringComparison.OrdinalIgnoreCase))
                    flags.Add(names[i].Replace("Flag", string.Empty));
            }

            return flags.Count > 0 ? string.Join(",", flags) : null;
        }

        // Last resort: no manufacturer data for this object and nothing set in the project either.
        var hasSend = comRef.Descendants(ns + "Send").Any();
        var hasReceive = comRef.Descendants(ns + "Receive").Any();

        if (hasSend && hasReceive) return "Send,Receive";
        if (hasSend) return "Send";
        if (hasReceive) return "Receive";

        return null;
    }

    /// <summary>
    /// The datapoint type a communication object DECLARES: its own attribute, else the manufacturer
    /// catalog. Unlike the group-address cascade this never infers a type from ObjectSize — an
    /// object that names no type shows none, while the group address above it may still be resolved
    /// from the width of its linked objects.
    /// <para>
    /// The result is the canonical id rather than the raw attribute, because the raw value may list
    /// every accepted subtype ("DPT-9 DPST-9-1 DPST-9-2 …") and anything decoding it would read that
    /// as 9.001. A value that parses to nothing is passed on unchanged instead of being dropped, but
    /// it must not push the catalog aside — hence the usability check on each level.
    /// </para>
    /// </summary>
    private static string? ResolveDeclaredDatapointType(XElement comRef, ComObjectCatalogEntry? catalogEntry)
    {
        var raw = comRef.Attribute("DatapointType")?.Value;

        var fromInstance = DptInfo.TryParse(raw);
        if (DatapointTypeCascade.IsUsable(fromInstance)) return fromInstance!.ToDptId();

        var fromCatalog = DptInfo.TryParse(catalogEntry?.DatapointType);
        if (DatapointTypeCascade.IsUsable(fromCatalog)) return fromCatalog!.ToDptId();

        return string.IsNullOrEmpty(raw) ? catalogEntry?.DatapointType : raw;
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
