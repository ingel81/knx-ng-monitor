using System.Xml.Linq;
using KnxMonitor.ProjectParser.Core.Enums;

namespace KnxMonitor.ProjectParser.Helpers;

/// <summary>
/// Reads the project-level metadata (ETS version, group-address style) that ETS stores in
/// <c>project.xml</c> and <c>0.xml</c>. Both files share the same root layout:
/// <code>
/// &lt;KNX xmlns="http://knx.org/xml/project/23" CreatedBy="ETS6" ToolVersion="6.2.7302.0"&gt;
///   &lt;Project Id="P-03AE"&gt;
///     &lt;ProjectInformation Name="…" GroupAddressStyle="TwoLevel" … /&gt;
/// </code>
/// Note where the attributes live: version markers sit on the ROOT, the address style on
/// <c>ProjectInformation</c> — reading GroupAddressStyle off the root silently yields the
/// ThreeLevel default for every real project.
/// </summary>
public static class ProjectXmlMetadata
{
    /// <summary>Namespace schema first, then the ToolVersion / CreatedBy attributes.</summary>
    public static EtsVersion ReadEtsVersion(XElement? root)
    {
        if (root == null)
            return EtsVersion.Unknown;

        foreach (var candidate in new[]
                 {
                     EtsSchemaVersion.FromNamespace(root.Name.NamespaceName),
                     EtsSchemaVersion.FromToolVersion(root.Attribute("ToolVersion")?.Value),
                     EtsSchemaVersion.FromCreatedBy(root.Attribute("CreatedBy")?.Value)
                 })
        {
            if (candidate != EtsVersion.Unknown)
                return candidate;
        }

        return EtsVersion.Unknown;
    }

    /// <summary>
    /// Group-address style, taken from the first element carrying a <c>GroupAddressStyle</c>
    /// attribute (root itself, otherwise ProjectInformation). Returns null when the document
    /// carries no style at all, so callers can fall back or keep looking in another file.
    /// </summary>
    public static AddressingStyle? ReadAddressingStyle(XElement? root)
    {
        if (root == null)
            return null;

        var raw = root.Attribute("GroupAddressStyle")?.Value;

        if (string.IsNullOrEmpty(raw))
        {
            // Descendants() is lazy and ProjectInformation is the second element in the document,
            // so this stops early even on a multi-MB 0.xml.
            raw = root.Descendants()
                .Select(e => e.Attribute("GroupAddressStyle")?.Value)
                .FirstOrDefault(v => !string.IsNullOrEmpty(v));
        }

        return ParseAddressingStyle(raw);
    }

    /// <summary>Map the raw attribute value; an unknown but present value falls back to ThreeLevel.</summary>
    public static AddressingStyle? ParseAddressingStyle(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return null;

        return raw.ToLowerInvariant() switch
        {
            "threelevel" or "3level" => AddressingStyle.ThreeLevel,
            "twolevel" or "2level" => AddressingStyle.TwoLevel,
            "free" or "freelevel" => AddressingStyle.Free,
            _ => AddressingStyle.ThreeLevel
        };
    }
}
