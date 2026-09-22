using FluentAssertions;
using KnxMonitor.ProjectParser.Core.Models;

namespace KnxMonitor.ProjectParser.Tests.Unit.Models;

public class DptInfoTests
{
    [Theory]
    [InlineData("DPST-1-1", 1, 1)]
    [InlineData("DPST-5-001", 5, 1)]
    [InlineData("DPST-14-056", 14, 56)]
    [InlineData("DPST-19-1", 19, 1)]
    [InlineData("DPT-1-1", 1, 1)]
    [InlineData("DPT-5-001", 5, 1)]
    [InlineData("DPT-9-1", 9, 1)]
    public void TryParse_ValidDptStringWithSub_ReturnsCorrectInfo(
        string input,
        int expectedMain,
        int expectedSub)
    {
        // Act
        var result = DptInfo.TryParse(input);

        // Assert
        result.Should().NotBeNull();
        result!.Main.Should().Be(expectedMain);
        result.Sub.Should().Be(expectedSub);
        result.OriginalString.Should().Be(input);
    }

    [Theory]
    [InlineData("DPST-9", 9)]
    [InlineData("DPT-1", 1)]
    [InlineData("DPT-14", 14)]
    public void TryParse_ValidDptStringWithoutSub_ReturnsCorrectInfo(
        string input,
        int expectedMain)
    {
        // Act
        var result = DptInfo.TryParse(input);

        // Assert
        result.Should().NotBeNull();
        result!.Main.Should().Be(expectedMain);
        result.Sub.Should().BeNull();
        result.OriginalString.Should().Be(input);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void TryParse_EmptyOrNullString_ReturnsNull(string? input)
    {
        // Act
        var result = DptInfo.TryParse(input);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("InvalidFormat")]
    [InlineData("123")]
    public void TryParse_InvalidFormat_ReturnsOnlyOriginalString(string input)
    {
        // Act
        var result = DptInfo.TryParse(input);

        // Assert
        result.Should().NotBeNull();
        result!.OriginalString.Should().Be(input);
        result.Main.Should().Be(0); // Default value
        result.Sub.Should().BeNull();
    }

    [Fact]
    public void ToString_WithSub_ReturnsFormattedString()
    {
        // Arrange
        var dpt = new DptInfo { Main = 5, Sub = 1, OriginalString = "DPST-5-001" };

        // Act
        var result = dpt.ToString();

        // Assert
        result.Should().Be("DPT 5.001");
    }

    [Fact]
    public void ToString_WithoutSub_ReturnsFormattedString()
    {
        // Arrange
        var dpt = new DptInfo { Main = 9, Sub = null, OriginalString = "DPST-9" };

        // Act
        var result = dpt.ToString();

        // Assert
        result.Should().Be("DPT 9");
    }

    [Theory]
    [InlineData("DPST-1-001", 1, 1)]  // Leading zeros should be handled
    [InlineData("DPST-5-000", 5, 0)]  // Sub can be 0
    [InlineData("DPST-232-600", 232, 600)]  // Large numbers
    public void TryParse_EdgeCases_HandlesCorrectly(
        string input,
        int expectedMain,
        int expectedSub)
    {
        // Act
        var result = DptInfo.TryParse(input);

        // Assert
        result.Should().NotBeNull();
        result!.Main.Should().Be(expectedMain);
        result.Sub.Should().Be(expectedSub);
    }

    // --- knx:IDREFS: DatapointType is a whitespace-separated LIST, not a single id ---

    [Theory]
    [InlineData("DPT-5 DPST-5-10", 5, 10)]
    [InlineData("DPST-5-10 DPT-5", 5, 10)]
    [InlineData("DPT-1 DPST-1-001", 1, 1)]
    [InlineData("  DPT-9   DPST-9-1  ", 9, 1)]
    [InlineData("DPT-14	DPST-14-056", 14, 56)]
    public void TryParse_MultipleTokens_PrefersTheMostSpecific(
        string input,
        int expectedMain,
        int expectedSub)
    {
        var result = DptInfo.TryParse(input);

        result.Should().NotBeNull();
        result!.Main.Should().Be(expectedMain);
        result.Sub.Should().Be(expectedSub);
        result.OriginalString.Should().Be(input);
    }

    [Fact]
    public void TryParse_MultipleTokensWithoutSubtype_KeepsTheFirstMainType()
    {
        var result = DptInfo.TryParse("DPT-5 DPT-6");

        result.Should().NotBeNull();
        result!.Main.Should().Be(5);
        result.Sub.Should().BeNull();
    }

    [Fact]
    public void TryParse_SubtypeTokenWins_EvenWhenMainTypesDiffer()
    {
        // Malformed but seen in the wild; the id carrying a subtype is the more specific statement.
        var result = DptInfo.TryParse("DPT-5 DPST-9-1");

        result.Should().NotBeNull();
        result!.Main.Should().Be(9);
        result.Sub.Should().Be(1);
    }

    /// <summary>
    /// A list of MANY subtypes is not "main plus subtype" but "these subtypes are accepted" —
    /// xknx_test_project has an object typed
    /// <c>"DPT-9 DPST-9-1 DPST-9-2 … DPST-9-31"</c>. Picking one of them would be a guess, so only
    /// the main type may survive.
    /// </summary>
    [Fact]
    public void TryParse_ListOfAcceptedSubtypes_KeepsOnlyTheMainType()
    {
        var value = "DPT-9 " + string.Join(' ', Enumerable.Range(1, 31).Select(i => $"DPST-9-{i}"));

        var result = DptInfo.TryParse(value);

        result.Should().NotBeNull();
        result!.Main.Should().Be(9);
        result.Sub.Should().BeNull();
    }

    [Fact]
    public void TryParse_SameSubtypeRepeated_StillCountsAsOne()
    {
        var result = DptInfo.TryParse("DPT-1 DPST-1-001 DPST-1-1");

        result.Should().NotBeNull();
        result!.Main.Should().Be(1);
        result.Sub.Should().Be(1);
    }

    [Theory]
    [InlineData("garbage more garbage")]
    [InlineData("DPS-1-1")]
    public void TryParse_MultiTokenGarbage_ReturnsOnlyOriginalString(string input)
    {
        var result = DptInfo.TryParse(input);

        result.Should().NotBeNull();
        result!.OriginalString.Should().Be(input);
        result.Main.Should().Be(0);
        result.Sub.Should().BeNull();
    }

    // --- ToDptId: the canonical form that gets stored and decoded ---

    [Theory]
    [InlineData("DPST-1-1", "DPST-1-1")]
    [InlineData("DPST-5-001", "DPST-5-1")]
    [InlineData("DPT-9", "DPT-9")]
    [InlineData("DPT-5 DPST-5-10", "DPST-5-10")]
    public void ToDptId_ReturnsWhatWasUnderstood(string input, string expected)
    {
        DptInfo.TryParse(input)!.ToDptId().Should().Be(expected);
    }

    /// <summary>
    /// The reason this exists: a decoder that reads the first two numbers out of
    /// "DPT-9 DPST-9-1 DPST-9-2 …" lands on 9.001, a subtype the project never committed to.
    /// </summary>
    [Fact]
    public void ToDptId_ListOfAcceptedSubtypes_CollapsesToTheMainType()
    {
        var value = "DPT-9 " + string.Join(' ', Enumerable.Range(1, 31).Select(i => $"DPST-9-{i}"));

        DptInfo.TryParse(value)!.ToDptId().Should().Be("DPT-9");
    }

    /// <summary>An unparseable value is passed on untouched instead of being dropped.</summary>
    [Fact]
    public void ToDptId_Unparseable_FallsBackToTheRawString()
    {
        DptInfo.TryParse("LegacyVarData")!.ToDptId().Should().Be("LegacyVarData");
    }
}
