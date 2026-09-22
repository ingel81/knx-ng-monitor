using KnxMonitor.ProjectParser.Core.Models;

namespace KnxMonitor.ProjectParser.Helpers;

/// <summary>
/// The rules behind issue #24: how a group address gets a datapoint type when
/// <c>GroupAddress/@DatapointType</c> is empty — which is always the case for ETS 4 (the attribute
/// did not exist yet, and migrating to ETS 5/6 does not fill it in) and common enough in ETS 6.
/// <para>
/// Per linked communication object the cascade is
/// <c>ComObjectInstanceRef/@DatapointType</c> → <c>ComObjectRef/@DatapointType</c> →
/// <c>ComObject/@DatapointType</c> → <c>ObjectSize</c>. The candidates of all objects linked to one
/// group address are then folded together by <see cref="Combine"/>.
/// </para>
/// </summary>
/// <summary>
/// One communication object's contribution to a group address: the type it yields, and whether that
/// was DECLARED (by the project file or the manufacturer catalog) or only inferred from the object's
/// width.
/// </summary>
internal readonly record struct DatapointCandidate(DptInfo? Dpt, bool Inferred);

internal static class DatapointTypeCascade
{
    /// <summary>
    /// Main types derived from an object size, in two classes:
    /// <list type="bullet">
    ///   <item>1 bit, 4 bit and 14 bytes have their width to THEMSELVES — DPT 1, 3 and 16 are the
    ///         only types of that size, so this is a fact, not a guess.</item>
    ///   <item>2 bytes is shared (DPT 7/8/9), and knx_master.xml marks DPT 9 as
    ///         <c>Default="true"</c>. Taking that default is KNX's own assumption and it is the one
    ///         that pays: across eight sample projects it is the only inference that resolves an
    ///         address at all, and 2-byte addresses in a building are overwhelmingly temperatures,
    ///         setpoints, brightness and wind.</item>
    /// </list>
    /// <para>
    /// 4 bytes is NOT in the list although knx_master.xml marks DPT 14 as a default too. Measured
    /// over those same eight projects it resolved exactly nothing, while 4-byte objects are just as
    /// often energy or pulse counters (DPT 12/13) as floats. Guessing a float there would decode
    /// every telegram on such an address into nonsense and persist it that way, at no measurable
    /// gain. Every other width stays empty for the same reason: no default to lean on.
    /// </para>
    /// </summary>
    private static readonly Dictionary<int, int> MainTypeByBitSize = new()
    {
        [1] = 1,     // 1 bit    -> DPT 1  (only type of this size)
        [4] = 3,     // 4 bit    -> DPT 3  (only type of this size)
        [16] = 9,    // 2 bytes  -> DPT 9  (Default="true" in knx_master.xml)
        [112] = 16,  // 14 bytes -> DPT 16 (only type of this size)
    };

    /// <summary>A parse result is only usable when it actually produced a main type.</summary>
    public static bool IsUsable(DptInfo? dpt) => dpt is { Main: > 0 };

    /// <summary>
    /// Derive a main type from an ETS <c>ObjectSize</c> such as "1 Bit", "4 Bit", "2 Bytes" or
    /// "14 Bytes". Returns null for every ambiguous or unparseable size.
    /// </summary>
    public static DptInfo? FromObjectSize(string? objectSize)
    {
        var bits = ParseBitSize(objectSize);
        if (bits == null || !MainTypeByBitSize.TryGetValue(bits.Value, out var main))
        {
            return null;
        }

        return new DptInfo { Main = main, Sub = null, OriginalString = $"DPT-{main}" };
    }

    /// <summary>
    /// Fold the candidates of all communication objects linked to one group address into a single
    /// type.
    /// <para>
    /// A declaration outranks a guess: where at least one linked object names its type, the
    /// candidates derived from <c>ObjectSize</c> are dropped before anything is compared. Without
    /// that, our own 2-byte and 4-byte defaults (DPT 9, DPT 14) would contradict a declared DPT 7/8
    /// or DPT 12/13 of the very same width, and the disagreement would erase both.
    /// </para>
    /// <para>
    /// What survives that filter is then merged: differing main types are irreconcilable and yield
    /// nothing, because the linked objects genuinely describe different things. A subtype only
    /// survives when the candidates naming one agree, otherwise the main type stands on its own —
    /// and a main type is never padded out to ".001".
    /// </para>
    /// </summary>
    public static DptInfo? Combine(IReadOnlyList<DatapointCandidate> candidates)
    {
        var usable = Rank(candidates);
        if (usable.Count == 0) return null;

        var main = usable[0].Main;
        foreach (var candidate in usable)
        {
            if (candidate.Main != main) return null;
        }

        int? sub = null;
        foreach (var candidate in usable)
        {
            if (!candidate.Sub.HasValue) continue;
            if (sub.HasValue && sub.Value != candidate.Sub.Value)
            {
                sub = null;
                break;
            }
            sub = candidate.Sub;
        }

        return new DptInfo
        {
            Main = main,
            Sub = sub,
            OriginalString = sub.HasValue ? $"DPST-{main}-{sub.Value}" : $"DPT-{main}"
        };
    }

    /// <summary>Declared candidates if there are any, otherwise the inferred ones.</summary>
    private static List<DptInfo> Rank(IReadOnlyList<DatapointCandidate> candidates)
    {
        var declared = new List<DptInfo>();
        var inferred = new List<DptInfo>();

        foreach (var candidate in candidates)
        {
            if (candidate.Dpt == null) continue;
            (candidate.Inferred ? inferred : declared).Add(candidate.Dpt);
        }

        return declared.Count > 0 ? declared : inferred;
    }

    private static int? ParseBitSize(string? objectSize)
    {
        if (string.IsNullOrWhiteSpace(objectSize)) return null;

        var parts = objectSize.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return null;
        if (!int.TryParse(parts[0], out var count) || count <= 0) return null;

        return parts[1].ToLowerInvariant() switch
        {
            "bit" or "bits" => count,
            "byte" or "bytes" => count * 8,
            _ => null
        };
    }
}
