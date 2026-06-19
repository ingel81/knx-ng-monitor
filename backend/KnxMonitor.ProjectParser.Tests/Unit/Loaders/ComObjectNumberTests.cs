using FluentAssertions;
using KnxMonitor.ProjectParser.Loaders;

namespace KnxMonitor.ProjectParser.Tests.Unit.Loaders;

/// <summary>
/// Focused tests for <see cref="BaseProjectLoader.ExtractComObjectNumber"/>. The object number is the
/// LAST numeric segment of the "O-..." token of a ComObjectInstanceRef RefId:
/// non-module ids "O-&lt;num&gt;_R-..." yield &lt;num&gt;, module ids "...O-&lt;ch&gt;-&lt;num&gt;_R-..."
/// yield &lt;num&gt; (the object number, not the channel).
/// </summary>
public class ComObjectNumberTests
{
    [Theory]
    // Non-module: number directly after "O-".
    [InlineData("O-71_R-1506", 71)]
    [InlineData("O-334_R-21", 334)]
    [InlineData("O-0_R-1", 0)]
    // Module: channel-object pair "O-<ch>-<num>"; extract <num>, not the channel.
    [InlineData("MD-2_M-1_MI-1_O-2-9_R-4", 9)]
    [InlineData("MD-2_M-1_MI-1_O-2-35_R-65", 35)]
    [InlineData("MD-2_M-2_MI-1_O-2-1_R-1", 1)]
    // No "_R" suffix: token runs to the end of the string.
    [InlineData("O-12", 12)]
    [InlineData("MD-1_O-3-7", 7)]
    public void ExtractComObjectNumber_ReturnsObjectNumber(string refId, int expected)
    {
        BaseProjectLoader.ExtractComObjectNumber(refId).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("R-5_X-7")] // no "O-" token at all
    public void ExtractComObjectNumber_ReturnsZero_WhenNoObjectId(string? refId)
    {
        BaseProjectLoader.ExtractComObjectNumber(refId).Should().Be(0);
    }
}
