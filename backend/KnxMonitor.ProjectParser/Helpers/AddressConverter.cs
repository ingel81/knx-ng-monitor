using KnxMonitor.ProjectParser.Core.Enums;

namespace KnxMonitor.ProjectParser.Helpers;

public static class AddressConverter
{
    /// <summary>
    /// Convert raw KNX group address integer to string format (3-level by default)
    /// </summary>
    public static string ToGroupAddressString(int rawAddress)
    {
        return ToGroupAddressString(rawAddress, AddressingStyle.ThreeLevel);
    }

    /// <summary>
    /// Convert raw KNX group address integer to string format with specified addressing style
    /// </summary>
    /// <param name="rawAddress">16-bit raw address value</param>
    /// <param name="style">Addressing style (ThreeLevel, TwoLevel, or FreeLevel)</param>
    /// <returns>Formatted address string</returns>
    public static string ToGroupAddressString(int rawAddress, AddressingStyle style)
    {
        return style switch
        {
            AddressingStyle.ThreeLevel => ToThreeLevelString(rawAddress),
            AddressingStyle.TwoLevel => ToTwoLevelString(rawAddress),
            AddressingStyle.Free => rawAddress.ToString(),
            _ => throw new ArgumentException($"Unknown addressing style: {style}", nameof(style))
        };
    }

    /// <summary>
    /// Convert to 3-level format: main/middle/sub (5/3/8 bits)
    /// Range: 0-31 / 0-7 / 0-255
    /// </summary>
    private static string ToThreeLevelString(int rawAddress)
    {
        var main = (rawAddress >> 11) & 0x1F;    // 5 bits
        var middle = (rawAddress >> 8) & 0x07;   // 3 bits
        var sub = rawAddress & 0xFF;             // 8 bits

        return $"{main}/{middle}/{sub}";
    }

    /// <summary>
    /// Convert to 2-level format: main/sub (5/11 bits)
    /// Range: 0-31 / 0-2047
    /// </summary>
    private static string ToTwoLevelString(int rawAddress)
    {
        var main = (rawAddress >> 11) & 0x1F;    // 5 bits
        var sub = rawAddress & 0x7FF;            // 11 bits

        return $"{main}/{sub}";
    }

    /// <summary>
    /// Convert raw KNX physical address integer to string format (area.line.device)
    /// </summary>
    public static string ToPhysicalAddressString(int rawAddress)
    {
        // Format: area.line.device
        // Bits: area(4) | line(4) | device(8)
        var area = (rawAddress >> 12) & 0x0F;
        var line = (rawAddress >> 8) & 0x0F;
        var device = rawAddress & 0xFF;

        return $"{area}.{line}.{device}";
    }

    /// <summary>
    /// Convert group address string to raw integer (3-level by default)
    /// </summary>
    public static int FromGroupAddressString(string address)
    {
        return FromGroupAddressString(address, AddressingStyle.ThreeLevel);
    }

    /// <summary>
    /// Convert group address string to raw integer with specified addressing style
    /// </summary>
    /// <param name="address">Address string (format depends on style)</param>
    /// <param name="style">Addressing style (ThreeLevel, TwoLevel, or FreeLevel)</param>
    /// <returns>16-bit raw address value</returns>
    public static int FromGroupAddressString(string address, AddressingStyle style)
    {
        return style switch
        {
            AddressingStyle.ThreeLevel => FromThreeLevelString(address),
            AddressingStyle.TwoLevel => FromTwoLevelString(address),
            AddressingStyle.Free => int.Parse(address),
            _ => throw new ArgumentException($"Unknown addressing style: {style}", nameof(style))
        };
    }

    /// <summary>
    /// Parse 3-level format: main/middle/sub (5/3/8 bits)
    /// </summary>
    private static int FromThreeLevelString(string address)
    {
        var parts = address.Split('/');
        if (parts.Length != 3)
            throw new ArgumentException("Invalid 3-level group address format. Expected: main/middle/sub", nameof(address));

        var main = int.Parse(parts[0]);
        var middle = int.Parse(parts[1]);
        var sub = int.Parse(parts[2]);

        // Validate ranges
        if (main > 31 || middle > 7 || sub > 255)
            throw new ArgumentException($"Address out of range. Max: 31/7/255, Got: {address}", nameof(address));

        return (main << 11) | (middle << 8) | sub;
    }

    /// <summary>
    /// Parse 2-level format: main/sub (5/11 bits)
    /// </summary>
    private static int FromTwoLevelString(string address)
    {
        var parts = address.Split('/');
        if (parts.Length != 2)
            throw new ArgumentException("Invalid 2-level group address format. Expected: main/sub", nameof(address));

        var main = int.Parse(parts[0]);
        var sub = int.Parse(parts[1]);

        // Validate ranges
        if (main > 31 || sub > 2047)
            throw new ArgumentException($"Address out of range. Max: 31/2047, Got: {address}", nameof(address));

        return (main << 11) | sub;
    }

    /// <summary>
    /// Convert physical address string (area.line.device) to raw integer
    /// </summary>
    public static int FromPhysicalAddressString(string address)
    {
        var parts = address.Split('.');
        if (parts.Length != 3)
            throw new ArgumentException("Invalid physical address format. Expected: area.line.device", nameof(address));

        var area = int.Parse(parts[0]);
        var line = int.Parse(parts[1]);
        var device = int.Parse(parts[2]);

        return (area << 12) | (line << 8) | device;
    }
}
