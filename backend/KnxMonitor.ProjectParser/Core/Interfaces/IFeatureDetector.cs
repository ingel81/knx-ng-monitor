using KnxMonitor.ProjectParser.Core.Models;

namespace KnxMonitor.ProjectParser.Core.Interfaces;

public interface IFeatureDetector
{
    /// <summary>
    /// Detect project features without full parsing
    /// Fast pre-scan for Wizard Requirements-Dialog
    /// </summary>
    Task<ProjectFeatures> DetectAsync(
        Stream stream,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Performs feature detection on a project, optionally unlocking it with the given password
    /// to determine KNX Secure usage that is hidden inside the encrypted inner archive.
    /// </summary>
    Task<ProjectFeatures> DetectAfterUnlockAsync(
        Stream stream,
        string? password,
        CancellationToken cancellationToken = default
    );
}
