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

    /// <summary>
    /// Worst case of GitHub issue #2: a password-protected project whose knx_master.xml is missing or
    /// carries no usable schema, so the pre-scan yields Unknown. Only after decrypting the archive do
    /// project.xml / 0.xml become readable — the parser must pick up the version there and still find
    /// a loader instead of throwing "No loader available for ETS version Unknown".
    /// </summary>
    [Fact]
    public async Task ParseAsync_UnknownVersionBeforeUnlock_RecoversFromDecryptedFiles()
    {
        var loader = new Mock<IProjectLoader>();
        loader.Setup(l => l.SupportedVersion).Returns(EtsVersion.Ets6);
        loader.Setup(l => l.CanLoad(It.Is<ProjectFeatures>(f => f.EtsVersion == EtsVersion.Ets6)))
            .Returns(true);
        loader.Setup(l => l.LoadAsync(
                It.IsAny<ProjectFileMap>(),
                It.IsAny<ProjectFeatures>(),
                It.IsAny<ParserOptions>(),
                It.IsAny<IProgress<ParserProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParseResult());

        var parser = new ProjectParser.Services.ProjectParser(
            new[] { loader.Object },
            new FeatureDetector(NullLogger<FeatureDetector>.Instance),
            new Mock<IKeyringReader>().Object,
            NullLogger<ProjectParser.Services.ProjectParser>.Instance);

        // No knx_master.xml in the outer archive -> nothing to detect the version from up front.
        using var zip = TestZipBuilder.BuildPasswordProtected(
            "P-0001",
            ZipHandler.DeriveEts6Password("affe"),
            new[]
            {
                ("P-0001/project.xml", "<KNX xmlns=\"http://knx.org/xml/project/23\" CreatedBy=\"ETS6\"/>"),
                ("P-0001/0.xml", "<KNX xmlns=\"http://knx.org/xml/project/23\"/>")
            });

        var result = await parser.ParseAsync(zip, new ParserOptions { Password = "affe" });

        result.Features!.EtsVersion.Should().Be(EtsVersion.Ets6);
        loader.Verify(l => l.LoadAsync(
            It.IsAny<ProjectFileMap>(),
            It.IsAny<ProjectFeatures>(),
            It.IsAny<ParserOptions>(),
            It.IsAny<IProgress<ParserProgress>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// In a password-protected project the pre-scan cannot see project.xml, so the addressing style
    /// falls back to ThreeLevel. After decryption the real style must win — otherwise a TwoLevel or
    /// Free project silently gets three-part group addresses.
    /// </summary>
    [Fact]
    public async Task ParseAsync_PasswordProtected_CorrectsAddressingStyleAfterUnlock()
    {
        ProjectFeatures? seenFeatures = null;

        var loader = new Mock<IProjectLoader>();
        loader.Setup(l => l.SupportedVersion).Returns(EtsVersion.Ets6);
        loader.Setup(l => l.CanLoad(It.IsAny<ProjectFeatures>())).Returns(true);
        loader.Setup(l => l.LoadAsync(
                It.IsAny<ProjectFileMap>(),
                It.IsAny<ProjectFeatures>(),
                It.IsAny<ParserOptions>(),
                It.IsAny<IProgress<ParserProgress>>(),
                It.IsAny<CancellationToken>()))
            .Callback<ProjectFileMap, ProjectFeatures, ParserOptions, IProgress<ParserProgress>?, CancellationToken>(
                (_, f, _, _, _) => seenFeatures = f)
            .ReturnsAsync(new ParseResult());

        var parser = new ProjectParser.Services.ProjectParser(
            new[] { loader.Object },
            new FeatureDetector(NullLogger<FeatureDetector>.Instance),
            new Mock<IKeyringReader>().Object,
            NullLogger<ProjectParser.Services.ProjectParser>.Instance);

        using var zip = TestZipBuilder.BuildPasswordProtected(
            "P-0001",
            ZipHandler.DeriveEts6Password("affe"),
            new[]
            {
                ("P-0001/project.xml",
                    "<KNX xmlns=\"http://knx.org/xml/project/23\"><Project Id=\"P-0001\">"
                    + "<ProjectInformation Name=\"x\" GroupAddressStyle=\"TwoLevel\" /></Project></KNX>")
            },
            new[] { ("knx_master.xml", "<KNX xmlns=\"http://knx.org/xml/project/23\"></KNX>") });

        var result = await parser.ParseAsync(zip, new ParserOptions { Password = "affe" });

        result.Features!.AddressingStyle.Should().Be(AddressingStyle.TwoLevel);
        seenFeatures!.AddressingStyle.Should().Be(AddressingStyle.TwoLevel);
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
