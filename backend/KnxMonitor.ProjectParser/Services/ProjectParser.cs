using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Core.Interfaces;
using KnxMonitor.ProjectParser.Core.Models;
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

            // 2. Select appropriate loader
            var loader = _loaders.FirstOrDefault(l => l.CanLoad(features));
            if (loader == null)
            {
                throw new NotSupportedException(
                    $"No loader available for ETS version {features.EtsVersion}"
                );
            }

            _logger.LogInformation("Using loader: {Loader}", loader.GetType().Name);

            // 3. Open ZIP (mit Password falls nötig)
            ReportProgress(progress, ParseStep.OpenZip, 0);
            var files = await ZipHandler.LoadAsync(
                stream,
                features,
                options.Password,
                progress,
                cancellationToken
            );

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
