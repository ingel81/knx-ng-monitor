using System.Xml.Linq;
using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Core.Interfaces;
using KnxMonitor.ProjectParser.Core.Models;
using KnxMonitor.ProjectParser.Helpers;
using Microsoft.Extensions.Logging;

namespace KnxMonitor.ProjectParser.Services;

public class ProjectParser : IProjectParser
{
    private readonly IEnumerable<IProjectLoader> _loaders;
    private readonly IFeatureDetector _featureDetector;
    private readonly IKeyringReader _keyringReader;
    private readonly ILogger<ProjectParser> _logger;

    public ProjectParser(
        IEnumerable<IProjectLoader> loaders,
        IFeatureDetector featureDetector,
        IKeyringReader keyringReader,
        ILogger<ProjectParser> logger)
    {
        _loaders = loaders;
        _featureDetector = featureDetector;
        _keyringReader = keyringReader;
        _logger = logger;
    }

    public async Task<ParseResult> ParseAsync(
        Stream stream,
        ParserOptions options,
        IProgress<ParserProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            // 1. Detect features
            ReportProgress(progress, ParseStep.DetectFeatures, 0);
            var features = await _featureDetector.DetectAsync(stream, cancellationToken);

            // 2. Open ZIP (mit Password falls nötig). Läuft VOR der Loader-Auswahl, damit eine
            //    unbekannte ETS-Version noch aus den entpackten Projektdateien nachgezogen werden
            //    kann — bei passwortgeschützten Projekten sind die vorher nicht lesbar.
            ReportProgress(progress, ParseStep.OpenZip, 0);
            var files = await ZipHandler.LoadAsync(
                stream,
                features,
                options.Password,
                progress,
                cancellationToken
            );

            // 3. Refine features from the now-readable project files, then select the loader. Only
            //    needed when the pre-scan could not see them (encrypted) or came up empty — for a
            //    plain project the detector already read the very same files.
            if (features.HasPassword || features.EtsVersion == EtsVersion.Unknown)
            {
                await RefineFeaturesFromExtractedFilesAsync(files, features, cancellationToken);
            }

            var loader = _loaders.FirstOrDefault(l => l.CanLoad(features));
            if (loader == null)
            {
                throw new NotSupportedException(
                    $"No loader available for ETS version {features.EtsVersion}. The project schema "
                    + "could not be determined from knx_master.xml, project.xml or 0.xml."
                );
            }

            _logger.LogInformation("Using loader: {Loader}", loader.GetType().Name);

            // 4. Load project data
            var result = await loader.LoadAsync(files, features, options, progress, cancellationToken);

            // 4b. Optional keyring
            if (options.KeyringStream != null)
            {
                if (string.IsNullOrEmpty(options.KeyringPassword))
                {
                    throw new InvalidOperationException("KeyringStream provided without KeyringPassword");
                }
                result.Keyring = await _keyringReader.ReadAsync(
                    options.KeyringStream,
                    options.KeyringPassword,
                    cancellationToken);
            }

            // 5. Statistics
            result.Features = features;
            result.Statistics = new ParseStatistics
            {
                Duration = DateTime.UtcNow - startTime,
                GroupAddressCount = result.GroupAddresses.Count,
                DeviceCount = result.Devices.Count,
                LocationCount = result.Locations.Count,
                CommunicationObjectCount = result.CommunicationObjects.Count
            };

            ReportProgress(progress, ParseStep.Complete, 100);

            _logger.LogInformation(
                "Parsed successfully: {GAs} GAs, {Devices} Devices in {Duration}ms",
                result.GroupAddresses.Count,
                result.Devices.Count,
                result.Statistics.Duration.TotalMilliseconds
            );

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse project");
            throw;
        }
    }

    /// <summary>
    /// Re-read ETS version and group-address style from the DECRYPTED archive contents. The pre-scan
    /// only sees knx_master.xml in a password-protected project, which means:
    /// <list type="bullet">
    ///   <item>the version stays Unknown when knx_master.xml is missing or carries no known schema —
    ///         no loader would match, which is what GitHub issue #2 reported;</item>
    ///   <item>the addressing style silently defaults to ThreeLevel, so a TwoLevel/Free project would
    ///         get wrong group-address strings ("0/0/1" instead of "0/1").</item>
    /// </list>
    /// project.xml / 0.xml carry both markers and are readable at this point, so they win.
    /// </summary>
    private async Task RefineFeaturesFromExtractedFilesAsync(
        ProjectFileMap files,
        ProjectFeatures features,
        CancellationToken cancellationToken)
    {
        var styleFound = false;

        foreach (var fileName in new[] { "project.xml", "0.xml" })
        {
            foreach (var path in files.FindAllByName(fileName))
            {
                if (features.EtsVersion != EtsVersion.Unknown && styleFound)
                    return;

                XDocument xml;
                try
                {
                    await using var stream = files.OpenRead(path);
                    xml = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogDebug(ex, "Could not read project metadata from {Path}", path);
                    continue;
                }

                if (features.EtsVersion == EtsVersion.Unknown)
                {
                    var version = ProjectXmlMetadata.ReadEtsVersion(xml.Root);
                    if (version != EtsVersion.Unknown)
                    {
                        _logger.LogInformation(
                            "ETS version {Version} recovered from {Path} after opening the archive",
                            version, path);
                        features.EtsVersion = version;
                    }
                }

                if (!styleFound)
                {
                    var style = ProjectXmlMetadata.ReadAddressingStyle(xml.Root);
                    if (style.HasValue)
                    {
                        if (style.Value != features.AddressingStyle)
                        {
                            _logger.LogInformation(
                                "Addressing style corrected to {Style} from {Path}", style.Value, path);
                        }
                        features.AddressingStyle = style.Value;
                        styleFound = true;
                    }
                }
            }
        }
    }

    private void ReportProgress(
        IProgress<ParserProgress>? progress,
        ParseStep step,
        int percent)
    {
        progress?.Report(new ParserProgress
        {
            Step = step,
            PercentComplete = percent
        });
    }
}
