using KnxMonitor.Core.Entities;

namespace KnxMonitor.Infrastructure.Services;

/// <summary>Where a device sits in the building: a compact label and the full path.</summary>
/// <param name="Compact">Direct parent space plus every higher level that has siblings.</param>
/// <param name="Full">Every level from the root down to the direct parent.</param>
public sealed record BuildingPart(string Compact, string Full);

/// <summary>
/// Builds the "building part" of each device from the project's flat location tree (#12).
/// </summary>
/// <remarks>
/// The compact form keeps the space a device sits in and, walking up, only those levels that
/// tell it apart from something else, i.e. that have siblings. A chain like
/// <c>00_SYS › 00/1_CNT › 00/2_WR › 00/3_EG › 01_EGD</c>, where <c>00_SYS</c> and
/// <c>00/1_CNT</c> are the only node on their level, becomes <c>00/2_WR › 00/3_EG › 01_EGD</c>.
/// Single-child levels carry no information in a table column; the full path stays available
/// for the tooltip.
/// </remarks>
public static class BuildingPartResolver
{
    public const string Separator = " › ";

    /// <summary>Building part per device address (a.l.d). Devices in no location are absent.</summary>
    public static Dictionary<string, BuildingPart> ForDevices(IEnumerable<Location> locations)
    {
        var list = locations.ToList();
        var byId = new Dictionary<string, Location>(StringComparer.Ordinal);
        foreach (var l in list) byId.TryAdd(l.ExternalId, l);

        // A parent id that does not resolve makes the node a root, same as the topology view.
        string? ParentOf(Location l) =>
            l.ParentExternalId != null && byId.ContainsKey(l.ParentExternalId) ? l.ParentExternalId : null;

        var siblingCount = list
            .GroupBy(ParentOf)
            .ToDictionary(g => g.Key ?? string.Empty, g => g.Count());

        var result = new Dictionary<string, BuildingPart>(StringComparer.Ordinal);
        var depthOf = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var location in list)
        {
            if (string.IsNullOrWhiteSpace(location.DeviceAddresses)) continue;

            var chain = ChainTo(location, byId, ParentOf);
            var part = Compose(chain, l => siblingCount.GetValueOrDefault(ParentOf(l) ?? string.Empty, 1));

            foreach (var raw in location.DeviceAddresses.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var address = raw.Trim();
                // A device listed in two places keeps the more specific (deeper) one.
                if (depthOf.TryGetValue(address, out var depth) && depth >= chain.Count) continue;
                depthOf[address] = chain.Count;
                result[address] = part;
            }
        }

        return result;
    }

    /// <summary>
    /// Compact and full label for a root-to-parent chain. <paramref name="siblingsOf"/> returns
    /// how many nodes share a node's level (itself included).
    /// </summary>
    internal static BuildingPart Compose(IReadOnlyList<Location> chain, Func<Location, int> siblingsOf)
    {
        if (chain.Count == 0) return new BuildingPart(string.Empty, string.Empty);

        var compact = new List<string>();
        for (var i = 0; i < chain.Count; i++)
        {
            // The direct parent always counts; above it only levels with more than one node.
            if (i == chain.Count - 1 || siblingsOf(chain[i]) > 1)
                compact.Add(chain[i].Name);
        }

        return new BuildingPart(
            string.Join(Separator, compact),
            string.Join(Separator, chain.Select(l => l.Name)));
    }

    private static List<Location> ChainTo(
        Location location, IReadOnlyDictionary<string, Location> byId, Func<Location, string?> parentOf)
    {
        var chain = new List<Location>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (Location? cur = location; cur != null && seen.Add(cur.ExternalId);)
        {
            chain.Add(cur);
            var parent = parentOf(cur);
            cur = parent != null ? byId[parent] : null;
        }
        chain.Reverse();
        return chain;
    }
}
