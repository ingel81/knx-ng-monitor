using FluentAssertions;
using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Core.Models;

namespace KnxMonitor.ProjectParser.Tests.Unit.Models;

public class ModelPropertyTests
{
    [Fact]
    public void Device_Properties_Roundtrip()
    {
        var device = new Device
        {
            PhysicalAddress = "1.1.5",
            RawAddress = 0x1105,
            Name = "Sensor",
            Manufacturer = "ACME",
            ProductName = "KNX-Sensor",
            ProductRefId = "M-0001_P-1",
            HardwareProgramRefId = "M-0001_H-1",
            ApplicationProgramRefId = "M-0001_A-1",
            HasKnxSecure = true,
        };

        device.PhysicalAddress.Should().Be("1.1.5");
        device.RawAddress.Should().Be(0x1105);
        device.Name.Should().Be("Sensor");
        device.Manufacturer.Should().Be("ACME");
        device.ProductName.Should().Be("KNX-Sensor");
        device.ProductRefId.Should().Be("M-0001_P-1");
        device.HardwareProgramRefId.Should().Be("M-0001_H-1");
        device.ApplicationProgramRefId.Should().Be("M-0001_A-1");
        device.HasKnxSecure.Should().BeTrue();
    }

    [Fact]
    public void GroupAddress_Properties_Roundtrip()
    {
        var ga = new GroupAddress
        {
            Address = "1/2/3",
            RawAddress = 0x0A03,
            Name = "Light Kitchen",
            Description = "Main light",
            DatapointType = DptInfo.TryParse("DPST-1-1"),
            DataSecure = true,
            Identifier = "GA-42",
        };

        ga.Address.Should().Be("1/2/3");
        ga.RawAddress.Should().Be(0x0A03);
        ga.Name.Should().Be("Light Kitchen");
        ga.Description.Should().Be("Main light");
        ga.DatapointType.Should().NotBeNull();
        ga.DataSecure.Should().BeTrue();
        ga.Identifier.Should().Be("GA-42");
    }

    [Fact]
    public void ParserProgress_Properties_Roundtrip()
    {
        var progress = new ParserProgress
        {
            Step = ParseStep.ParseDevices,
            PercentComplete = 50,
            Message = "Loading devices",
            ItemsProcessed = 12,
            TotalItems = 24,
        };

        progress.Step.Should().Be(ParseStep.ParseDevices);
        progress.PercentComplete.Should().Be(50);
        progress.Message.Should().Be("Loading devices");
        progress.ItemsProcessed.Should().Be(12);
        progress.TotalItems.Should().Be(24);
    }

    [Fact]
    public void KeyringGroupAddress_Properties_Roundtrip()
    {
        var key = new byte[] { 1, 2, 3, 4 };
        var kga = new KeyringGroupAddress
        {
            Address = "1/1/1",
            Key = key,
        };

        kga.Address.Should().Be("1/1/1");
        kga.Key.Should().BeSameAs(key);
    }

    [Fact]
    public void KeyringData_Defaults_AreEmpty()
    {
        var data = new KeyringData();

        data.ProjectName.Should().BeNull();
        data.Devices.Should().BeEmpty();
        data.GroupAddresses.Should().BeEmpty();
        data.BackboneKey.Should().BeNull();
    }

    [Fact]
    public void KeyringDevice_Properties_Roundtrip()
    {
        var toolKey = new byte[16];
        var device = new KeyringDevice
        {
            IndividualAddress = "1.1.0",
            ToolKey = toolKey,
            ManagementPassword = "admin",
            Authentication = "auth",
        };

        device.IndividualAddress.Should().Be("1.1.0");
        device.ToolKey.Should().BeSameAs(toolKey);
        device.ManagementPassword.Should().Be("admin");
        device.Authentication.Should().Be("auth");
    }
}
