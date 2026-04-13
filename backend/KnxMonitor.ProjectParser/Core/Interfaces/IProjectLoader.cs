using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Core.Models;

namespace KnxMonitor.ProjectParser.Core.Interfaces;

public interface IProjectLoader
{
    /// <summary>
    /// Supported ETS version
    /// </summary>
    EtsVersion SupportedVersion { get; }

    /// <summary>
    /// Load project data from an in-memory file map
    /// </summary>
    Task<ParseResult> LoadAsync(
        ProjectFileMap files,
        ProjectFeatures features,
        ParserOptions options,
        IProgress<ParserProgress>? progress = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Check if loader can handle this project
    /// </summary>
    bool CanLoad(ProjectFeatures features);
}
