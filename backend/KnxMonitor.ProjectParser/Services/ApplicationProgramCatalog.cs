using System.Xml.Linq;
using KnxMonitor.ProjectParser.Core.Models;
using Microsoft.Extensions.Logging;

namespace KnxMonitor.ProjectParser.Services;

/// <summary>Attribute names of the six KNX communication flags, in the order they are reported.</summary>
internal static class ComObjectFlagNames
{
    public static readonly string[] All =
    {
        "ReadFlag", "WriteFlag", "CommunicationFlag", "TransmitFlag", "UpdateFlag", "ReadOnInitFlag"
    };
}

/// <summary>
/// What the manufacturer catalog says about one communication object: the
/// <c>&lt;ComObject&gt;</c> defaults with the values of the referencing <c>&lt;ComObjectRef&gt;</c>
/// laid over them, per attribute.
/// </summary>
internal sealed class ComObjectCatalogEntry
{
    public string? DatapointType { get; set; }
    public string? ObjectSize { get; set; }
    public string?[] Flags { get; } = new string?[ComObjectFlagNames.All.Length];

    public void OverlayWith(ComObjectCatalogEntry more)
    {
        if (!string.IsNullOrEmpty(more.DatapointType)) DatapointType = more.DatapointType;
        if (!string.IsNullOrEmpty(more.ObjectSize)) ObjectSize = more.ObjectSize;
        for (var i = 0; i < Flags.Length; i++)
        {
            if (!string.IsNullOrEmpty(more.Flags[i])) Flags[i] = more.Flags[i];
        }
    }
}

/// <summary>
/// Resolves every <c>&lt;ComObjectInstanceRef&gt;</c> of a project to the manufacturer catalog entry
/// behind it. That entry carries the datapoint type, the object size and the communication flags the
/// project file itself usually omits — ETS only writes those onto the instance when they differ from
/// the catalog default, which is why ETS 4 group addresses have no DPT at all and ETS 5/6
/// communication objects have no flags.
/// <para>
/// The jump goes through <c>ComObjectInstanceRef/@RefId</c>, which names a <b>ComObjectRef</b>, not a
/// ComObject: one ComObject has many refs with different datapoint types and object sizes, so
/// shortcutting to the ComObject yields wrong types.
/// </para>
/// <para>
/// Application programs are the largest files in an archive (up to ~60 MB each), so they are read
/// with a streaming <see cref="System.Xml.XmlReader"/>, once per program, and only for the programs
/// the project actually uses. Any failure here is logged and skipped: the data it adds is an
/// enrichment, never a reason to fail an import.
/// </para>
/// </summary>
internal sealed class ApplicationProgramCatalog
{
    private readonly Dictionary<(string DeviceId, string RefId), string> _catalogIds;
    private readonly Dictionary<string, ComObjectCatalogEntry> _entries;

    public static ApplicationProgramCatalog Empty { get; } = new(
        new Dictionary<(string, string), string>(),
        new Dictionary<string, ComObjectCatalogEntry>(StringComparer.OrdinalIgnoreCase));

    private ApplicationProgramCatalog(
        Dictionary<(string, string), string> catalogIds,
        Dictionary<string, ComObjectCatalogEntry> entries)
    {
        _catalogIds = catalogIds;
        _entries = entries;
    }

    public int EntryCount => _entries.Count;

    public ComObjectCatalogEntry? Find(string? deviceId, string? refId)
    {
        if (string.IsNullOrEmpty(deviceId) || string.IsNullOrEmpty(refId)) return null;
        if (!_catalogIds.TryGetValue((deviceId, refId), out var catalogId)) return null;
        return _entries.GetValueOrDefault(catalogId);
    }

