using System.Text;
using FluentAssertions;
using KnxMonitor.ProjectParser.Core.Models;
using KnxMonitor.ProjectParser.Services;

namespace KnxMonitor.ProjectParser.Tests.Unit.Services;

public class ApplicationProgramCatalogTests
{
    /// <summary>
    /// ETS 4 writes the instance RefId out in full, so the application program is readable straight
    /// off it when Hardware.xml gives nothing.
    /// </summary>
    [Theory]
    [InlineData("M-0083_A-0013-11-A9D6_O-0_R-10000", "M-0083_A-0013-11-A9D6")]
    [InlineData("M-0008_A-1144-13-EB4F-O000A_O-1_R-1", "M-0008_A-1144-13-EB4F-O000A")]
    [InlineData("M-0083_A-0153-10-297A-O00EF_MD-2_O-2-4_R-1", "M-0083_A-0153-10-297A-O00EF")]
    public void DeriveProgramIdFromRefId_LongForm_ReturnsProgramId(string refId, string expected)
    {
        ApplicationProgramCatalog.DeriveProgramIdFromRefId(refId).Should().Be(expected);
    }

    /// <summary>ETS 5/6 use the short form, which says nothing about the program.</summary>
    [Theory]
    [InlineData("O-40_R-1433")]
    [InlineData("MD-2_M-1_MI-1_O-2-1_R-1")]
    [InlineData("")]
    [InlineData("M-0083")]
    public void DeriveProgramIdFromRefId_ShortOrUnusable_ReturnsNull(string refId)
    {
        ApplicationProgramCatalog.DeriveProgramIdFromRefId(refId).Should().BeNull();
    }

    /// <summary>
    /// The project names the module INSTANCE (M-&lt;n&gt; / MI-&lt;n&gt;), the catalog only knows the
    /// module DEFINITION. "M-1" may only be dropped inside a module — as the first segment it is the
    /// manufacturer.
    /// </summary>
    [Theory]
    [InlineData("MD-2_M-1_MI-1_O-2-1_R-1", "MD-2_O-2-1_R-1")]
    [InlineData("MD-2_M-2_MI-1_O-2-35_R-65", "MD-2_O-2-35_R-65")]
    [InlineData("MD-1_M-1_MI-1_MD-3_M-2_MI-4_O-1-0_R-9", "MD-1_MD-3_O-1-0_R-9")]
    [InlineData("O-334_R-21", "O-334_R-21")]
    [InlineData("M-0083_A-0013-11-A9D6_O-0_R-10000", "M-0083_A-0013-11-A9D6_O-0_R-10000")]
    public void StripModuleInstanceSegments_DropsOnlyModuleInstances(string refId, string expected)
    {
        ApplicationProgramCatalog.StripModuleInstanceSegments(refId).Should().Be(expected);
    }

    [Theory]
    // Short ETS 5/6 form gets the program id in front.
    [InlineData("M-0002_A-A066-14-550B", "O-40_R-1433", "M-0002_A-A066-14-550B_O-40_R-1433")]
    // Long ETS 4 form is already qualified and must not be doubled.
    [InlineData("M-0083_A-0013-11-A9D6", "M-0083_A-0013-11-A9D6_O-0_R-10000", "M-0083_A-0013-11-A9D6_O-0_R-10000")]
    // Module instance is stripped, then qualified.
    [InlineData("M-0083_A-0153-10-297A-O00EF", "MD-2_M-1_MI-1_O-2-1_R-1", "M-0083_A-0153-10-297A-O00EF_MD-2_O-2-1_R-1")]
    public void BuildCatalogId_QualifiesAndNormalises(string programId, string refId, string expected)
    {
        ApplicationProgramCatalog.BuildCatalogId(programId, refId).Should().Be(expected);
    }

    [Fact]
    public void Empty_FindsNothing()
    {
        ApplicationProgramCatalog.Empty.Find("P-0001-0_DI-1", "O-1_R-1").Should().BeNull();
        ApplicationProgramCatalog.Empty.Find(null, null).Should().BeNull();
        ApplicationProgramCatalog.Empty.EntryCount.Should().Be(0);
    }
}

public class HardwareProgramMapTests
{
    private static ProjectFileMap Map(params (string Path, string Content)[] files) =>
        new(files.ToDictionary(f => f.Path, f => Encoding.UTF8.GetBytes(f.Content)));

