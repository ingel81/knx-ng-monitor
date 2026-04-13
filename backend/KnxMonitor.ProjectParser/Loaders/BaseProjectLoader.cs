using System.Xml.Linq;
using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Core.Interfaces;
using KnxMonitor.ProjectParser.Core.Models;
using Microsoft.Extensions.Logging;

namespace KnxMonitor.ProjectParser.Loaders;

public abstract class BaseProjectLoader : IProjectLoader
{
    protected readonly ILogger _logger;

    public abstract EtsVersion SupportedVersion { get; }

    protected BaseProjectLoader(ILogger logger)
    {
        _logger = logger;
    }

    public virtual bool CanLoad(ProjectFeatures features)
    {
        return features.EtsVersion == SupportedVersion;
    }

    public async Task<ParseResult> LoadAsync(
        ProjectFileMap files,
        ProjectFeatures features,
        ParserOptions options,
        IProgress<ParserProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ParseResult();

        try
        {
            var projectXml = await LoadProjectXmlAsync(files, progress, cancellationToken);

            progress?.Report(new ParserProgress
            {
                Step = ParseStep.ParseGroupAddresses,
                PercentComplete = 0
            });

            result.GroupAddresses = await ParseGroupAddressesAsync(
                projectXml,
                features.AddressingStyle,
                cancellationToken
            );

            progress?.Report(new ParserProgress
            {
                Step = ParseStep.ParseGroupAddresses,
                PercentComplete = 100
            });

            progress?.Report(new ParserProgress
            {
                Step = ParseStep.ParseDevices,
                PercentComplete = 0
            });

            result.Devices = await ParseDevicesAsync(
                files,
                projectXml,
                cancellationToken
            );

            progress?.Report(new ParserProgress
            {
                Step = ParseStep.ParseDevices,
                PercentComplete = 100
            });

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load project");
            throw;
        }
    }

    protected abstract Task<XDocument> LoadProjectXmlAsync(
        ProjectFileMap files,
        IProgress<ParserProgress>? progress,
        CancellationToken cancellationToken
    );

    protected abstract Task<List<GroupAddress>> ParseGroupAddressesAsync(
        XDocument projectXml,
        AddressingStyle addressingStyle,
        CancellationToken cancellationToken
    );

    protected abstract Task<List<Device>> ParseDevicesAsync(
        ProjectFileMap files,
        XDocument projectXml,
        CancellationToken cancellationToken
    );

    protected XNamespace GetNamespace(XDocument xml)
    {
        return xml.Root?.Name.Namespace ?? XNamespace.None;
    }

    protected static async Task<XDocument> LoadProjectXmlFromFilesAsync(
        ProjectFileMap files,
        CancellationToken cancellationToken)
    {
        var path = files.FindFirstByName("0.xml")
                   ?? throw new FileNotFoundException("Project file (0.xml) not found in archive");

        await using var stream = files.OpenRead(path);
        return await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
    }

    protected async Task<Dictionary<string, (string Manufacturer, string ProductName)>>
        LoadHardwareDataAsync(ProjectFileMap files, CancellationToken cancellationToken)
    {
        var hardwareData = new Dictionary<string, (string, string)>();

        foreach (var path in files.FindAllByName("Hardware.xml"))
        {
            await using var stream = files.OpenRead(path);
            var xml = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
            var ns = GetNamespace(xml);

            var manufacturer = xml.Root?
                .Attribute("RefId")?.Value?
                .Split('-').FirstOrDefault() ?? "Unknown";

            foreach (var product in xml.Descendants(ns + "Product"))
            {
                var productRefId = product.Attribute("Id")?.Value;
                var productName = product.Attribute("Text")?.Value ?? "Unknown";

                if (productRefId != null)
                {
                    hardwareData[productRefId] = (manufacturer, productName);
                }
            }
        }

        return hardwareData;
    }
}
