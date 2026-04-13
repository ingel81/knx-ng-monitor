using FluentAssertions;
using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Helpers;

namespace KnxMonitor.ProjectParser.Tests.Unit.Helpers;

public class AddressConverterTests
{
    [Theory]
    [InlineData(0, "0/0/0")]
    [InlineData(1, "0/0/1")]
    [InlineData(255, "0/0/255")]
    [InlineData(256, "0/1/0")]
    [InlineData(2049, "1/0/1")]
    [InlineData(6657, "3/2/1")]
    [InlineData(32767, "15/7/255")]
    [InlineData(65535, "31/7/255")]
    public void ToGroupAddressString_ValidInt_ReturnsCorrectFormat(int rawAddress, string expected)
    {
        // Act
        var result = AddressConverter.ToGroupAddressString(rawAddress);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, "0.0.0")]
    [InlineData(1, "0.0.1")]
    [InlineData(255, "0.0.255")]
    [InlineData(256, "0.1.0")]
    [InlineData(4353, "1.1.1")]
    [InlineData(8448, "2.1.0")]
    [InlineData(61440, "15.0.0")]
    [InlineData(65535, "15.15.255")]
    public void ToPhysicalAddressString_ValidInt_ReturnsCorrectFormat(int rawAddress, string expected)
    {
        // Act
        var result = AddressConverter.ToPhysicalAddressString(rawAddress);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("0/0/0", 0)]
    [InlineData("0/0/1", 1)]
    [InlineData("0/0/255", 255)]
    [InlineData("0/1/0", 256)]
    [InlineData("1/0/1", 2049)]
    [InlineData("3/2/1", 6657)]
    [InlineData("15/7/255", 32767)]
    [InlineData("31/7/255", 65535)]
    public void FromGroupAddressString_ValidString_ReturnsCorrectInt(string address, int expected)
    {
        // Act
        var result = AddressConverter.FromGroupAddressString(address);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("0.0.0", 0)]
    [InlineData("0.0.1", 1)]
    [InlineData("0.0.255", 255)]
    [InlineData("0.1.0", 256)]
    [InlineData("1.1.1", 4353)]
    [InlineData("2.1.0", 8448)]
    [InlineData("15.0.0", 61440)]
    [InlineData("15.15.255", 65535)]
    public void FromPhysicalAddressString_ValidString_ReturnsCorrectInt(string address, int expected)
    {
        // Act
        var result = AddressConverter.FromPhysicalAddressString(address);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("1/2")]          // Missing sub
    [InlineData("1")]            // Only main
    [InlineData("1/2/3/4")]      // Too many parts
    [InlineData("")]             // Empty
    public void FromGroupAddressString_InvalidFormat_ThrowsArgumentException(string invalidAddress)
    {
        // Act
        Action act = () => AddressConverter.FromGroupAddressString(invalidAddress);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Invalid 3-level group address format*");
    }

    [Theory]
    [InlineData("1.2")]          // Missing device
    [InlineData("1")]            // Only area
    [InlineData("1.2.3.4")]      // Too many parts
    [InlineData("")]             // Empty
    public void FromPhysicalAddressString_InvalidFormat_ThrowsArgumentException(string invalidAddress)
    {
        // Act
        Action act = () => AddressConverter.FromPhysicalAddressString(invalidAddress);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Invalid physical address format*");
    }

    [Fact]
    public void GroupAddress_RoundTrip_PreservesValue()
    {
        // Arrange
        var originalRaw = 6657; // 3/2/1

        // Act
        var addressString = AddressConverter.ToGroupAddressString(originalRaw);
        var convertedRaw = AddressConverter.FromGroupAddressString(addressString);

        // Assert
        convertedRaw.Should().Be(originalRaw);
        addressString.Should().Be("3/2/1");
    }

    [Fact]
    public void PhysicalAddress_RoundTrip_PreservesValue()
    {
        // Arrange
        var originalRaw = 4353; // 1.1.1

        // Act
        var addressString = AddressConverter.ToPhysicalAddressString(originalRaw);
        var convertedRaw = AddressConverter.FromPhysicalAddressString(addressString);

        // Assert
        convertedRaw.Should().Be(originalRaw);
        addressString.Should().Be("1.1.1");
    }

    // ========== 2-Level Addressing Style Tests ==========

    [Theory]
    [InlineData(0, "0/0")]
    [InlineData(1, "0/1")]
    [InlineData(255, "0/255")]
    [InlineData(2047, "0/2047")]
    [InlineData(2048, "1/0")]
    [InlineData(2049, "1/1")]
    [InlineData(6657, "3/513")]
    [InlineData(32767, "15/2047")]
    [InlineData(65535, "31/2047")]
    public void ToGroupAddressString_TwoLevel_ReturnsCorrectFormat(int rawAddress, string expected)
    {
        // Act
        var result = AddressConverter.ToGroupAddressString(rawAddress, AddressingStyle.TwoLevel);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("0/0", 0)]
    [InlineData("0/1", 1)]
    [InlineData("0/255", 255)]
    [InlineData("0/2047", 2047)]
    [InlineData("1/0", 2048)]
    [InlineData("1/1", 2049)]
    [InlineData("3/513", 6657)]
    [InlineData("15/2047", 32767)]
    [InlineData("31/2047", 65535)]
    public void FromGroupAddressString_TwoLevel_ReturnsCorrectInt(string address, int expected)
    {
        // Act
        var result = AddressConverter.FromGroupAddressString(address, AddressingStyle.TwoLevel);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void GroupAddress_TwoLevel_RoundTrip_PreservesValue()
    {
        // Arrange
        var originalRaw = 6657; // 3/513 in 2-level

        // Act
        var addressString = AddressConverter.ToGroupAddressString(originalRaw, AddressingStyle.TwoLevel);
        var convertedRaw = AddressConverter.FromGroupAddressString(addressString, AddressingStyle.TwoLevel);

        // Assert
        convertedRaw.Should().Be(originalRaw);
        addressString.Should().Be("3/513");
    }

    [Theory]
    [InlineData("1")]            // Only main
    [InlineData("1/2/3")]        // Too many parts
    [InlineData("")]             // Empty
    public void FromGroupAddressString_TwoLevel_InvalidFormat_ThrowsArgumentException(string invalidAddress)
    {
        // Act
        Action act = () => AddressConverter.FromGroupAddressString(invalidAddress, AddressingStyle.TwoLevel);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Invalid 2-level group address format*");
    }

    [Theory]
    [InlineData("32/0")]         // Main out of range (max 31)
    [InlineData("0/2048")]       // Sub out of range (max 2047)
    [InlineData("50/100")]       // Main way out of range
    public void FromGroupAddressString_TwoLevel_OutOfRange_ThrowsArgumentException(string invalidAddress)
    {
        // Act
        Action act = () => AddressConverter.FromGroupAddressString(invalidAddress, AddressingStyle.TwoLevel);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Address out of range*");
    }

    // ========== Free-Level Addressing Style Tests ==========

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(255, "255")]
    [InlineData(2047, "2047")]
    [InlineData(6657, "6657")]
    [InlineData(32767, "32767")]
    [InlineData(65535, "65535")]
    public void ToGroupAddressString_FreeLevel_ReturnsCorrectFormat(int rawAddress, string expected)
    {
        // Act
        var result = AddressConverter.ToGroupAddressString(rawAddress, AddressingStyle.Free);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("255", 255)]
    [InlineData("2047", 2047)]
    [InlineData("6657", 6657)]
    [InlineData("32767", 32767)]
    [InlineData("65535", 65535)]
    public void FromGroupAddressString_FreeLevel_ReturnsCorrectInt(string address, int expected)
    {
        // Act
        var result = AddressConverter.FromGroupAddressString(address, AddressingStyle.Free);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void GroupAddress_FreeLevel_RoundTrip_PreservesValue()
    {
        // Arrange
        var originalRaw = 12345;

        // Act
        var addressString = AddressConverter.ToGroupAddressString(originalRaw, AddressingStyle.Free);
        var convertedRaw = AddressConverter.FromGroupAddressString(addressString, AddressingStyle.Free);

        // Assert
        convertedRaw.Should().Be(originalRaw);
        addressString.Should().Be("12345");
    }

    // ========== 3-Level Range Validation Tests ==========

    [Theory]
    [InlineData("32/0/0")]       // Main out of range (max 31)
    [InlineData("0/8/0")]        // Middle out of range (max 7)
    [InlineData("0/0/256")]      // Sub out of range (max 255)
    public void FromGroupAddressString_ThreeLevel_OutOfRange_ThrowsArgumentException(string invalidAddress)
    {
        // Act
        Action act = () => AddressConverter.FromGroupAddressString(invalidAddress, AddressingStyle.ThreeLevel);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Address out of range*");
    }

    // ========== Cross-Style Comparison Tests ==========

    [Fact]
    public void ToGroupAddressString_SameRawAddress_DifferentStyles_ProducesDifferentFormats()
    {
        // Arrange
        var rawAddress = 6657;

        // Act
        var threeLevel = AddressConverter.ToGroupAddressString(rawAddress, AddressingStyle.ThreeLevel);
        var twoLevel = AddressConverter.ToGroupAddressString(rawAddress, AddressingStyle.TwoLevel);
        var freeLevel = AddressConverter.ToGroupAddressString(rawAddress, AddressingStyle.Free);

        // Assert
        threeLevel.Should().Be("3/2/1");
        twoLevel.Should().Be("3/513");
        freeLevel.Should().Be("6657");
    }

    [Fact]
    public void ToGroupAddressString_DefaultStyle_UsesThreeLevel()
    {
        // Arrange
        var rawAddress = 6657;

        // Act
        var defaultStyle = AddressConverter.ToGroupAddressString(rawAddress);
        var explicitThreeLevel = AddressConverter.ToGroupAddressString(rawAddress, AddressingStyle.ThreeLevel);

        // Assert
        defaultStyle.Should().Be(explicitThreeLevel);
        defaultStyle.Should().Be("3/2/1");
    }
}
