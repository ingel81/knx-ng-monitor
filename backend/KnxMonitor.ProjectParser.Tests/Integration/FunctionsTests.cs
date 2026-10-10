using FluentAssertions;
using KnxMonitor.ProjectParser.Core.Models;
using KnxMonitor.ProjectParser.Tests.Fixtures;
using Xunit;

namespace KnxMonitor.ProjectParser.Tests.Integration;

/// <summary>
/// ETS functions (#12): &lt;Function&gt; elements inside a &lt;Space&gt;, linking group addresses via
/// &lt;GroupAddressRef RefId&gt;. Only ETS 5/6 know them; ETS 4 projects must come back empty.
/// </summary>
public class FunctionsTests : IClassFixture<SampleFileFixture>
{
    private readonly SampleFileFixture _fixture;

    public FunctionsTests(SampleFileFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<ParseResult> ParseAsync(string fileName)
    {
        await using var stream = File.OpenRead(TestSamples.Path(fileName));
        return await _fixture.Parser.ParseAsync(stream, new ParserOptions());
    }

    [Fact]
    public async Task Ets6_Function_IsParsed_WithSpaceAndResolvedGroupAddresses()
    {
        var result = await ParseAsync("testprojekt-ets6-functions.knxproj");

        // Same content as xknxproject's stub for this file.
        var function = result.Functions.Should().ContainSingle().Subject;
        function.Id.Should().Be("P-05C0-0_F-1");
        function.Name.Should().Be("LivingroomLight");
        function.Type.Should().Be("FT-1");
        function.SpaceId.Should().Be("P-05C0-0_BP-2");
        function.GroupAddresses.Should().Equal("0/0/1", "0/0/2");

        result.Statistics.FunctionCount.Should().Be(1);
    }

    [Fact]
    public async Task Ets6_FunctionSpace_IsAParsedLocation()
    {
        var result = await ParseAsync("testprojekt-ets6-functions.knxproj");

        var spaceIds = result.Locations.Select(l => l.Id).ToHashSet();
        result.Functions.Should().NotBeEmpty();
        foreach (var function in result.Functions)
            spaceIds.Should().Contain(function.SpaceId!);
    }

    [Theory]
    [InlineData("test_project-ets4-no_password.knxproj")]
    [InlineData("xknx_test_project_no_password.knxproj")]
    [InlineData("ets6_free.knxproj")]
    public async Task Projects_WithoutFunctions_ReturnAnEmptyList(string fileName)
    {
        var result = await ParseAsync(fileName);

        result.Functions.Should().BeEmpty();
        result.Statistics.FunctionCount.Should().Be(0);
    }

    [SkippableTheory]
    [InlineData("test_ets_v6.4.1.knxproj")]
    [InlineData("myProject_ets_v5.7.7.knxproj")]
    public async Task OwnSamples_FunctionGroupAddresses_AllExistInTheProject(string fileName)
    {
        Skip.IfNot(TestSamples.Exists(fileName), $"missing fixture: {fileName}");

        var result = await ParseAsync(fileName);

        var addresses = result.GroupAddresses.Select(g => g.Address).ToHashSet();
        // Neither sample defines functions today; the check guards a future export that does.
        foreach (var function in result.Functions)
            function.GroupAddresses.Should().OnlyContain(a => addresses.Contains(a));
    }
}
