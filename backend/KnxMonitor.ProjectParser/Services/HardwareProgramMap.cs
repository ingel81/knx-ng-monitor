using System.Xml;
using KnxMonitor.ProjectParser.Core.Models;
using Microsoft.Extensions.Logging;

namespace KnxMonitor.ProjectParser.Services;

/// <summary>
/// Maps a device's <c>Hardware2ProgramRefId</c> to the id of its application program, read from the
/// <c>Hardware.xml</c> files in the archive:
/// <code>
/// &lt;Hardware2Program Id="M-0002_H-…-1_HP-A066-14-550B"&gt;
///   &lt;ApplicationProgramRef RefId="M-0002_A-A066-14-550B" /&gt;
/// </code>
/// ETS 5/6 write their <c>ComObjectInstanceRef/@RefId</c> in the short form (<c>O-40_R-1433</c>),
/// which says nothing about the program the object belongs to, so this detour is the only reliable
/// way there. For ETS 6 it is also the only way to learn the <c>-OXXXX</c> suffix its program files
/// carry.
/// </summary>
internal static class HardwareProgramMap
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

    public static async Task<Dictionary<string, string>> LoadAsync(
        ProjectFileMap files,
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in files.FindAllByName("Hardware.xml"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await ReadOneAsync(files, path, map, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One damaged manufacturer folder may not cost the other manufacturers their
                // application programs — and with them every flag and every inferred datapoint type.
                logger?.LogWarning(ex, "Could not read {Path}; its devices keep project-level data only", path);
            }
        }

        return map;
    }

    private static async Task ReadOneAsync(
        ProjectFileMap files,
        string path,
        Dictionary<string, string> map,
        CancellationToken cancellationToken)
    {
        {
            await using var stream = files.OpenRead(path);
            using var reader = XmlReader.Create(stream, Settings);

            string? currentHardware2Program = null;

            while (await reader.ReadAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (reader.NodeType == XmlNodeType.EndElement
                    && reader.LocalName == "Hardware2Program")
                {
                    currentHardware2Program = null;
                    continue;
                }

                if (reader.NodeType != XmlNodeType.Element) continue;

                switch (reader.LocalName)
                {
                    case "Hardware2Program":
                        currentHardware2Program = reader.GetAttribute("Id");
                        if (reader.IsEmptyElement) currentHardware2Program = null;
                        break;

                    case "ApplicationProgramRef":
                        var programId = reader.GetAttribute("RefId");
                        if (currentHardware2Program != null && !string.IsNullOrEmpty(programId))
                        {
                            map[currentHardware2Program] = programId;
                        }
                        break;
                }
            }
        }
    }
}
