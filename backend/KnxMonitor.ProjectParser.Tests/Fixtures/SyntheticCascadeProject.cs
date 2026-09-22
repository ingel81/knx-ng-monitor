namespace KnxMonitor.ProjectParser.Tests.Fixtures;

/// <summary>
/// A hand-built ETS 5 project for the datapoint-type cascade. None of the shipped samples contains
/// the decisive case — ONE <c>&lt;ComObject&gt;</c> referenced by several <c>&lt;ComObjectRef&gt;</c>
/// with different datapoint types and object sizes — which is exactly where shortcutting from the
/// instance ref to the ComObject produces wrong types.
/// <para>
/// What each group address is built to prove:
/// <list type="table">
///   <item><term>0/0/1</term><description>ref without a type falls through to the ComObject → 1.001</description></item>
///   <item><term>0/0/2</term><description>the ref's type beats the ComObject's → 3.007 (the decisive case)</description></item>
///   <item><term>0/0/3</term><description><c>DatapointType=""</c> counts as absent → 1.001</description></item>
///   <item><term>0/0/4</term><description>a type already in the project is never overwritten → stays 1.001 although the instance says DPST-5-1</description></item>
///   <item><term>0/0/5</term><description>no type anywhere → ObjectSize "2 Bytes" → DPT 9</description></item>
///   <item><term>0/0/6</term><description>a declared type beats one inferred from ObjectSize → 1.001, not the DPT 9 of the other object</description></item>
///   <item><term>0/0/7</term><description>linked objects disagree on the subtype → main type only</description></item>
///   <item><term>0/0/8</term><description>the instance ref's own type AND its own flags win over everything → 9.001, Read+Transmit added</description></item>
///   <item><term>0/0/9</term><description>an unparseable type on the instance must not push the catalog aside → 1.001</description></item>
///   <item><term>0/0/10</term><description>two DECLARED main types contradict each other → no type at all</description></item>
///   <item><term>0/0/11</term><description>ObjectSize "4 Bytes" is ambiguous (DPT 12/13/14) → no type at all</description></item>
/// </list>
/// </para>
/// </summary>
internal static class SyntheticCascadeProject
{
    private const string Ns = "http://knx.org/xml/project/20";
    private const string Program = "M-0001_A-1000-11-AAAA";

    public static MemoryStream Build() => TestZipBuilder.BuildOuter(new[]
    {
        ("P-0001.signature", string.Empty),
        ("knx_master.xml", KnxMaster),
        ("M-0001/Hardware.xml", Hardware),
        ($"M-0001/{Program}.xml", ApplicationProgram),
        ("P-0001/project.xml", ProjectXml),
        ("P-0001/0.xml", InstallationXml),
    });

    private const string KnxMaster = $"""
        <KNX xmlns="{Ns}">
          <MasterData>
            <Manufacturers>
              <Manufacturer Id="M-0001" Name="Testhersteller" />
            </Manufacturers>
          </MasterData>
        </KNX>
        """;

    private const string ProjectXml = $"""
        <KNX xmlns="{Ns}" CreatedBy="ETS5" ToolVersion="5.7.1093.37876">
          <Project Id="P-0001">
            <ProjectInformation Name="Kaskadentest" GroupAddressStyle="ThreeLevel" />
          </Project>
        </KNX>
        """;

    private const string Hardware = $"""
        <KNX xmlns="{Ns}">
          <ManufacturerData>
            <Manufacturer RefId="M-0001">
              <Hardware>
                <Hardware Id="M-0001_H-1-1" Name="Testaktor">
                  <Products>
                    <Product Id="M-0001_H-1-1_P-1" Text="Testaktor 4-fach" />
                  </Products>
                  <Hardware2Programs>
                    <Hardware2Program Id="M-0001_H-1-1_HP-1000-11-AAAA">
                      <ApplicationProgramRef RefId="{Program}" />
                    </Hardware2Program>
                  </Hardware2Programs>
                </Hardware>
              </Hardware>
            </Manufacturer>
          </ManufacturerData>
        </KNX>
        """;

    private const string ApplicationProgram = $"""
        <KNX xmlns="{Ns}">
          <ManufacturerData>
            <Manufacturer RefId="M-0001">
              <ApplicationPrograms>
                <ApplicationProgram Id="{Program}">
                  <Static>
                    <ComObjectTable>
                      <ComObject Id="{Program}_O-1" Number="1" Name="Kanal A" Text="Kanal A"
                                 ObjectSize="1 Bit" DatapointType="DPST-1-1"
                                 ReadFlag="Disabled" WriteFlag="Enabled" CommunicationFlag="Enabled"
                                 TransmitFlag="Disabled" UpdateFlag="Disabled" ReadOnInitFlag="Disabled" />
                      <ComObject Id="{Program}_O-2" Number="2" Name="Kanal B" Text="Kanal B"
                                 ObjectSize="2 Bytes"
                                 ReadFlag="Disabled" WriteFlag="Enabled" CommunicationFlag="Enabled"
                                 TransmitFlag="Disabled" UpdateFlag="Disabled" ReadOnInitFlag="Disabled" />
                      <ComObject Id="{Program}_O-3" Number="3" Name="Zaehler" Text="Zaehler"
                                 ObjectSize="4 Bytes"
                                 ReadFlag="Enabled" WriteFlag="Disabled" CommunicationFlag="Enabled"
                                 TransmitFlag="Enabled" UpdateFlag="Disabled" ReadOnInitFlag="Disabled" />
                    </ComObjectTable>
                    <ComObjectRefs>
                      <ComObjectRef Id="{Program}_O-1_R-1" RefId="{Program}_O-1" />
                      <ComObjectRef Id="{Program}_O-1_R-2" RefId="{Program}_O-1"
                                    DatapointType="DPST-3-7" ObjectSize="4 Bit" />
                      <ComObjectRef Id="{Program}_O-1_R-3" RefId="{Program}_O-1"
                                    DatapointType="" ReadFlag="Enabled" />
                      <ComObjectRef Id="{Program}_O-1_R-8" RefId="{Program}_O-1"
                                    DatapointType="DPST-1-2" />
                      <ComObjectRef Id="{Program}_O-2_R-4" RefId="{Program}_O-2" />
                      <ComObjectRef Id="{Program}_O-2_R-5" RefId="{Program}_O-2" />
                      <ComObjectRef Id="{Program}_O-2_R-7" RefId="{Program}_O-2" />
                      <ComObjectRef Id="{Program}_O-1_R-6" RefId="{Program}_O-1" />
                      <ComObjectRef Id="{Program}_O-1_R-9" RefId="{Program}_O-1" />
                      <ComObjectRef Id="{Program}_O-1_R-10" RefId="{Program}_O-1" />
                      <ComObjectRef Id="{Program}_O-2_R-11" RefId="{Program}_O-2" />
                      <ComObjectRef Id="{Program}_O-3_R-12" RefId="{Program}_O-3" />
                    </ComObjectRefs>
                  </Static>
                </ApplicationProgram>
              </ApplicationPrograms>
            </Manufacturer>
          </ManufacturerData>
        </KNX>
        """;

