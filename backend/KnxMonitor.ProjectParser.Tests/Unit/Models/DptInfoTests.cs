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
}
