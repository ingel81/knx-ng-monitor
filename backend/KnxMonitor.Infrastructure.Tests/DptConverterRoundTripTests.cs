using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using Knx.Falcon;
using KnxMonitor.Infrastructure.KnxConnection;
using Xunit;

namespace KnxMonitor.Infrastructure.Tests;

/// <summary>
/// Deterministic verification of the GA-write encoding path (roadmap #4d) WITHOUT a bus.
/// <see cref="DptConverter.Encode"/> is the inverse of <see cref="DptConverter.Decode"/>; both
/// delegate to Falcon's pure DPT codec. Encoding a user value and decoding it back must round-trip,
/// and for fixed-width integer DPTs the produced raw bytes must match the expected on-wire value.
/// This closes the "byte-level encoding must be checked" gap as far as is possible off-hardware:
/// the bus only transports the bytes Falcon produces here, which we assert directly.
/// </summary>
public class DptConverterRoundTripTests
{
    private static double LeadingNumber(string decoded)
    {
        var m = Regex.Match(decoded, @"-?\d+(?:\.\d+)?");
        m.Success.Should().BeTrue($"decoded value '{decoded}' should contain a number");
        return double.Parse(m.Value, CultureInfo.InvariantCulture);
    }

    [Theory]
    [InlineData("1.001", "1", true)]   // On
    [InlineData("1.001", "0", false)]  // Off
    [InlineData("1.002", "true", true)]
    [InlineData("1.002", "false", false)]
    public void Encode_Dpt1_Bool_RoundTrips(string dpt, string input, bool expectedTrue)
    {
        var gv = DptConverter.Encode(dpt, input);

        gv.Should().NotBeNull();
        // 1-bit telegrams carry a single payload bit.
        var decoded = DptConverter.Decode(dpt, gv).ToLowerInvariant();
        if (expectedTrue)
            decoded.Should().MatchRegex("on|true|enable|yes|open|up|active|start|1");
        else
            decoded.Should().MatchRegex("off|false|disable|no|close|down|inactive|stop|0");
    }

    [Theory]
    [InlineData("5.010", "0", new byte[] { 0x00 })]
    [InlineData("5.010", "128", new byte[] { 0x80 })]
    [InlineData("5.010", "255", new byte[] { 0xFF })]
    public void Encode_Dpt5010_Counter_ProducesExactBytes(string dpt, string input, byte[] expectedBytes)
    {
        var gv = DptConverter.Encode(dpt, input);

        gv.Should().NotBeNull();
        gv!.Value.Should().Equal(expectedBytes);                 // exact on-wire bytes
        LeadingNumber(DptConverter.Decode(dpt, gv)).Should().Be(double.Parse(input)); // round-trip
    }

    [Theory]
    [InlineData("7.001", "5000", new byte[] { 0x13, 0x88 })]    // 16-bit unsigned, big-endian
    [InlineData("7.001", "0", new byte[] { 0x00, 0x00 })]
    public void Encode_Dpt7_TwoByteUnsigned_ProducesExactBytes(string dpt, string input, byte[] expectedBytes)
    {
        var gv = DptConverter.Encode(dpt, input);

        gv.Should().NotBeNull();
        gv!.Value.Should().Equal(expectedBytes);
        LeadingNumber(DptConverter.Decode(dpt, gv)).Should().Be(double.Parse(input));
    }

    [Theory]
    [InlineData("9.001", "21.5")]   // temperature (2-byte float)
    [InlineData("9.001", "-10.0")]
    [InlineData("14.056", "42.69")] // 4-byte IEEE float (power)
    public void Encode_FloatDpts_RoundTripWithinTolerance(string dpt, string input)
    {
        var gv = DptConverter.Encode(dpt, input);

        gv.Should().NotBeNull();
        var expected = double.Parse(input, CultureInfo.InvariantCulture);
        // DPT-9 is a 16-bit half-float → small quantisation error is expected; DPT-14 is exact-ish.
        LeadingNumber(DptConverter.Decode(dpt, gv)).Should().BeApproximately(expected, dpt.StartsWith("9") ? 0.1 : 0.01);
    }

    [Theory]
    [InlineData("13.001", "-1000")] // 4-byte signed
    [InlineData("13.001", "123456")]
    public void Encode_Dpt13_FourByteSigned_RoundTrips(string dpt, string input)
    {
        var gv = DptConverter.Encode(dpt, input);

        gv.Should().NotBeNull();
        gv!.Value.Length.Should().Be(4);
        LeadingNumber(DptConverter.Decode(dpt, gv)).Should().Be(double.Parse(input));
    }

    [Fact]
    public void Encode_NoDpt_AcceptsRawHex()
    {
        var gv = DptConverter.Encode(null, "0x80");
        gv.Should().NotBeNull();
        gv!.Value.Should().Equal(new byte[] { 0x80 });
    }

    [Fact]
    public void Encode_UnparseableValue_ForNumericDpt_DoesNotThrow()
    {
        // A non-numeric input for a numeric DPT must fail softly (null), never throw — the write
        // endpoint then reports "could not encode" instead of crashing.
        var act = () => DptConverter.Encode("9.001", "not-a-number");
        act.Should().NotThrow();
    }
}