    [Fact]
    public async Task LoadAsync_ReadsEveryHardwareFile()
    {
        using var files = Map(
            ("M-0002/Hardware.xml", """
                <KNX xmlns="http://knx.org/xml/project/20"><ManufacturerData><Manufacturer RefId="M-0002">
                  <Hardware><Hardware Id="M-0002_H-1-1"><Hardware2Programs>
                    <Hardware2Program Id="M-0002_H-1-1_HP-A066-14-550B">
                      <ApplicationProgramRef RefId="M-0002_A-A066-14-550B" />
                    </Hardware2Program>
                  </Hardware2Programs></Hardware></Hardware>
                </Manufacturer></ManufacturerData></KNX>
                """),
            ("M-0008/Hardware.xml", """
                <KNX xmlns="http://knx.org/xml/project/23"><ManufacturerData><Manufacturer RefId="M-0008">
                  <Hardware><Hardware Id="M-0008_H-8-0-O000A"><Hardware2Programs>
                    <Hardware2Program Id="M-0008_H-8-0-O000A_HP-1144-13-EB4F-O000A">
                      <ApplicationProgramRef RefId="M-0008_A-1144-13-EB4F-O000A" />
                    </Hardware2Program>
                  </Hardware2Programs></Hardware></Hardware>
                </Manufacturer></ManufacturerData></KNX>
                """));

        var map = await HardwareProgramMap.LoadAsync(files, CancellationToken.None);

        map.Should().HaveCount(2);
        map["M-0002_H-1-1_HP-A066-14-550B"].Should().Be("M-0002_A-A066-14-550B");
        map["M-0008_H-8-0-O000A_HP-1144-13-EB4F-O000A"].Should().Be("M-0008_A-1144-13-EB4F-O000A");
    }

    /// <summary>
    /// An ApplicationProgramRef outside a Hardware2Program (ETS writes them under Products too) must
    /// not be attributed to the previous Hardware2Program.
    /// </summary>
    [Fact]
    public async Task LoadAsync_IgnoresRefsOutsideAHardware2Program()
    {
        using var files = Map(("M-0001/Hardware.xml", """
            <KNX xmlns="http://knx.org/xml/project/20"><ManufacturerData><Manufacturer RefId="M-0001">
              <Hardware><Hardware Id="M-0001_H-1-1">
                <Hardware2Programs>
                  <Hardware2Program Id="M-0001_H-1-1_HP-1">
                    <ApplicationProgramRef RefId="M-0001_A-1" />
                  </Hardware2Program>
                </Hardware2Programs>
                <ApplicationProgramRef RefId="M-0001_A-STRAY" />
              </Hardware></Hardware>
            </Manufacturer></ManufacturerData></KNX>
            """));

        var map = await HardwareProgramMap.LoadAsync(files, CancellationToken.None);

        map.Should().ContainSingle();
        map["M-0001_H-1-1_HP-1"].Should().Be("M-0001_A-1");
    }

    [Fact]
    public async Task LoadAsync_NoHardwareFiles_ReturnsEmptyMap()
    {
        using var files = Map(("P-0001/0.xml", "<KNX/>"));

        (await HardwareProgramMap.LoadAsync(files, CancellationToken.None)).Should().BeEmpty();
    }

    /// <summary>
    /// One damaged manufacturer folder may not cost the others their application programs — and with
    /// them every flag and every inferred datapoint type in the project.
    /// </summary>
    [Fact]
    public async Task LoadAsync_OneBrokenFile_KeepsTheOthers()
    {
        using var files = Map(
            ("M-0001/Hardware.xml", "<KNX><ManufacturerData><Hardware2Program Id=\"broken\""),
            ("M-0002/Hardware.xml", """
                <KNX xmlns="http://knx.org/xml/project/20"><ManufacturerData><Manufacturer RefId="M-0002">
                  <Hardware><Hardware Id="M-0002_H-1-1"><Hardware2Programs>
                    <Hardware2Program Id="M-0002_H-1-1_HP-1">
                      <ApplicationProgramRef RefId="M-0002_A-1" />
                    </Hardware2Program>
                  </Hardware2Programs></Hardware></Hardware>
                </Manufacturer></ManufacturerData></KNX>
                """));

        var map = await HardwareProgramMap.LoadAsync(files, CancellationToken.None);

        map.Should().ContainSingle();
        map["M-0002_H-1-1_HP-1"].Should().Be("M-0002_A-1");
    }
}

public class ApplicationProgramReaderTests
{
    private const string Program = "M-0001_A-1000-11-AAAA";

