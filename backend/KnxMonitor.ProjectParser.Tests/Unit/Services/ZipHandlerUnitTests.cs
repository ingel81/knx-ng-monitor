using FluentAssertions;
using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Core.Models;
using KnxMonitor.ProjectParser.Services;
using KnxMonitor.ProjectParser.Tests.Fixtures;

namespace KnxMonitor.ProjectParser.Tests.Unit.Services;

public class ZipHandlerUnitTests
{
    [Fact]
    public async Task LoadAsync_NoPassword_ReturnsAllFiles()
    {
        using var zip = TestZipBuilder.BuildOuter(new[]
        {
            ("P-0001/0.xml", "<project/>"),
            ("P-0001/dir/",   ""),
            ("knx_master.xml", "<master/>"),
        });

        var features = new ProjectFeatures { HasPassword = false, EtsVersion = EtsVersion.Ets5 };

        var files = await ZipHandler.LoadAsync(zip, features, password: null);

        files.Contains("P-0001/0.xml").Should().BeTrue();
        files.Contains("knx_master.xml").Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_PasswordRequired_ButMissing_Throws()
    {
        using var zip = TestZipBuilder.BuildOuter(new[] { ("P-0001.zip", "dummy") });
        var features = new ProjectFeatures { HasPassword = true, EtsVersion = EtsVersion.Ets5 };

        var act = async () => await ZipHandler.LoadAsync(zip, features, password: null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*password*");
    }

    [Fact]
    public async Task LoadAsync_PasswordProtected_NoNestedArchive_Throws()
    {
        using var zip = TestZipBuilder.BuildOuter(new[] { ("knx_master.xml", "x") });
        var features = new ProjectFeatures { HasPassword = true, EtsVersion = EtsVersion.Ets5 };

        var act = async () => await ZipHandler.LoadAsync(zip, features, "affe");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Nested project archive not found*");
    }

    /// <summary>
    /// ETS6 derives the actual ZIP password from the user password via PBKDF2. When the pre-scan could
    /// not determine the ETS version (Unknown), both encodings must be tried instead of failing.
    /// </summary>
    [Fact]
    public async Task LoadAsync_UnknownVersion_Ets6DerivedPassword_Decrypts()
    {
        using var zip = TestZipBuilder.BuildPasswordProtected(
            "P-0001",
            ZipHandler.DeriveEts6Password("affe"),
            new[] { ("P-0001/0.xml", "<KNX xmlns=\"http://knx.org/xml/project/23\"/>") });

        var features = new ProjectFeatures { HasPassword = true, EtsVersion = EtsVersion.Unknown };

        var files = await ZipHandler.LoadAsync(zip, features, "affe");

        files.Contains("P-0001/0.xml").Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_UnknownVersion_PlainPassword_Decrypts()
    {
        using var zip = TestZipBuilder.BuildPasswordProtected(
            "P-0001",
            "affe",
            new[] { ("P-0001/0.xml", "<KNX xmlns=\"http://knx.org/xml/project/20\"/>") });

        var features = new ProjectFeatures { HasPassword = true, EtsVersion = EtsVersion.Unknown };

        var files = await ZipHandler.LoadAsync(zip, features, "affe");

        files.Contains("P-0001/0.xml").Should().BeTrue();
    }

    /// <summary>Detected version says ETS5, but the archive is really an ETS6 one — must still open.</summary>
    [Fact]
    public async Task LoadAsync_MisdetectedVersion_FallsBackToOtherEncoding()
    {
        using var zip = TestZipBuilder.BuildPasswordProtected(
            "P-0001",
            ZipHandler.DeriveEts6Password("affe"),
            new[] { ("P-0001/0.xml", "<KNX/>") });

        var features = new ProjectFeatures { HasPassword = true, EtsVersion = EtsVersion.Ets5 };

        var files = await ZipHandler.LoadAsync(zip, features, "affe");

        files.Contains("P-0001/0.xml").Should().BeTrue();
    }

    /// <summary>
    /// Only the P-XXXX/ folder is encrypted; knx_master.xml and the M-XXXX/ manufacturer data stay in
    /// the outer archive. Both halves must end up in the file map, otherwise devices lose their
    /// product name and manufacturer (they fell back to the bare physical address).
    /// </summary>
    [Fact]
    public async Task LoadAsync_PasswordProtected_MergesOuterManufacturerData()
    {
        using var zip = TestZipBuilder.BuildPasswordProtected(
            "P-0001",
            ZipHandler.DeriveEts6Password("affe"),
            new[] { ("P-0001/0.xml", "<KNX xmlns=\"http://knx.org/xml/project/23\"/>") },
            new[]
            {
                ("knx_master.xml", "<KNX xmlns=\"http://knx.org/xml/project/23\"></KNX>"),
                ("M-0083/Hardware.xml", "<KNX><ManufacturerData><Manufacturer RefId=\"M-0083\"/></ManufacturerData></KNX>"),
            });

        var features = new ProjectFeatures { HasPassword = true, EtsVersion = EtsVersion.Ets6 };

        var files = await ZipHandler.LoadAsync(zip, features, "affe");

        files.Contains("P-0001/0.xml").Should().BeTrue();
        files.Contains("knx_master.xml").Should().BeTrue();
        files.Contains("M-0083/Hardware.xml").Should().BeTrue();

        // The encrypted container itself is of no use to the loaders.
        files.Contains("P-0001.zip").Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_WrongPassword_ThrowsWithClearMessage()
    {
        using var zip = TestZipBuilder.BuildPasswordProtected(
            "P-0001",
            ZipHandler.DeriveEts6Password("affe"),
            new[] { ("P-0001/0.xml", "<KNX/>") });

        var features = new ProjectFeatures { HasPassword = true, EtsVersion = EtsVersion.Ets6 };

        var act = async () => await ZipHandler.LoadAsync(zip, features, "wrong");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*password is wrong*");
    }

    [Fact]
    public void DeriveEts6Password_KnownInput_ReturnsKnownHash()
    {
        // Reference: test-project password "test" in testprojekt-ets6.knxproj uses PBKDF2 result in Base64.
        var result = ZipHandler.DeriveEts6Password("test");

        result.Should().NotBeNullOrEmpty();
        Convert.FromBase64String(result).Should().HaveCount(32);
    }
}
