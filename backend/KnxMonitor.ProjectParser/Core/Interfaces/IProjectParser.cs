using KnxMonitor.ProjectParser.Core.Models;

namespace KnxMonitor.ProjectParser.Core.Interfaces;

public interface IProjectParser
{
    /// <summary>
    /// Parse .knxproj file (Basic features: GAs, Devices)
    /// Compatible with existing Wizard
    /// </summary>
    Task<ParseResult> ParseAsync(
        Stream stream,
        ParserOptions options,
        IProgress<ParserProgress>? progress = null,
        CancellationToken cancellationToken = default
    );
}