    private static Stream Xml(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    private const string Sample = $"""
        <KNX xmlns="http://knx.org/xml/project/20"><ManufacturerData><Manufacturer RefId="M-0001">
          <ApplicationPrograms><ApplicationProgram Id="{Program}"><Static>
            <ComObjectTable>
              <ComObject Id="{Program}_O-1" Number="1" ObjectSize="1 Bit" DatapointType="DPST-1-1"
                         ReadFlag="Disabled" WriteFlag="Enabled" CommunicationFlag="Enabled"
                         TransmitFlag="Disabled" UpdateFlag="Disabled" ReadOnInitFlag="Disabled" />
              <ComObject Id="{Program}_O-2" Number="2" ObjectSize="4 Bytes" />
            </ComObjectTable>
            <ComObjectRefs>
              <ComObjectRef Id="{Program}_O-1_R-1" RefId="{Program}_O-1" />
              <ComObjectRef Id="{Program}_O-1_R-2" RefId="{Program}_O-1"
                            DatapointType="DPST-3-7" ObjectSize="4 Bit" ReadFlag="Enabled" />
              <ComObjectRef Id="{Program}_O-2_R-3" RefId="{Program}_O-2" />
              <ComObjectRef Id="{Program}_O-9_R-9" RefId="{Program}_O-9" DatapointType="DPST-5-1" />
            </ComObjectRefs>
          </Static></ApplicationProgram></ApplicationPrograms>
        </Manufacturer></ManufacturerData></KNX>
        """;

    private static Task<Dictionary<string, ComObjectCatalogEntry>> Read(params string[] wanted) =>
        ApplicationProgramReader.ReadAsync(
            Xml(Sample), wanted.ToHashSet(StringComparer.OrdinalIgnoreCase), CancellationToken.None);

    [Fact]
    public async Task ReadAsync_RefWithoutOwnValues_InheritsTheComObject()
    {
        var result = await Read($"{Program}_O-1_R-1");

        var entry = result[$"{Program}_O-1_R-1"];
        entry.DatapointType.Should().Be("DPST-1-1");
        entry.ObjectSize.Should().Be("1 Bit");
        entry.Flags.Should().Equal("Disabled", "Enabled", "Enabled", "Disabled", "Disabled", "Disabled");
    }

    /// <summary>The ref is the more specific level and wins per attribute, not per element.</summary>
    [Fact]
    public async Task ReadAsync_RefOverridesSingleAttributes()
    {
        var result = await Read($"{Program}_O-1_R-2");

        var entry = result[$"{Program}_O-1_R-2"];
        entry.DatapointType.Should().Be("DPST-3-7");
        entry.ObjectSize.Should().Be("4 Bit");

        // ReadFlag comes from the ref, everything else still from the ComObject.
        entry.Flags[0].Should().Be("Enabled");
        entry.Flags[1].Should().Be("Enabled");
        entry.Flags[3].Should().Be("Disabled");
    }

    /// <summary>
    /// Two refs onto the same ComObject with different types — the case none of the shipped samples
    /// has and the reason the cascade may not shortcut to the ComObject.
    /// </summary>
    [Fact]
    public async Task ReadAsync_SameComObjectTwoRefs_KeepsThemApart()
    {
        var result = await Read($"{Program}_O-1_R-1", $"{Program}_O-1_R-2");

        result[$"{Program}_O-1_R-1"].DatapointType.Should().Be("DPST-1-1");
        result[$"{Program}_O-1_R-2"].DatapointType.Should().Be("DPST-3-7");
    }

    [Fact]
    public async Task ReadAsync_ComObjectWithoutType_LeavesOnlyTheObjectSize()
    {
        var entry = (await Read($"{Program}_O-2_R-3"))[$"{Program}_O-2_R-3"];

        entry.DatapointType.Should().BeNull();
        entry.ObjectSize.Should().Be("4 Bytes");
    }

    /// <summary>A ref pointing at a ComObject that is not in the file still yields its own values.</summary>
    [Fact]
    public async Task ReadAsync_DanglingComObjectRef_KeepsTheRefsOwnValues()
    {
        var entry = (await Read($"{Program}_O-9_R-9"))[$"{Program}_O-9_R-9"];

        entry.DatapointType.Should().Be("DPST-5-1");
        entry.ObjectSize.Should().BeNull();
    }

    [Fact]
    public async Task ReadAsync_ReturnsNothingForRefsItWasNotAskedFor()
    {
        var result = await Read($"{Program}_O-1_R-1");

        result.Should().ContainSingle();
    }

    /// <summary>
    /// The scan stops as soon as every wanted ref and its ComObject is in, because the parameter and
    /// module sections behind them are most of a 60 MB file. That shortcut must not depend on
    /// ComObjectTable coming first: with the order reversed the ComObject is still picked up.
    /// </summary>
    [Fact]
    public async Task ReadAsync_ComObjectRefsBeforeComObjectTable_StillMerges()
    {
        const string reversed = $"""
            <KNX xmlns="http://knx.org/xml/project/20"><ManufacturerData><Manufacturer RefId="M-0001">
              <ApplicationPrograms><ApplicationProgram Id="{Program}"><Static>
                <ComObjectRefs>
                  <ComObjectRef Id="{Program}_O-1_R-1" RefId="{Program}_O-1" />
                </ComObjectRefs>
                <ComObjectTable>
                  <ComObject Id="{Program}_O-1" Number="1" ObjectSize="1 Bit" DatapointType="DPST-1-1" />
                </ComObjectTable>
              </Static></ApplicationProgram></ApplicationPrograms>
            </Manufacturer></ManufacturerData></KNX>
            """;

        var result = await ApplicationProgramReader.ReadAsync(
            Xml(reversed),
            new HashSet<string> { $"{Program}_O-1_R-1" },
            CancellationToken.None);

        result[$"{Program}_O-1_R-1"].DatapointType.Should().Be("DPST-1-1");
        result[$"{Program}_O-1_R-1"].ObjectSize.Should().Be("1 Bit");
    }

    /// <summary>A ref id that is simply not in the file must not leave a half-built entry behind.</summary>
    [Fact]
    public async Task ReadAsync_UnknownRefId_IsAbsentFromTheResult()
    {
        var result = await Read($"{Program}_O-1_R-1", $"{Program}_O-404_R-404");

        result.Should().ContainKey($"{Program}_O-1_R-1");
        result.Should().NotContainKey($"{Program}_O-404_R-404");
    }
}