    private const string InstallationXml = $"""
        <KNX xmlns="{Ns}" CreatedBy="ETS5" ToolVersion="5.7.1093.37876">
          <Project Id="P-0001">
            <Installations>
              <Installation InstallationId="0" Name="" GroupAddressStyle="ThreeLevel">
                <Topology>
                  <Area Id="P-0001-0_A-0" Name="Bereich" Address="0">
                    <Line Id="P-0001-0_L-1" Name="Linie" Address="0">
                      <DeviceInstance Id="P-0001-0_DI-1" Address="1" Name="Testaktor"
                                      ProductRefId="M-0001_H-1-1_P-1"
                                      Hardware2ProgramRefId="M-0001_H-1-1_HP-1000-11-AAAA">
                        <ComObjectInstanceRefs>
                          <ComObjectInstanceRef RefId="O-1_R-1" Links="GA-1" />
                          <ComObjectInstanceRef RefId="O-1_R-2" Links="GA-2" />
                          <ComObjectInstanceRef RefId="O-1_R-3" Links="GA-3" />
                          <ComObjectInstanceRef RefId="O-2_R-4" DatapointType="DPST-5-1" Links="GA-4" />
                          <ComObjectInstanceRef RefId="O-2_R-5" Links="GA-5" />
                          <ComObjectInstanceRef RefId="O-1_R-6" Links="GA-6" />
                          <ComObjectInstanceRef RefId="O-2_R-7" Links="GA-6" />
                          <ComObjectInstanceRef RefId="O-1_R-9" Links="GA-7" />
                          <ComObjectInstanceRef RefId="O-1_R-8" Links="GA-7" />
                          <ComObjectInstanceRef RefId="O-1_R-1" DatapointType="DPST-9-1"
                                                ReadFlag="Enabled" TransmitFlag="Enabled" Links="GA-8" />
                          <ComObjectInstanceRef RefId="O-1_R-1" DatapointType="LegacyVarData" Links="GA-9" />
                          <ComObjectInstanceRef RefId="O-1_R-10" DatapointType="DPST-1-1" Links="GA-10" />
                          <ComObjectInstanceRef RefId="O-2_R-11" DatapointType="DPST-9-1" Links="GA-10" />
                          <ComObjectInstanceRef RefId="O-3_R-12" Links="GA-11" />
                        </ComObjectInstanceRefs>
                      </DeviceInstance>
                    </Line>
                  </Area>
                </Topology>
                <GroupAddresses>
                  <GroupRanges>
                    <GroupRange Id="P-0001-0_GR-1" Name="Haupt" RangeStart="1" RangeEnd="2047">
                      <GroupRange Id="P-0001-0_GR-2" Name="Mittel" RangeStart="1" RangeEnd="255">
                        <GroupAddress Id="P-0001-0_GA-1" Address="1" Name="Ref ohne Typ" />
                        <GroupAddress Id="P-0001-0_GA-2" Address="2" Name="Ref schlaegt ComObject" />
                        <GroupAddress Id="P-0001-0_GA-3" Address="3" Name="Leerer Typ" />
                        <GroupAddress Id="P-0001-0_GA-4" Address="4" Name="Schon getypt" DatapointType="DPST-1-1" />
                        <GroupAddress Id="P-0001-0_GA-5" Address="5" Name="Nur ObjectSize" />
                        <GroupAddress Id="P-0001-0_GA-6" Address="6" Name="Haupttypen uneins" />
                        <GroupAddress Id="P-0001-0_GA-7" Address="7" Name="Subtypen uneins" />
                        <GroupAddress Id="P-0001-0_GA-8" Address="8" Name="Instanz gewinnt" />
                        <GroupAddress Id="P-0001-0_GA-9" Address="9" Name="Instanz unlesbar" />
                        <GroupAddress Id="P-0001-0_GA-10" Address="10" Name="Deklarationen uneins" />
                        <GroupAddress Id="P-0001-0_GA-11" Address="11" Name="Vier Bytes mehrdeutig" />
                      </GroupRange>
                    </GroupRange>
                  </GroupRanges>
                </GroupAddresses>
              </Installation>
            </Installations>
          </Project>
        </KNX>
        """;
}
