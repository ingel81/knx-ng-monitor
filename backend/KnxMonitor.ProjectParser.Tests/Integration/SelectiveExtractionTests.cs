using FluentAssertions;
using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Core.Models;
using KnxMonitor.ProjectParser.Loaders;
using KnxMonitor.ProjectParser.Services;
using KnxMonitor.ProjectParser.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnxMonitor.ProjectParser.Tests.Integration;

/// <summary>
/// An import must touch the files it needs and nothing else. The manufacturer application programs
/// are 87 to 99 % of a project's uncompressed volume (a 6.7 MB project unpacked to 128 MB, its
/// largest single file 52.6 MB), so unpacking the archive wholesale risked an OOM on a Pi or a
/// memory-capped container. Catalogs, baggages and signatures are never read at all, and the
/// application programs are streamed rather than held.
/// </summary>
public class SelectiveExtractionTests
{
    /// <summary>
    /// Allocations of THIS thread only. <c>GC.GetTotalAllocatedBytes</c> counts the whole process and
    /// would pick up whatever other test classes allocate in parallel. Indexing a plain archive never
    /// awaits anything incomplete, so the measured work stays on the calling thread.
    /// </summary>
    private static async Task<(ProjectFileMap Files, long Allocated)> MeasureIndexingAsync(
        string sample, EtsVersion version)
    {
        using var stream = File.OpenRead(TestSamples.Path(sample));
        var features = new ProjectFeatures { EtsVersion = version, HasPassword = false };

        var before = GC.GetAllocatedBytesForCurrentThread();
        var task = ZipHandler.LoadAsync(stream, features, password: null);
        task.IsCompletedSuccessfully.Should().BeTrue("indexing a plain archive does not await anything");
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        return (await task, allocated);
    }

    private static async Task<(List<string> Accessed, ParseResult Result)> LoadEts4SampleAsync()
    {
        await using var stream = File.OpenRead(TestSamples.Path("test_project-ets4-no_password.knxproj"));

        var features = new ProjectFeatures { EtsVersion = EtsVersion.Ets4, HasPassword = false };
        using var files = await ZipHandler.LoadAsync(stream, features, password: null);

        var loader = new Ets4ProjectLoader(NullLogger<Ets4ProjectLoader>.Instance);
        var result = await loader.LoadAsync(files, features, new ParserOptions());

        return (files.AccessedPaths.ToList(), result);
    }

    [Fact]
    public async Task FullLoad_ReadsOnlyTheFilesItNeeds()
    {
        var (accessed, result) = await LoadEts4SampleAsync();

        result.GroupAddresses.Should().HaveCount(3);

        accessed.Should().Contain(p => p.EndsWith("0.xml"));
        accessed.Should().Contain(p => p.EndsWith("knx_master.xml"));
        accessed.Should().Contain(p => p.EndsWith("Hardware.xml"));

        // The application programs are needed for the datapoint-type cascade — but read by streaming.
        accessed.Should().Contain("M-0083/M-0083_A-0013-11-A9D6.xml");
        accessed.Should().Contain("M-0002/M-0002_A-A061-14-BE27.xml");
    }

    [Fact]
    public async Task FullLoad_NeverTouchesCatalogsBaggagesOrSignatures()
    {
        var (accessed, _) = await LoadEts4SampleAsync();

        accessed.Should().NotContain(p => p.EndsWith("Catalog.xml"));
        accessed.Should().NotContain(p => p.Contains("Baggage"));
        accessed.Should().NotContain(p => p.EndsWith(".signature"));
        accessed.Should().NotContain(p => p.EndsWith(".dll"));
    }

    /// <summary>
    /// The archive holds 4.8 MB uncompressed, 3.5 MB of it application programs. Opening it and
    /// indexing the entries must not allocate anything like that — which is what the old
    /// "unpack every entry into a byte[]" path did.
    /// </summary>
    [Fact]
    public async Task OpeningTheArchive_DoesNotAllocateItsContents()
    {
        var (files, allocated) = await MeasureIndexingAsync(
            "test_project-ets4-no_password.knxproj", EtsVersion.Ets4);

        using (files)
        {
            files.Count.Should().BeGreaterThan(10);
            files.AccessedPaths.Should().BeEmpty();
        }

        allocated.Should().BeLessThan(500_000);
    }

    /// <summary>
    /// The large real-world project this work was measured against: 6.7 MB packed, 128 MB unpacked,
    /// largest single file 52.6 MB. Skipped where the private samples are not present.
    /// </summary>
    [SkippableFact]
    public async Task OpeningALargeArchive_CostsTheDirectoryOnly()
    {
        const string sample = "myProject_ets_v5.7.7.knxproj";
        Skip.IfNot(TestSamples.Exists(sample), $"{sample} not available");

        var (files, allocated) = await MeasureIndexingAsync(sample, EtsVersion.Ets5);

        using (files)
        {
            files.Count.Should().BeGreaterThan(50);
            files.AccessedPaths.Should().BeEmpty();
        }

        allocated.Should().BeLessThan(2_000_000);
    }

    /// <summary>
    /// The same project loaded end to end: it ships 31 application programs and uses 24, so a run
    /// that reads all of them would mean the selection is not working.
    /// </summary>
    [SkippableFact]
    public async Task LargeProject_ReadsOnlyTheApplicationProgramsItUses()
    {
        const string sample = "myProject_ets_v5.7.7.knxproj";
        Skip.IfNot(TestSamples.Exists(sample), $"{sample} not available");

        await using var stream = File.OpenRead(TestSamples.Path(sample));
        var features = new ProjectFeatures { EtsVersion = EtsVersion.Ets5, HasPassword = false };

        using var files = await ZipHandler.LoadAsync(stream, features, password: null);

        var loader = new Ets5ProjectLoader(NullLogger<Ets5ProjectLoader>.Instance);
        var result = await loader.LoadAsync(files, features, new ParserOptions());
        result.GroupAddresses.Should().HaveCount(841);

        static bool IsApplicationProgram(string path) =>
            path.Contains("_A-", StringComparison.Ordinal) && path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

        var available = files.FilePaths.Count(IsApplicationProgram);
        var read = files.AccessedPaths.Count(IsApplicationProgram);

        available.Should().BeGreaterThan(0);
        read.Should().BeLessThan(available, "an application program no device uses is never opened");

        files.AccessedPaths.Should().NotContain(p => p.EndsWith("Catalog.xml"));
        files.AccessedPaths.Should().NotContain(p => p.Contains("Baggage"));
    }
}
