using FluentAssertions;
using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Core.Models;
using KnxMonitor.ProjectParser.Loaders;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnxMonitor.ProjectParser.Tests.Unit.Loaders;

public class LoaderErrorPathTests
{
    [Fact]
    public async Task Ets5Loader_MissingProjectXml_Throws()
    {
        var loader = new Ets5ProjectLoader(NullLogger<Ets5ProjectLoader>.Instance);
        var files = new ProjectFileMap(new Dictionary<string, byte[]>
        {
            ["P-0001/other.xml"] = System.Text.Encoding.UTF8.GetBytes("<x/>"),
        });

        var act = async () => await loader.LoadAsync(
            files,
            new ProjectFeatures { EtsVersion = EtsVersion.Ets5, AddressingStyle = AddressingStyle.ThreeLevel },
            new ParserOptions());

        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("*0.xml*");
    }

    [Fact]
    public async Task Ets6Loader_NoSegments_FallsBackToDirectDevices()
    {
        var loader = new Ets6ProjectLoader(NullLogger<Ets6ProjectLoader>.Instance);

        const string zeroXml =
            "<KNX xmlns=\"http://knx.org/xml/project/21\">" +
              "<DeviceInstance Address=\"100\" Name=\"direct\" />" +
            "</KNX>";

        var files = new ProjectFileMap(new Dictionary<string, byte[]>
        {
            ["P-0001/0.xml"] = System.Text.Encoding.UTF8.GetBytes(zeroXml),
        });

        var result = await loader.LoadAsync(
            files,
            new ProjectFeatures { EtsVersion = EtsVersion.Ets6, AddressingStyle = AddressingStyle.ThreeLevel },
            new ParserOptions());

        result.Devices.Should().ContainSingle(d => d.Name == "direct");
    }
}
