using FluentAssertions;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Infrastructure.Data;
using KnxMonitor.Infrastructure.Services;
using ProjectFunction = KnxMonitor.ProjectParser.Core.Models.ProjectFunction;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KnxMonitor.Infrastructure.Tests;

/// <summary>
/// Building part of the sender and ETS functions of the destination (#12): the compact path,
/// the GA-to-function mapping and that a re-import refreshes the functions.
/// </summary>
public class BuildingPartAndFunctionTests
{
    private static Location Loc(string id, string name, string? parent, string? devices = null) => new()
    {
        ExternalId = id,
        Name = name,
        Type = "BuildingPart",
        ParentExternalId = parent,
        DeviceAddresses = devices
    };

    /// <summary>The tree from the issue: two single-child levels on top, siblings below.</summary>
    private static List<Location> IssueTree() => new()
    {
        Loc("sys", "00_SYS", null),
        Loc("cnt", "00/1_CNT", "sys"),
        Loc("wr", "00/2_WR", "cnt"),
        Loc("ab", "00/6_AB", "cnt"),
        Loc("eg", "00/3_EG", "wr"),
        Loc("og", "00/4_OG", "wr"),
        Loc("ug", "00/5_UG", "wr"),
        Loc("egd", "01_EGD", "eg", devices: "1.0.81"),
        Loc("ogd", "01_OGD", "og", devices: "1.0.90,1.0.91"),
    };

    [Fact]
    public void Compact_path_keeps_the_parent_and_every_level_with_siblings()
    {
        var parts = BuildingPartResolver.ForDevices(IssueTree());

        // 01_EGD is the only child of 00/3_EG, but as direct parent it always counts.
        parts["1.0.81"].Compact.Should().Be("00/2_WR › 00/3_EG › 01_EGD");
        parts["1.0.81"].Full.Should().Be("00_SYS › 00/1_CNT › 00/2_WR › 00/3_EG › 01_EGD");
        parts["1.0.90"].Compact.Should().Be("00/2_WR › 00/4_OG › 01_OGD");
        parts["1.0.91"].Should().Be(parts["1.0.90"]);
    }

    [Fact]
    public void Device_in_a_root_space_gets_just_that_space()
    {
        var parts = BuildingPartResolver.ForDevices(new[] { Loc("house", "Haus", null, devices: "1.1.1") });

        parts["1.1.1"].Should().Be(new BuildingPart("Haus", "Haus"));
    }

    [Fact]
    public void Sibling_roots_count_as_a_level_with_siblings()
    {
        var parts = BuildingPartResolver.ForDevices(new[]
        {
            Loc("a", "Haus A", null),
            Loc("b", "Haus B", null),
            Loc("a1", "Keller", "a", devices: "1.1.1"),
        });

        parts["1.1.1"].Compact.Should().Be("Haus A › Keller");
    }

    [Fact]
    public void Device_listed_twice_keeps_the_deeper_location_and_unknown_parents_are_roots()
    {
        var parts = BuildingPartResolver.ForDevices(new[]
        {
            Loc("house", "Haus", null, devices: "1.1.1"),
            Loc("room", "Küche", "house", devices: "1.1.1"),
            Loc("orphan", "Verteiler", "gone", devices: "1.1.2"),
        });

        parts["1.1.1"].Full.Should().Be("Haus › Küche");
        parts["1.1.2"].Full.Should().Be("Verteiler");
        parts.Should().NotContainKey("9.9.9");
    }

    [Fact]
    public void Functions_attach_to_their_group_addresses_without_duplicates()
    {
        var gas = new List<GroupAddress>
        {
            new() { Address = "0/0/1" },
            new() { Address = "0/0/2" },
            new() { Address = "0/0/3" },
        };
        var functions = new[]
        {
            new ProjectFunction { Name = "Licht Wohnen", GroupAddresses = { "0/0/1", "0/0/2" } },
            new ProjectFunction { Name = "Zentral, aus", GroupAddresses = { "0/0/1" } },
            new ProjectFunction { Name = "Licht Wohnen", GroupAddresses = { "0/0/1" } },
            new ProjectFunction { Name = "  ", GroupAddresses = { "0/0/3" } },
        };

        KnxProjectParserService.AttachFunctions(gas, functions);

        gas[0].Functions.Should().Be("Licht Wohnen\nZentral, aus");
        gas[1].Functions.Should().Be("Licht Wohnen");
        gas[2].Functions.Should().BeNull("a function without a name has nothing to show");
    }

    [Fact]
    public async Task Reimport_merge_refreshes_the_functions_and_keeps_the_group_address_row()
    {
        using var db = new SqliteTestDb();

        int projectId, gaId;
        using (var scope = db.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var project = new Project
            {
                Name = "p", FileName = "p.knxproj", ImportDate = DateTime.UtcNow,
                GroupAddresses = { new GroupAddress { Address = "1/1/1", Name = "Licht", Functions = "Alt", CreatedAt = DateTime.UtcNow } }
            };
            ctx.Projects.Add(project);
            await ctx.SaveChangesAsync();
            projectId = project.Id;
            gaId = project.GroupAddresses.Single().Id;
        }

        using (var scope = db.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
            await repo.MergeReimportAsync(projectId, "p.knxproj",
                new[] { new GroupAddress { Address = "1/1/1", Name = "Licht", Functions = "Neu\nZentral", CreatedAt = DateTime.UtcNow } },
                Array.Empty<Device>(), Array.Empty<Location>(), Array.Empty<CommunicationObject>(), Array.Empty<GroupRange>());
        }

        using (var scope = db.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ga = await ctx.GroupAddresses.SingleAsync();
            ga.Id.Should().Be(gaId);
            ga.Functions.Should().Be("Neu\nZentral");
        }
    }
}
