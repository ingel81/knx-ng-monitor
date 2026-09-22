using System.Text;
using FluentAssertions;
using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Core.Models;
using KnxMonitor.ProjectParser.Services;
using KnxMonitor.ProjectParser.Tests.Fixtures;

namespace KnxMonitor.ProjectParser.Tests.Unit.Services;

/// <summary>
/// The file map must stay lazy: an import reads 0.xml, project.xml, knx_master.xml, Hardware.xml and
/// (for the DPT cascade) the application programs it actually needs. Everything else — above all the
/// remaining M-XXXX/*_A-*.xml, which are 87 to 99 % of the uncompressed volume — must never be
/// touched. These tests pin that down; without them the map silently falls back to unpacking
/// everything and a big project blows the memory budget of a Pi or a capped container.
/// </summary>
public class ZipHandlerLazyTests
{
    private static string Filler(int bytes) => new('x', bytes);

    [Fact]
    public async Task LoadAsync_DoesNotReadAnyEntryUpFront()
    {
        using var zip = TestZipBuilder.BuildOuter(new[]
        {
            ("P-0001/0.xml", "<project/>"),
            ("knx_master.xml", "<master/>"),
            ("M-0083/M-0083_A-0013-11-A9D6.xml", Filler(2_000_000)),
        });

        var features = new ProjectFeatures { HasPassword = false, EtsVersion = EtsVersion.Ets5 };

        using var files = await ZipHandler.LoadAsync(zip, features, password: null);

        files.Count.Should().Be(3);
        files.AccessedPaths.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_MaterialisesOnlyTheEntriesThatAreRead()
    {
        using var zip = TestZipBuilder.BuildOuter(new[]
        {
            ("P-0001/0.xml", "<project/>"),
            ("knx_master.xml", "<master/>"),
            ("M-0083/M-0083_A-0013-11-A9D6.xml", Filler(2_000_000)),
        });

        var features = new ProjectFeatures { HasPassword = false, EtsVersion = EtsVersion.Ets5 };

        using var files = await ZipHandler.LoadAsync(zip, features, password: null);

        using (var stream = files.OpenRead("P-0001/0.xml"))
        {
            using var reader = new StreamReader(stream);
            (await reader.ReadToEndAsync()).Should().Be("<project/>");
        }

        files.AccessedPaths.Should().BeEquivalentTo(new[] { "P-0001/0.xml" });
    }

    /// <summary>
    /// Indexing a large archive must cost a directory read, not its contents. The single application
    /// program below is 8 MB uncompressed; allocating even a fraction of that during LoadAsync would
    /// mean the eager path is back.
    /// </summary>
    [Fact]
    public async Task LoadAsync_AllocatesFarLessThanTheUncompressedSize()
    {
        using var zip = TestZipBuilder.BuildOuter(new[]
        {
            ("P-0001/0.xml", "<project/>"),
            ("M-0083/M-0083_A-0013-11-A9D6.xml", Filler(8_000_000)),
        });

        var features = new ProjectFeatures { HasPassword = false, EtsVersion = EtsVersion.Ets5 };

        // Thread-local: GC.GetTotalAllocatedBytes counts the whole process and would pick up what
        // other test classes allocate in parallel. Indexing a plain archive never awaits.
        var before = GC.GetAllocatedBytesForCurrentThread();
        var task = ZipHandler.LoadAsync(zip, features, password: null);
        task.IsCompletedSuccessfully.Should().BeTrue();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        using var files = await task;

        files.Count.Should().Be(2);
        allocated.Should().BeLessThan(1_000_000);
    }

    [Fact]
    public async Task LoadAsync_PasswordProtected_KeepsInnerEntriesLazyToo()
    {
        using var zip = TestZipBuilder.BuildPasswordProtected(
            "P-0001",
            ZipHandler.DeriveEts6Password("affe"),
            new[]
            {
                ("P-0001/0.xml", "<KNX xmlns=\"http://knx.org/xml/project/23\"/>"),
                ("P-0001/project.xml", "<KNX/>"),
            },
            new[] { ("M-0083/M-0083_A-0013-11-A9D6.xml", Filler(1_000_000)) });

        var features = new ProjectFeatures { HasPassword = true, EtsVersion = EtsVersion.Ets6 };

        using var files = await ZipHandler.LoadAsync(zip, features, "affe");

        // Verifying the password decrypts exactly one small entry; that happens inside ZipHandler and
        // is not an entry read through the map.
        files.AccessedPaths.Should().BeEmpty();

        using var stream = files.OpenRead("P-0001/0.xml");
        using var reader = new StreamReader(stream);
        (await reader.ReadToEndAsync()).Should().Contain("project/23");

        files.AccessedPaths.Should().BeEquivalentTo(new[] { "P-0001/0.xml" });
    }

    [Fact]
    public async Task OpenRead_AfterDispose_Throws()
    {
        using var zip = TestZipBuilder.BuildOuter(new[] { ("P-0001/0.xml", "<project/>") });
        var features = new ProjectFeatures { HasPassword = false, EtsVersion = EtsVersion.Ets5 };

        var files = await ZipHandler.LoadAsync(zip, features, password: null);
        files.Dispose();

        var act = () => files.OpenRead("P-0001/0.xml");

        act.Should().Throw<ObjectDisposedException>();
    }

    /// <summary>Entries are opened on demand, so two of them may legitimately be open at once.</summary>
    [Fact]
    public async Task OpenRead_TwoEntriesSimultaneously_BothReadCorrectly()
    {
        using var zip = TestZipBuilder.BuildOuter(new[]
        {
            ("a.xml", "<a/>"),
            ("b.xml", "<b/>"),
        });

        var features = new ProjectFeatures { HasPassword = false, EtsVersion = EtsVersion.Ets5 };

        using var files = await ZipHandler.LoadAsync(zip, features, password: null);

        using var first = files.OpenRead("a.xml");
        using var second = files.OpenRead("b.xml");

        using var firstReader = new StreamReader(first);
        using var secondReader = new StreamReader(second);

        (await secondReader.ReadToEndAsync()).Should().Be("<b/>");
        (await firstReader.ReadToEndAsync()).Should().Be("<a/>");
    }

    [Fact]
    public async Task GetBytes_LazyEntry_ReturnsFullContent()
    {
        using var zip = TestZipBuilder.BuildOuter(new[] { ("a.xml", "<a/>") });
        var features = new ProjectFeatures { HasPassword = false, EtsVersion = EtsVersion.Ets5 };

        using var files = await ZipHandler.LoadAsync(zip, features, password: null);

        // TestZipBuilder writes through a StreamWriter, so the entry carries a UTF-8 BOM.
        Encoding.UTF8.GetString(files.GetBytes("a.xml")).TrimStart('﻿').Should().Be("<a/>");
        files.AccessedPaths.Should().BeEquivalentTo(new[] { "a.xml" });
    }
}
