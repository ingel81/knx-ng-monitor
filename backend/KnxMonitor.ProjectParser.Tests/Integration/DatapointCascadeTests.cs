using FluentAssertions;
using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Core.Models;
using KnxMonitor.ProjectParser.Tests.Fixtures;

namespace KnxMonitor.ProjectParser.Tests.Integration;

/// <summary>
/// Issue #24: a group address gets its datapoint type from the linked communication objects when
/// <c>GroupAddress/@DatapointType</c> is empty. ETS 4 never writes that attribute and migrating to
/// ETS 5/6 does not add it, so without the cascade those projects show no values and no charts.
/// </summary>
public class DatapointCascadeTests : IClassFixture<SampleFileFixture>
{
    private readonly SampleFileFixture _fixture;

    public DatapointCascadeTests(SampleFileFixture fixture)
    {
        _fixture = fixture;
    }

    private static string? Dpt(ParseResult result, string address)
    {
        var ga = result.GroupAddresses.SingleOrDefault(g => g.Address == address);
        ga.Should().NotBeNull($"the project should contain group address {address}");
        return ga!.DatapointType?.ToString();
    }

    [Fact]
    public async Task SyntheticProject_ResolvesEveryCascadeRule()
    {
        await using var project = SyntheticCascadeProject.Build();

        var result = await _fixture.Parser.ParseAsync(project, new ParserOptions());

        result.Features.EtsVersion.Should().Be(EtsVersion.Ets5);

        // ComObjectRef without a type -> the ComObject behind it.
        Dpt(result, "0/0/1").Should().Be("DPT 1.001");

        // THE decisive case: one ComObject, several refs. The ref's own type wins, so taking the
        // ComObject's DPST-1-1 here would be wrong.
        Dpt(result, "0/0/2").Should().Be("DPT 3.007");

        // DatapointType="" is not a type.
        Dpt(result, "0/0/3").Should().Be("DPT 1.001");

        // A type already present in the project is never touched, not even by an instance ref that
        // claims something else.
        Dpt(result, "0/0/4").Should().Be("DPT 1.001");

        // Nothing but ObjectSize="2 Bytes" -> DPT 9 (Default="true" in knx_master.xml).
        Dpt(result, "0/0/5").Should().Be("DPT 9");

        // One object declares DPST-1-1, the other only has ObjectSize "2 Bytes" (inferred DPT 9).
        // A declaration outranks a guess, so the declared type stands instead of both cancelling out.
        Dpt(result, "0/0/6").Should().Be("DPT 1.001");

        // Two DECLARED main types, though, really are irreconcilable.
        Dpt(result, "0/0/10").Should().BeNull();

        // 4 bytes carries no usable default: DPT 12/13 counters are as common there as DPT 14
        // floats, and decoding a counter as a float would silently persist nonsense.
        Dpt(result, "0/0/11").Should().BeNull();

        // Linked objects disagree on the subtype: fall back to the main type, do not pad to .001.
        Dpt(result, "0/0/7").Should().Be("DPT 1");

        // The instance ref is the most specific level and beats the catalog.
        Dpt(result, "0/0/8").Should().Be("DPT 9.001");

        // An instance attribute that parses to nothing is not a type and may not shut out the
        // catalog behind it.
        Dpt(result, "0/0/9").Should().Be("DPT 1.001");
    }

    /// <summary>
    /// Same rule one level down: the communication object's own DatapointType is the canonical id of
    /// what was understood, and an unreadable instance value falls through to the catalog rather than
    /// being reported as the type.
    /// </summary>
    [Fact]
    public async Task SyntheticProject_UnparseableInstanceType_FallsThroughToTheCatalog()
    {
        await using var project = SyntheticCascadeProject.Build();

        var result = await _fixture.Parser.ParseAsync(project, new ParserOptions());

        var comObject = result.CommunicationObjects.Single(c => c.GroupAddressLinks.Contains("0/0/9"));
        comObject.DatapointType.Should().Be("DPST-1-1");
    }

    /// <summary>
    /// The three-level flag merge from COMOBJECT_FLAGS_PLAN.md: the ComObject carries the defaults,
    /// the ComObjectRef overrides single attributes. Reading the instance ref alone left every
    /// ETS 5/6 communication object without flags.
    /// </summary>
    [Fact]
    public async Task SyntheticProject_MergesFlagsFromCatalog()
    {
        await using var project = SyntheticCascadeProject.Build();

        var result = await _fixture.Parser.ParseAsync(project, new ParserOptions());

        var fromComObject = result.CommunicationObjects
            .Single(c => c.GroupAddressLinks.Contains("0/0/1"));
        fromComObject.Flags.Should().Be("Write,Communication");

        // O-1_R-3 sets ReadFlag="Enabled", which overlays the ComObject's "Disabled".
        var overridden = result.CommunicationObjects
            .Single(c => c.GroupAddressLinks.Contains("0/0/3"));
        overridden.Flags.Should().Be("Read,Write,Communication");
    }

    /// <summary>
    /// ETS 4 is the whole point of the issue: the project file has no DatapointType anywhere, so
    /// every type has to come out of the manufacturer catalog.
    /// </summary>
    [Fact]
    public async Task Ets4Sample_AllGroupAddressesGetADatapointType()
    {
        await using var stream = File.OpenRead(TestSamples.Path("test_project-ets4-no_password.knxproj"));

        var result = await _fixture.Parser.ParseAsync(stream, new ParserOptions());

        result.GroupAddresses.Should().HaveCount(3);
        result.GroupAddresses.Should().OnlyContain(ga => ga.DatapointType != null);

        // 0/0/1 links a com object declared DPST-1-1 and one that only states "1 Bit" — the stated
        // subtype is the only one on the table, so it stands.
        Dpt(result, "0/0/1").Should().Be("DPT 1.001");

        // 0/0/2 and 0/0/3 have no declared type anywhere; "1 Bit" pins the main type and nothing
        // justifies a subtype.
        Dpt(result, "0/0/2").Should().Be("DPT 1");
        Dpt(result, "0/0/3").Should().Be("DPT 1");
    }

