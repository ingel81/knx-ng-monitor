using FluentAssertions;
using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Helpers;

namespace KnxMonitor.ProjectParser.Tests.Unit.Helpers;

public class AddressConverterEdgeCaseTests
{
    [Fact]
    public void ToGroupAddressString_UnknownStyle_Throws()
    {
        var act = () => AddressConverter.ToGroupAddressString(1, (AddressingStyle)99);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Unknown addressing style*");
    }

    [Fact]
    public void FromGroupAddressString_UnknownStyle_Throws()
    {
        var act = () => AddressConverter.FromGroupAddressString("1/2/3", (AddressingStyle)99);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Unknown addressing style*");
    }
}
