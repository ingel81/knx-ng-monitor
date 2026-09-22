using System.Xml;

namespace KnxMonitor.ProjectParser.Services;

/// <summary>
/// Streams one manufacturer application program and pulls out the catalog data for a given set of
/// <c>&lt;ComObjectRef&gt;</c> ids. These files reach ~60 MB, so they are never loaded as an
/// <c>XDocument</c>; a forward-only <see cref="XmlReader"/> over the archive entry keeps the whole
/// pass at a few hundred kilobytes.
/// </summary>
internal static class ApplicationProgramReader
{
    private static readonly XmlReaderSettings Settings = new()
    {
        Async = true,
        IgnoreComments = true,
        IgnoreWhitespace = true,
        IgnoreProcessingInstructions = true,
        DtdProcessing = DtdProcessing.Prohibit,
        CloseInput = false
    };

    public static async Task<Dictionary<string, ComObjectCatalogEntry>> ReadAsync(
        Stream stream,
        IReadOnlySet<string> wantedComObjectRefIds,
        CancellationToken cancellationToken)
    {
        // <ComObjectTable> precedes <ComObjectRefs> in every ETS schema, but nothing in the format
        // guarantees it, so both halves are collected and joined at the end. The ComObject records
        // are a few hundred small objects even in the largest programs.
        var comObjects = new Dictionary<string, ComObjectCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        var refs = new Dictionary<string, (string? ComObjectId, ComObjectCatalogEntry Entry)>(
            StringComparer.OrdinalIgnoreCase);

        // ComObjects a wanted ref points at but that have not come past yet. Once this is empty and
        // every wanted ref is in, the rest of the file cannot contribute anything — and the rest of
        // the file is most of it: the parameter and module sections behind ComObjectRefs dwarf them.
        var pendingComObjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var reader = XmlReader.Create(stream, Settings);

        while (await reader.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.NodeType != XmlNodeType.Element) continue;

            switch (reader.LocalName)
            {
                case "ComObject":
                {
                    var id = reader.GetAttribute("Id");
                    if (string.IsNullOrEmpty(id)) break;

                    comObjects[id] = ReadEntry(reader);
                    pendingComObjects.Remove(id);
                    break;
                }

                case "ComObjectRef":
                {
                    var id = reader.GetAttribute("Id");
                    if (string.IsNullOrEmpty(id) || !wantedComObjectRefIds.Contains(id)) break;

                    var comObjectId = reader.GetAttribute("RefId");
                    refs[id] = (comObjectId, ReadEntry(reader));

                    if (!string.IsNullOrEmpty(comObjectId) && !comObjects.ContainsKey(comObjectId))
                    {
                        pendingComObjects.Add(comObjectId);
                    }
                    break;
                }

                default:
                    continue;
            }

            if (refs.Count == wantedComObjectRefIds.Count && pendingComObjects.Count == 0) break;
        }

        var result = new Dictionary<string, ComObjectCatalogEntry>(refs.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var (refElementId, (comObjectId, refEntry)) in refs)
        {
            var merged = new ComObjectCatalogEntry();

            if (!string.IsNullOrEmpty(comObjectId) && comObjects.TryGetValue(comObjectId, out var baseEntry))
            {
                merged.OverlayWith(baseEntry);
            }

            // The ref is the more specific level and wins per attribute, not per element.
            merged.OverlayWith(refEntry);

            result[refElementId] = merged;
        }

        return result;
    }

    private static ComObjectCatalogEntry ReadEntry(XmlReader reader)
    {
        var entry = new ComObjectCatalogEntry
        {
            DatapointType = reader.GetAttribute("DatapointType"),
            ObjectSize = reader.GetAttribute("ObjectSize")
        };

        for (var i = 0; i < ComObjectFlagNames.All.Length; i++)
        {
            entry.Flags[i] = reader.GetAttribute(ComObjectFlagNames.All[i]);
        }

        return entry;
    }
}
