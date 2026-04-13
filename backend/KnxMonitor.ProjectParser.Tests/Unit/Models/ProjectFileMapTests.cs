using System.Text;
using FluentAssertions;
using KnxMonitor.ProjectParser.Core.Models;

namespace KnxMonitor.ProjectParser.Tests.Unit.Models;

public class ProjectFileMapTests
{
    private static ProjectFileMap CreateMap() =>
        new(new Dictionary<string, byte[]>
        {
            ["0.xml"] = Encoding.UTF8.GetBytes("root"),
            ["P-0001/0.xml"] = Encoding.UTF8.GetBytes("project"),
            ["M-0001/Hardware.xml"] = Encoding.UTF8.GetBytes("hw1"),
            ["M-0002/Hardware.xml"] = Encoding.UTF8.GetBytes("hw2"),
        });

    [Fact]
    public void Count_ReturnsEntryCount()
    {
        CreateMap().Count.Should().Be(4);
    }

    [Fact]
    public void FilePaths_EnumeratesAllKeys()
    {
        CreateMap().FilePaths.Should().HaveCount(4);
    }

    [Fact]
    public void Contains_Matches_CaseInsensitive()
    {
        var map = CreateMap();

        map.Contains("0.xml").Should().BeTrue();
        map.Contains("0.XML").Should().BeTrue();
        map.Contains("missing.xml").Should().BeFalse();
    }

    [Fact]
    public void GetBytes_ExistingFile_ReturnsBytes()
    {
        var bytes = CreateMap().GetBytes("0.xml");
        Encoding.UTF8.GetString(bytes).Should().Be("root");
    }

    [Fact]
    public void GetBytes_MissingFile_Throws()
    {
        var act = () => CreateMap().GetBytes("not-there.xml");

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void OpenRead_ReturnsReadableStream()
    {
        using var stream = CreateMap().OpenRead("0.xml");

        using var reader = new StreamReader(stream);
        reader.ReadToEnd().Should().Be("root");
    }

    [Fact]
    public void FindPaths_Predicate_ReturnsMatches()
    {
        var matches = CreateMap().FindPaths(p => p.EndsWith(".xml")).ToList();

        matches.Should().HaveCount(4);
    }

    [Fact]
    public void FindFirstByName_MatchesBareAndPrefixed()
    {
        var map = CreateMap();

        map.FindFirstByName("0.xml").Should().NotBeNull();
        map.FindFirstByName("Hardware.xml").Should().NotBeNull();
        map.FindFirstByName("missing").Should().BeNull();
    }

    [Fact]
    public void FindAllByName_ReturnsAllHardwareFiles()
    {
        CreateMap().FindAllByName("Hardware.xml").Should().HaveCount(2);
    }

    [Fact]
    public void FindFirstByName_IgnoresSimilarPrefix()
    {
        var map = new ProjectFileMap(new Dictionary<string, byte[]>
        {
            ["deep/nested/a.xml"] = new byte[] { 1 },
            ["ab.xml"] = new byte[] { 2 },
        });

        // "a.xml" should match "deep/nested/a.xml" but NOT "ab.xml"
        map.FindFirstByName("a.xml").Should().Be("deep/nested/a.xml");
    }
}
