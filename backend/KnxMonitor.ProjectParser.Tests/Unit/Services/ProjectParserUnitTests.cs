using FluentAssertions;
using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Core.Interfaces;
using KnxMonitor.ProjectParser.Core.Models;
using KnxMonitor.ProjectParser.Services;
using KnxMonitor.ProjectParser.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KnxMonitor.ProjectParser.Tests.Unit.Services;

public class ProjectParserUnitTests
{
    [Fact]
    public async Task ParseAsync_NoMatchingLoader_ThrowsNotSupported()
    {
        var featureDetector = new Mock<IFeatureDetector>();
        featureDetector
            .Setup(d => d.DetectAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectFeatures { EtsVersion = EtsVersion.Unknown });

        var keyringReader = new Mock<IKeyringReader>();

        var parser = new ProjectParser.Services.ProjectParser(
            Array.Empty<IProjectLoader>(),
            featureDetector.Object,
            keyringReader.Object,
            NullLogger<ProjectParser.Services.ProjectParser>.Instance);

        using var zip = TestZipBuilder.BuildOuter(new[] { ("x", "y") });
        var options = new ParserOptions();

        var act = async () => await parser.ParseAsync(zip, options);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*No loader available for ETS version*");
    }

    [Fact]
    public async Task ParseAsync_LoaderThrows_Rethrows()
    {
        var featureDetector = new Mock<IFeatureDetector>();
        featureDetector
            .Setup(d => d.DetectAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectFeatures { EtsVersion = EtsVersion.Ets5 });

        var keyringReader = new Mock<IKeyringReader>();

        var loader = new Mock<IProjectLoader>();
        loader.Setup(l => l.SupportedVersion).Returns(EtsVersion.Ets5);
        loader.Setup(l => l.CanLoad(It.IsAny<ProjectFeatures>())).Returns(true);
        loader.Setup(l => l.LoadAsync(
                It.IsAny<ProjectFileMap>(),
                It.IsAny<ProjectFeatures>(),
                It.IsAny<ParserOptions>(),
                It.IsAny<IProgress<ParserProgress>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("loader boom"));

        var parser = new ProjectParser.Services.ProjectParser(
            new[] { loader.Object },
            featureDetector.Object,
            keyringReader.Object,
            NullLogger<ProjectParser.Services.ProjectParser>.Instance);

        using var zip = TestZipBuilder.BuildOuter(new[] { ("x", "<y/>") });
        var options = new ParserOptions();

        var act = async () => await parser.ParseAsync(zip, options);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*loader boom*");
    }
}