    /// <summary>
    /// ETS 5 mixes both: some group addresses carry a type, others do not. The ones that do must come
    /// through untouched.
    /// </summary>
    [Fact]
    public async Task Ets5Sample_FillsTheGapsAndKeepsTheRest()
    {
        await using var stream = File.OpenRead(TestSamples.Path("xknx_test_project_no_password.knxproj"));

        var result = await _fixture.Parser.ParseAsync(stream, new ParserOptions());

        // Declared in the project file as DPST-1-1 / DPST-1-8 — unchanged.
        Dpt(result, "2/0/6").Should().Be("DPT 1.001");
        Dpt(result, "1/0/3").Should().Be("DPT 1.008");

        // No DatapointType on the GroupAddress element; resolved through the catalog.
        Dpt(result, "1/0/5").Should().Be("DPT 1");
        Dpt(result, "1/0/4").Should().Be("DPT 1");
    }

    /// <summary>
    /// The full xknx sample, checked against the expectations xknxproject produces for the very same
    /// file (docs/samples/xknxproject/stubs/xknx_test_project.json). Covers the module-object path
    /// and the "DPT-9 DPST-9-1 … DPST-9-31" list of accepted subtypes.
    /// </summary>
    [Theory]
    [InlineData("1/0/0", "DPT 1.008")]
    [InlineData("1/0/1", "DPT 1")]
    [InlineData("1/0/4", "DPT 1")]
    [InlineData("2/0/0", "DPT 1.002")]
    [InlineData("2/0/1", "DPT 9.001")]
    [InlineData("2/1/10", "DPT 1")]
    [InlineData("2/1/21", "DPT 9")]
    [InlineData("2/1/22", "DPT 9")]
    [InlineData("2/1/23", "DPT 9")]
    [InlineData("7/1/1", "DPT 5.001")]
    public async Task XknxSample_MatchesTheXknxprojectReference(string address, string expected)
    {
        await using var stream = File.OpenRead(TestSamples.Path("xknx_test_project.knxproj"));

        var result = await _fixture.Parser.ParseAsync(stream, new ParserOptions { Password = "test" });

        Dpt(result, address).Should().Be(expected);
    }

    /// <summary>A group address linked to no communication object keeps no type — nothing to infer from.</summary>
    [Fact]
    public async Task XknxSample_UnlinkedGroupAddressStaysWithoutType()
    {
        await using var stream = File.OpenRead(TestSamples.Path("xknx_test_project.knxproj"));

        var result = await _fixture.Parser.ParseAsync(stream, new ParserOptions { Password = "test" });

        Dpt(result, "2/1/1").Should().BeNull();
    }

    /// <summary>
    /// ETS 6 with module definitions: the instance RefId carries the module INSTANCE
    /// (<c>MD-2_M-1_MI-1_O-2-1_R-1</c>) while the catalog only knows the module DEFINITION
    /// (<c>MD-2_O-2-1_R-1</c>). Without stripping those segments no module object resolves and the
    /// flags stay empty, which is the defect COMOBJECT_FLAGS_PLAN.md describes.
    /// </summary>
    [Fact]
    public async Task Ets6ModuleSample_ResolvesFlagsAndKeepsDeclaredTypes()
    {
        await using var stream = File.OpenRead(TestSamples.Path("module-definition-test.knxproj"));

        var result = await _fixture.Parser.ParseAsync(stream, new ParserOptions());

        result.CommunicationObjects.Should().NotBeEmpty();
        result.CommunicationObjects.Count(c => !string.IsNullOrEmpty(c.Flags))
            .Should().BeGreaterThan(0, "ETS 6 flags live in the manufacturer catalog, not in the project");

        // Every group address in this sample declares its own type; the cascade must not touch them.
        Dpt(result, "0/0/1").Should().Be("DPT 9.001");
        Dpt(result, "0/1/1").Should().Be("DPT 9.001");
    }

    /// <summary>
    /// Regression guard on the large real-world ETS 5 project: 754 of its 841 group addresses carry
    /// a datapoint type in the project file. The cascade must leave every one of them untouched —
    /// filling gaps may never rewrite what ETS already stated.
    /// </summary>
    [SkippableFact]
    public async Task LargeEts5Project_KeepsEveryDeclaredDatapointType()
    {
        const string sample = "myProject_ets_v5.7.7.knxproj";
        Skip.IfNot(TestSamples.Exists(sample), $"{sample} not available");

        await using var stream = File.OpenRead(TestSamples.Path(sample));

        var result = await _fixture.Parser.ParseAsync(stream, new ParserOptions());

        result.GroupAddresses.Should().HaveCount(841);
        result.GroupAddresses.Count(ga => ga.DatapointType != null).Should().Be(754);

        // Every communication object resolves against the catalog, so the flags are no longer empty
        // for ETS 5 (COMOBJECT_FLAGS_PLAN.md measured 0 of 1528 on exactly this kind of project).
        result.CommunicationObjects.Should().NotBeEmpty();
        result.CommunicationObjects.Should().OnlyContain(c => !string.IsNullOrEmpty(c.Flags));
    }
}