    public static async Task<ApplicationProgramCatalog> BuildAsync(
        ProjectFileMap files,
        XDocument projectXml,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var ns = projectXml.Root?.Name.Namespace ?? XNamespace.None;

        Dictionary<string, string> programRefs;
        try
        {
            programRefs = await HardwareProgramMap.LoadAsync(files, cancellationToken, logger);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Carry on with an empty map rather than giving up the catalog: ETS 4 writes the program
            // id into the instance RefId, so DeriveProgramIdFromRefId still resolves those devices —
            // and that fallback exists for exactly this case, a missing or broken Hardware.xml.
            logger.LogWarning(ex, "Hardware.xml unreadable; falling back to the ids in the project file");
            programRefs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var catalogIds = new Dictionary<(string, string), string>();
        var needed = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var device in projectXml.Descendants(ns + "DeviceInstance"))
        {
            var deviceId = device.Attribute("Id")?.Value;
            if (string.IsNullOrEmpty(deviceId)) continue;

            var hardwareRef = device.Attribute("Hardware2ProgramRefId")?.Value;

            foreach (var instanceRef in device.Descendants(ns + "ComObjectInstanceRef"))
            {
                var refId = instanceRef.Attribute("RefId")?.Value;
                if (string.IsNullOrEmpty(refId)) continue;

                var programId = ResolveProgramId(hardwareRef, refId, programRefs);
                if (programId == null) continue;

                var catalogId = BuildCatalogId(programId, refId);
                catalogIds[(deviceId, refId)] = catalogId;

                if (!needed.TryGetValue(programId, out var set))
                {
                    needed[programId] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                set.Add(catalogId);
            }
        }

        var entries = new Dictionary<string, ComObjectCatalogEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var (programId, wanted) in needed)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = files.FindFirstByName(programId + ".xml");
            if (path == null)
            {
                logger.LogDebug("Application program {Program} not found in archive", programId);
                continue;
            }

            try
            {
                await using var stream = files.OpenRead(path);
                var fromProgram = await ApplicationProgramReader.ReadAsync(stream, wanted, cancellationToken);
                foreach (var (id, entry) in fromProgram)
                {
                    entries[id] = entry;
                }

                if (fromProgram.Count < wanted.Count)
                {
                    // Worth knowing: an id the program does not contain also defeats the reader's
                    // early exit, so that program is streamed to its end instead of stopping after
                    // the ComObjectRefs. Silent otherwise, and these files reach ~60 MB.
                    logger.LogDebug(
                        "{Missing} of {Wanted} communication objects not found in {Path}",
                        wanted.Count - fromProgram.Count, wanted.Count, path);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not read application program {Path}", path);
            }
        }

        var resolved = 0;
        foreach (var catalogId in catalogIds.Values)
        {
            if (entries.ContainsKey(catalogId)) resolved++;
        }

        logger.LogInformation(
            "Resolved {Resolved} of {Requested} communication objects from {Programs} application programs",
            resolved, catalogIds.Count, needed.Count);

        return new ApplicationProgramCatalog(catalogIds, entries);
    }

    private static string? ResolveProgramId(
        string? hardware2ProgramRefId,
        string refId,
        IReadOnlyDictionary<string, string> programRefs)
    {
        if (!string.IsNullOrEmpty(hardware2ProgramRefId)
            && programRefs.TryGetValue(hardware2ProgramRefId, out var fromHardware))
        {
            return fromHardware;
        }

        // ETS 4 writes fully-qualified instance RefIds ("M-0083_A-0013-11-A9D6_O-0_R-10000"), so the
        // program id is readable straight off them when Hardware.xml is missing or incomplete.
        return DeriveProgramIdFromRefId(refId);
    }

    internal static string? DeriveProgramIdFromRefId(string refId)
    {
        if (!refId.StartsWith("M-", StringComparison.OrdinalIgnoreCase)) return null;

        var cut = refId.Length;
        foreach (var marker in new[] { "_O-", "_MD-" })
        {
            var index = refId.IndexOf(marker, StringComparison.Ordinal);
            if (index > 0 && index < cut) cut = index;
        }

        return cut < refId.Length ? refId[..cut] : null;
    }

    /// <summary>
    /// Turn a project-side instance RefId into the id the catalog uses.
    /// ETS 5/6 write the short form (<c>O-40_R-1433</c>), which needs the program id in front;
    /// ETS 4 writes it out in full already. Module objects additionally carry the module
    /// <b>instance</b> (<c>MD-2_M-1_MI-1_O-2-1_R-1</c>) while the catalog only knows the module
    /// <b>definition</b> (<c>MD-2_O-2-1_R-1</c>), so those segments are dropped.
    /// </summary>
    internal static string BuildCatalogId(string programId, string refId)
    {
        var normalized = StripModuleInstanceSegments(refId);

        return normalized.StartsWith(programId + "_", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : programId + "_" + normalized;
    }

    internal static string StripModuleInstanceSegments(string refId)
    {
        if (!refId.Contains("MD-", StringComparison.Ordinal)) return refId;

        var parts = refId.Split('_');
        var kept = new List<string>(parts.Length);
        var insideModule = false;

        foreach (var part in parts)
        {
            if (part.StartsWith("MD-", StringComparison.Ordinal))
            {
                insideModule = true;
                kept.Add(part);
                continue;
            }

            // Only inside a module definition can "M-1" mean a module rather than a manufacturer.
            if (insideModule && (IsNumbered(part, "M-") || IsNumbered(part, "MI-"))) continue;

            kept.Add(part);
        }

        return string.Join('_', kept);
    }

    private static bool IsNumbered(string value, string prefix)
    {
        if (value.Length <= prefix.Length) return false;
        if (!value.StartsWith(prefix, StringComparison.Ordinal)) return false;

        for (var i = prefix.Length; i < value.Length; i++)
        {
            if (!char.IsAsciiDigit(value[i])) return false;
        }

        return true;
    }
}
