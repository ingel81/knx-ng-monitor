using FluentAssertions;
using KnxMonitor.ProjectParser.Core.Models;
using KnxMonitor.ProjectParser.Tests.Fixtures;

namespace KnxMonitor.ProjectParser.Tests.Integration;

/// <summary>
/// Communication flags come from the two levels that carry them: the manufacturer catalog holds the
/// factory setting, and the project file carries only what the integrator changed in ETS. ETS 4
/// additionally has <c>&lt;Send&gt;</c> / <c>&lt;Receive&gt;</c> connectors, which are NOT flags —
/// they name the object's primary group address and the ones it also listens to. Reporting those as
/// flags showed every ETS 4 object as "Transmit", including receive-only ones.
/// </summary>
public class ComObjectFlagTests : IClassFixture<SampleFileFixture>
{
    private readonly SampleFileFixture _fixture;

    public ComObjectFlagTests(SampleFileFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<ParseResult> ParseAsync(string sample, string? password = null)
    {
        await using var stream = File.OpenRead(TestSamples.Path(sample));
        return await _fixture.Parser.ParseAsync(stream, new ParserOptions { Password = password });
    }

    /// <summary>
    /// The ETS 4 sample, checked against what its application programs actually declare:
    /// O-0 "Channel A / Switch On/Off" is Communication+Write, O-5 and O-13 "State" are
    /// Communication+Read+Transmit. Every one of them is wired with a &lt;Send&gt; connector, so the
    /// old path reported all five as "Send" alone.
    /// </summary>
    [Fact]
    public async Task Ets4_FlagsComeFromTheManufacturerCatalog_NotFromTheConnectors()
    {
        var result = await ParseAsync("test_project-ets4-no_password.knxproj");

        var switching = result.CommunicationObjects.Single(c => c.DeviceAddress == "0.0.1" && c.Number == 0);
        switching.Flags.Should().Be("Write,Communication");

        var state = result.CommunicationObjects.Single(c => c.DeviceAddress == "0.0.1" && c.Number == 5);
        state.Flags.Should().Be("Read,Communication,Transmit");

        // The blind actuator output is written to and never transmits, although its connector says Send.
        var blind = result.CommunicationObjects.Single(c => c.DeviceAddress == "0.0.2" && c.Number == 10);
        blind.Flags.Should().Be("Write,Communication");

        result.CommunicationObjects.Should().NotContain(
            c => c.Flags != null && (c.Flags.Contains("Send") || c.Flags.Contains("Receive")),
            "the connector wording is a fallback, not a flag");
    }

    /// <summary>
    /// The connectors are still the only thing left when a project ships no manufacturer data for an
    /// object — then they are better than nothing and must keep coming through.
    /// </summary>
    [Fact]
    public async Task Ets4_WithoutManufacturerData_FallsBackToTheConnectors()
    {
        await using var project = BuildConnectorOnlyProject();

        var result = await _fixture.Parser.ParseAsync(project, new ParserOptions());

        result.CommunicationObjects.Should().ContainSingle();
        result.CommunicationObjects[0].Flags.Should().Be("Send,Receive");
    }

    /// <summary>
    /// ETS writes a flag onto the instance ref ONLY where the integrator changed it, so that change
    /// has to win over the factory setting while every untouched flag keeps the catalog value.
    /// The synthetic project links two instance refs to the same catalog entry, one of them with
    /// Read and Transmit switched on.
    /// </summary>
    [Fact]
    public async Task FlagChangedInEts_OverridesTheFactorySetting_PerFlag()
    {
        await using var project = SyntheticCascadeProject.Build();

        var result = await _fixture.Parser.ParseAsync(project, new ParserOptions());

        // Catalog default of ComObject O-1: Write + Communication enabled, everything else disabled.
        var untouched = result.CommunicationObjects.Single(c => c.GroupAddressLinks.Contains("0/0/1"));
        untouched.Flags.Should().Be("Write,Communication");

        // Same catalog entry, but this instance sets ReadFlag and TransmitFlag. Write and
        // Communication must survive from the catalog.
        var changed = result.CommunicationObjects.Single(c => c.GroupAddressLinks.Contains("0/0/8"));
        changed.Flags.Should().Be("Read,Write,Communication,Transmit");
    }

    /// <summary>
    /// An ETS 4 project with Send/Receive connectors but no M-XXXX manufacturer folder at all, so
    /// nothing can resolve to a catalog entry.
    /// </summary>
    private static MemoryStream BuildConnectorOnlyProject()
    {
        const string ns = "http://knx.org/xml/project/11";

        return TestZipBuilder.BuildOuter(new[]
        {
            ("P-0002.signature", string.Empty),
            ("knx_master.xml", $"""<KNX xmlns="{ns}"><MasterData /></KNX>"""),
            ("P-0002/0.xml", $"""
                <KNX xmlns="{ns}" CreatedBy="ETS4" ToolVersion="ETS 4.2.0 (Build 3884)">
                  <Project Id="P-0002">
                    <Installations>
                      <Installation InstallationId="0" GroupAddressStyle="ThreeLevel">
                        <Topology>
                          <Area Id="P-0002-0_A-0" Address="0">
                            <Line Id="P-0002-0_L-0" Address="0">
                              <DeviceInstance Id="P-0002-0_DI-1" Address="1" Name="Aktor ohne Katalog">
                                <ComObjectInstanceRefs>
                                  <ComObjectInstanceRef RefId="M-9999_A-0000-00-0000_O-1_R-1" IsActive="1">
                                    <Connectors>
                                      <Send GroupAddressRefId="P-0002-0_GA-1" />
                                      <Receive GroupAddressRefId="P-0002-0_GA-2" />
                                    </Connectors>
                                  </ComObjectInstanceRef>
                                </ComObjectInstanceRefs>
                              </DeviceInstance>
                            </Line>
                          </Area>
                        </Topology>
                        <GroupAddresses>
                          <GroupRanges>
                            <GroupRange Id="P-0002-0_GR-1" Name="Haupt" RangeStart="1" RangeEnd="2047">
                              <GroupAddress Id="P-0002-0_GA-1" Address="1" Name="Schalten" />
                              <GroupAddress Id="P-0002-0_GA-2" Address="2" Name="Status" />
                            </GroupRange>
                          </GroupRanges>
                        </GroupAddresses>
                      </Installation>
                    </Installations>
                  </Project>
                </KNX>
                """),
        });
    }
}
