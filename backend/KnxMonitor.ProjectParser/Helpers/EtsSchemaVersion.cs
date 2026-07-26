using System.Globalization;
using KnxMonitor.ProjectParser.Core.Enums;

namespace KnxMonitor.ProjectParser.Helpers;

/// <summary>
/// Maps a KNX project file to an <see cref="EtsVersion"/>. The authoritative marker is the XML
/// schema version — the trailing number of the <c>http://knx.org/xml/project/NN</c> namespace.
/// ETS bumps that number with almost every minor release:
/// 11 = ETS 4.2, 14 = ETS 5.6, 20 = ETS 5.7, 21 = ETS 6.0, 22 = ETS 6.1, 23 = ETS 6.2/6.3, …
/// Therefore the mapping MUST be a numeric range check. A hard-coded list of known namespaces
/// silently breaks on every new ETS release — that was GitHub issue #2 ("No loader available for
/// ETS version Unknown"), which hit password-protected projects from ETS 6.1 upwards because
/// there the only readable version marker is <c>knx_master.xml</c>.
/// </summary>
public static class EtsSchemaVersion
{
    private const string NamespaceMarker = "knx.org/xml/project/";

    /// <summary>ETS 6.0 introduced schema 21; everything from here on is ETS 6 (or newer).</summary>
    public const int Ets6SchemaVersion = 21;

    /// <summary>ETS 5.7 — schema 20. Older ETS 5 releases (schema 14) use the ETS 4 layout.</summary>
    public const int Ets5SchemaVersion = 20;

    /// <summary>Oldest schema we can load (ETS 4.2).</summary>
    public const int Ets4SchemaVersion = 11;

    /// <summary>
    /// Extract the schema number from a project namespace URI. Returns null when the URI is not a
    /// KNX project namespace or carries no numeric suffix.
    /// </summary>
    public static int? ParseSchemaVersion(string? namespaceUri)
    {
        if (string.IsNullOrEmpty(namespaceUri))
            return null;

        var idx = namespaceUri.IndexOf(NamespaceMarker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;

        var suffix = namespaceUri[(idx + NamespaceMarker.Length)..].Trim().TrimEnd('/');
        return int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var schema)
            ? schema
            : null;
    }

    /// <summary>Map a schema number to the loader family that understands that file layout.</summary>
    public static EtsVersion FromSchemaVersion(int schemaVersion) => schemaVersion switch
    {
        >= Ets6SchemaVersion => EtsVersion.Ets6,  // 21, 22, 23 and any future ETS 6.x schema
        Ets5SchemaVersion => EtsVersion.Ets5,
        >= Ets4SchemaVersion => EtsVersion.Ets4,  // 11 … 14
        _ => EtsVersion.Unknown
    };

    /// <summary>Convenience overload: namespace URI straight to <see cref="EtsVersion"/>.</summary>
    public static EtsVersion FromNamespace(string? namespaceUri)
    {
        var schema = ParseSchemaVersion(namespaceUri);
        return schema.HasValue ? FromSchemaVersion(schema.Value) : EtsVersion.Unknown;
    }

    /// <summary>Fallback marker: the <c>ToolVersion</c> attribute of project.xml / 0.xml (e.g. "6.2.7302.0").</summary>
    public static EtsVersion FromToolVersion(string? toolVersion)
    {
        if (string.IsNullOrEmpty(toolVersion))
            return EtsVersion.Unknown;

        return toolVersion switch
        {
            _ when toolVersion.StartsWith("4.", StringComparison.Ordinal) => EtsVersion.Ets4,
            _ when toolVersion.StartsWith("5.", StringComparison.Ordinal) => EtsVersion.Ets5,
            _ when toolVersion.StartsWith("6.", StringComparison.Ordinal) => EtsVersion.Ets6,
            _ => EtsVersion.Unknown
        };
    }

    /// <summary>Fallback marker: the <c>CreatedBy</c> attribute (e.g. "ETS6").</summary>
    public static EtsVersion FromCreatedBy(string? createdBy)
    {
        if (string.IsNullOrEmpty(createdBy))
            return EtsVersion.Unknown;

        return createdBy switch
        {
            _ when createdBy.Contains("ETS4", StringComparison.OrdinalIgnoreCase) => EtsVersion.Ets4,
            _ when createdBy.Contains("ETS5", StringComparison.OrdinalIgnoreCase) => EtsVersion.Ets5,
            _ when createdBy.Contains("ETS6", StringComparison.OrdinalIgnoreCase) => EtsVersion.Ets6,
            _ => EtsVersion.Unknown
        };
    }
}
