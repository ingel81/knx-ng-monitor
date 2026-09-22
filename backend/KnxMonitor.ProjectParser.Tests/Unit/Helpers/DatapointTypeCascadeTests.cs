using FluentAssertions;
using KnxMonitor.ProjectParser.Core.Models;
using KnxMonitor.ProjectParser.Helpers;

namespace KnxMonitor.ProjectParser.Tests.Unit.Helpers;

public class DatapointTypeCascadeTests
{
    private static DptInfo Dpt(int main, int? sub = null) =>
        new() { Main = main, Sub = sub, OriginalString = sub.HasValue ? $"DPST-{main}-{sub}" : $"DPT-{main}" };

    /// <summary>A type the project or the catalog states outright.</summary>
    private static DatapointCandidate Declared(int main, int? sub = null) => new(Dpt(main, sub), false);

    /// <summary>A type only derived from the object's width.</summary>
    private static DatapointCandidate Inferred(int main) => new(Dpt(main), true);

    /// <summary>
    /// Only 1 bit, 4 bit and 14 bytes have their width to themselves; 2 and 4 bytes pick the type
    /// knx_master.xml marks Default="true". Everything else must stay empty — a wrong type decodes
    /// every telegram on that address wrongly.
    /// </summary>
    [Theory]
    [InlineData("1 Bit", 1)]
    [InlineData("4 Bit", 3)]
    [InlineData("2 Bytes", 9)]
    [InlineData("14 Bytes", 16)]
    public void FromObjectSize_UnambiguousSizes_ResolveToMainType(string size, int expectedMain)
    {
        var result = DatapointTypeCascade.FromObjectSize(size);

        result.Should().NotBeNull();
        result!.Main.Should().Be(expectedMain);
        result.Sub.Should().BeNull("an inferred main type is never padded out to .001");
    }

    [Theory]
    [InlineData("4 Bytes")]   // DPT 12/13 counters are as common here as DPT 14 floats
    [InlineData("1 Byte")]    // DPT 4, 5, 6, 17, 18, 20, 21, 26 …
    [InlineData("2 Bit")]     // DPT 2 and DPT 23
    [InlineData("3 Bytes")]   // DPT 10, 11, 30, 232, 240 …
    [InlineData("6 Bytes")]
    [InlineData("8 Bytes")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("LegacyVarData")]
    [InlineData("Bit")]
    [InlineData("x Bit")]
    [InlineData("0 Bit")]
    [InlineData("-1 Bit")]
    public void FromObjectSize_AmbiguousOrUnparseable_ReturnsNull(string? size)
    {
        DatapointTypeCascade.FromObjectSize(size).Should().BeNull();
    }

    [Fact]
    public void Combine_OnlyEmptyCandidates_ReturnsNull()
    {
        DatapointTypeCascade.Combine(new[] { new DatapointCandidate(null, false) }).Should().BeNull();
    }

    [Fact]
    public void Combine_NoCandidates_ReturnsNull()
    {
        DatapointTypeCascade.Combine(Array.Empty<DatapointCandidate>()).Should().BeNull();
    }

    [Fact]
    public void Combine_SingleCandidate_IsTakenAsIs()
    {
        var result = DatapointTypeCascade.Combine(new[] { Declared(5, 10) });

        result!.Main.Should().Be(5);
        result.Sub.Should().Be(10);
        result.OriginalString.Should().Be("DPST-5-10");
    }

    /// <summary>
    /// The case the main-type rule got wrong before: our own 2-byte default is DPT 9, but a declared
    /// DPT 7 has the exact same width. Treating both as equals made them cancel out and the address
    /// lost its type although one linked object had stated it outright.
    /// </summary>
    [Fact]
    public void Combine_DeclaredBeatsInferred_SameWidth()
    {
        var result = DatapointTypeCascade.Combine(new[] { Declared(7, 1), Inferred(9) });

        result!.Main.Should().Be(7);
        result.Sub.Should().Be(1);
    }

    /// <summary>
    /// An inferred main type never outranks a declared one, whatever width produced it — here a
    /// declared energy counter against a float inference.
    /// </summary>
    [Fact]
    public void Combine_DeclaredCounterBeatsAnInferredFloat()
    {
        var result = DatapointTypeCascade.Combine(new[] { Inferred(14), Declared(13, 13) });

        result!.Main.Should().Be(13);
        result.Sub.Should().Be(13);
    }

    [Fact]
    public void Combine_OnlyInferredCandidates_AreStillUsed()
    {
        var result = DatapointTypeCascade.Combine(new[] { Inferred(1), Inferred(1) });

        result!.Main.Should().Be(1);
        result.Sub.Should().BeNull();
    }

    [Fact]
    public void Combine_InferredCandidatesDisagree_ReturnsNull()
    {
        DatapointTypeCascade.Combine(new[] { Inferred(1), Inferred(14) }).Should().BeNull();
    }

    /// <summary>
    /// Two DECLARED main types really are irreconcilable: the linked objects describe different
    /// things and the address cannot be both.
    /// </summary>
    [Fact]
    public void Combine_MainTypesDisagree_ReturnsNull()
    {
        DatapointTypeCascade.Combine(new[] { Declared(1, 1), Declared(14) }).Should().BeNull();
    }

    [Fact]
    public void Combine_SubtypesDisagree_FallsBackToMainType()
    {
        var result = DatapointTypeCascade.Combine(new[] { Declared(1, 1), Declared(1, 2) });

        result!.Main.Should().Be(1);
        result.Sub.Should().BeNull();
        result.OriginalString.Should().Be("DPT-1");
    }

    [Fact]
    public void Combine_OnlyOneCandidateNamesASubtype_KeepsIt()
    {
        var result = DatapointTypeCascade.Combine(new[] { Declared(1), Declared(1, 1), Declared(1) });

        result!.Main.Should().Be(1);
        result.Sub.Should().Be(1);
    }

    [Fact]
    public void Combine_SameSubtypeSeveralTimes_KeepsIt()
    {
        var result = DatapointTypeCascade.Combine(new[] { Declared(9, 1), Declared(9, 1) });

        result!.Main.Should().Be(9);
        result.Sub.Should().Be(1);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(-1, false)]
    public void IsUsable_RequiresAMainType(int main, bool expected)
    {
        DatapointTypeCascade.IsUsable(new DptInfo { Main = main }).Should().Be(expected);
    }

    [Fact]
    public void IsUsable_Null_IsFalse()
    {
        DatapointTypeCascade.IsUsable(null).Should().BeFalse();
    }

    /// <summary>A value that parses to nothing but its original string must not count as a type.</summary>
    [Fact]
    public void IsUsable_UnparseableDptString_IsFalse()
    {
        DatapointTypeCascade.IsUsable(DptInfo.TryParse("LegacyVarData")).Should().BeFalse();
    }
}
