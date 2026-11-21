using FluentAssertions;
using KnxMonitor.Infrastructure.KnxConnection;
using Xunit;

namespace KnxMonitor.Infrastructure.Tests;

/// <summary>
/// Charts turn a decoded 1-bit value back into 0/1. The subtype decides which word is which:
/// "Up" is 0 for 1.008 and "Open" is 0 for 1.009, the opposite of what a generic word list
/// assumed, and most subtypes (Increase, Alarm, Day, ...) were in no list at all (#46).
/// </summary>
public class DptBitValueTests
{
    [Theory]
    [InlineData("DPST-1-8", "Up", 0)]
    [InlineData("DPST-1-8", "Down", 1)]
    [InlineData("1.009", "Open", 0)]
    [InlineData("1.009", "Close", 1)]
    [InlineData("DPST-1-1", "On", 1)]
    [InlineData("DPST-1-1", "Off", 0)]
    [InlineData("DPST-1-7", "Increase", 1)]
    [InlineData("DPST-1-24", "Night", 1)]
    [InlineData("DPST-1-100", "heating", 1)]
    [InlineData("DPST-1-19", "closed", 0)]
    public void TryGetBitValue_UsesTheSubtypeLabels(string dpt, string decoded, int expected)
    {
        DptConverter.TryGetBitValue(dpt, decoded, out var bit).Should().BeTrue();
        bit.Should().Be(expected);
    }

    [Fact]
    public void TryGetBitValue_MatchesWhatDecodeProduces_ForEverySubtype()
    {
        foreach (var sub in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 24, 100 })
        {
            var dpt = $"1.{sub:D3}";
            foreach (var on in new[] { false, true })
            {
                var decoded = DptConverter.Decode(dpt, new Knx.Falcon.GroupValue(on));
                DptConverter.TryGetBitValue(dpt, decoded, out var bit).Should().BeTrue($"{dpt} '{decoded}'");
                bit.Should().Be(on ? 1 : 0, $"{dpt} '{decoded}'");
            }
        }
    }

    [Theory]
    [InlineData("9.001", "21.5 °C")]
    [InlineData("DPST-1-8", "0x01")]
    [InlineData(null, "On")]
    [InlineData("DPST-1-8", null)]
    public void TryGetBitValue_ReturnsFalse_WhereTheCallerFallsBack(string? dpt, string? decoded)
    {
        DptConverter.TryGetBitValue(dpt, decoded, out _).Should().BeFalse();
    }
}
