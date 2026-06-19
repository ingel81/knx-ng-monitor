using FluentAssertions;
using KnxMonitor.ProjectParser.Core.Models;
using KnxMonitor.ProjectParser.Tests.Fixtures;

namespace KnxMonitor.ProjectParser.Tests.Integration;

/// <summary>
/// Verifies Location (building hierarchy) and ComObject extraction against the public no-password
/// fixtures. ETS4 uses &lt;Buildings&gt;/&lt;BuildingPart&gt; + &lt;Connectors&gt;; ETS5/6 use
/// &lt;Locations&gt;/&lt;Space&gt; + the @Links attribute. All three are covered here.
/// </summary>
public class LocationsAndComObjectsTests : IClassFixture<SampleFileFixture>
{
    private readonly SampleFileFixture _fixture;

    public LocationsAndComObjectsTests(SampleFileFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<ParseResult> ParseAsync(string fileName, string? password = null)
    {
        await using var stream = File.OpenRead(TestSamples.Path(fileName));
        return await _fixture.Parser.ParseAsync(stream, new ParserOptions { Password = password });
    }

    [Fact]
    public async Task Ets4_Buildings_AndConnectorComObjects_AreParsed()
    {
        var result = await ParseAsync("test_project-ets4-no_password.knxproj");

        // Buildings: House > {First floor > Kitchen, Second floor > Bathroom} = 5 nodes.
        result.Locations.Should().HaveCount(5);
        result.Locations.Should().Contain(l => l.Type == "Building" && l.ParentId == null);
        result.Locations.Should().Contain(l => l.Type == "Room");

        // A room references a device by RefId -> resolved to a physical address.
        var room = result.Locations.First(l => l.Name == "Kitchen");
        room.DeviceRefIds.Should().NotBeEmpty();
        room.DeviceRefIds.Should().AllSatisfy(a => a.Should().MatchRegex(@"^\d+\.\d+\.\d+$"));

        // Connector-style com objects (Send/Receive) resolve to m/m/s GA links.
        result.CommunicationObjects.Should().NotBeEmpty();
        result.CommunicationObjects.Should().AllSatisfy(c =>
        {
            c.DeviceAddress.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
            c.GroupAddressLinks.Should().NotBeEmpty();
            c.GroupAddressLinks.Should().AllSatisfy(g => g.Should().MatchRegex(@"^\d+/\d+/\d+$"));
        });
        result.CommunicationObjects.Should().Contain(c => c.Flags == "Send,Receive");
    }

    [Fact]
    public async Task Ets5_Locations_AndLinkComObjects_AreParsed()
    {
        var result = await ParseAsync("xknx_test_project_no_password.knxproj");

        // ETS5 Space-based locations.
        result.Locations.Should().NotBeEmpty();
        result.Locations.Should().Contain(l => l.Type == "Building");

        // @Links-style com objects: 7 links on the single device.
        result.CommunicationObjects.Should().HaveCount(7);
        result.CommunicationObjects.Should().AllSatisfy(c => c.GroupAddressLinks.Should().NotBeEmpty());

        // DatapointType carried through where the @Links ref provided one.
        result.CommunicationObjects.Should().Contain(c => c.DatapointType != null);
    }

    [Fact]
    public async Task Ets6_NestedSpaces_AndModuleComObjects_AreParsed()
    {
        // module-definition-test uses nested Spaces (Building>Floor>Room) and @Links with module ids.
        var result = await ParseAsync("module-definition-test.knxproj");

        result.Locations.Should().HaveCount(3);
        result.Locations.Should().Contain(l => l.Type == "Floor" && l.ParentId != null);
        result.Locations.Should().Contain(l => l.Type == "Room" && l.ParentId != null);

        result.CommunicationObjects.Should().NotBeEmpty();
        result.CommunicationObjects.Should().AllSatisfy(c => c.GroupAddressLinks.Should().NotBeEmpty());
    }

    [Fact]
    public async Task LocationParentIds_FormAConsistentTree()
    {
        var result = await ParseAsync("test_project-ets4-no_password.knxproj");

        var ids = result.Locations.Select(l => l.Id).ToHashSet();
        foreach (var loc in result.Locations.Where(l => l.ParentId != null))
        {
            ids.Should().Contain(loc.ParentId!, "every non-root location's parent must also be present");
        }
        result.Locations.Count(l => l.ParentId == null).Should().Be(1, "single building root");
    }

    [Fact]
    public async Task Ets6_GroupRanges_AreParsed_WithNamesAndBounds()
    {
        // module-definition-test has nested main/middle <GroupRange> nodes (e.g. "Heating" 1..2047
        // containing "Wohnzimmer" 1..255, "Kitchen" 256..511, ...). All are flattened into the list.
        var result = await ParseAsync("module-definition-test.knxproj");

        result.GroupRanges.Should().NotBeEmpty();

        // A known main range and one of its nested middle ranges.
        result.GroupRanges.Should().ContainSingle(r =>
            r.Name == "Heating" && r.RangeStart == 1 && r.RangeEnd == 2047);
        result.GroupRanges.Should().ContainSingle(r =>
            r.Name == "Kitchen" && r.RangeStart == 256 && r.RangeEnd == 511);

        result.GroupRanges.Should().AllSatisfy(r => r.RangeEnd.Should().BeGreaterThanOrEqualTo(r.RangeStart));
    }

    [Fact]
    public async Task Ets6_ModuleComObjectNumbers_UseObjectNotChannel()
    {
        // Module ids look like "...O-2-19_R-6": the object number is 19 (not the channel 2). The OLD
        // heuristic took the digits right after "O-" and would have returned the channel 2 for every
        // module object; the fix takes the last segment. Linked module objects in the fixture include
        // O-2-19 and O-2-20, so the extracted numbers must include 19 and 20.
        var result = await ParseAsync("module-definition-test.knxproj");

        var numbers = result.CommunicationObjects.Select(c => c.Number).ToHashSet();
        numbers.Should().Contain(19);
        numbers.Should().Contain(20);

        // Sanity: not every module com object collapsed onto the channel index 2.
        result.CommunicationObjects.Should().Contain(c => c.Number != 2);
    }

    [Fact]
    public async Task Statistics_IncludeLocationAndComObjectCounts()
    {
        var result = await ParseAsync("test_project-ets4-no_password.knxproj");

        result.Statistics.LocationCount.Should().Be(result.Locations.Count);
        result.Statistics.CommunicationObjectCount.Should().Be(result.CommunicationObjects.Count);
    }
}
