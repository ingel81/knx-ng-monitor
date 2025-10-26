using System.Text;

namespace KnxMonitor.Infrastructure.KnxConnection;

/// <summary>
/// Converts raw KNX DPT (Datapoint Type) values to human-readable formats
/// </summary>
public static class DptConverter
{
    /// <summary>
    /// Decode a raw KNX value based on the DPT type
    /// </summary>
    /// <param name="dptType">DPT type string (e.g., "1.001", "5.001", "9.001")</param>
    /// <param name="value">Raw byte array from KNX telegram</param>
    /// <returns>Decoded human-readable string</returns>
    public static string Decode(string? dptType, byte[] value)
    {
        if (value == null || value.Length == 0)
            return string.Empty;

        if (string.IsNullOrEmpty(dptType))
            return ConvertToHex(value);

        try
        {
            // Extract main DPT number (e.g., "1" from "1.001" or "DPT-1")
            var mainDpt = ExtractMainDpt(dptType);

            return mainDpt switch
            {
                1 => DecodeDpt1(value),      // Boolean (1 bit)
                2 => DecodeDpt2(value),      // 1-bit controlled (2 bits)
                3 => DecodeDpt3(value),      // 3-bit controlled (4 bits)
                5 => DecodeDpt5(value),      // 8-bit unsigned (0-255)
                6 => DecodeDpt6(value),      // 8-bit signed (-128 to 127)
                7 => DecodeDpt7(value),      // 16-bit unsigned (0-65535)
                8 => DecodeDpt8(value),      // 16-bit signed (-32768 to 32767)
                9 => DecodeDpt9(value),      // 16-bit float
                10 => DecodeDpt10(value),    // Time
                11 => DecodeDpt11(value),    // Date
                12 => DecodeDpt12(value),    // 32-bit unsigned
                13 => DecodeDpt13(value),    // 32-bit signed
                14 => DecodeDpt14(value),    // 32-bit float
                16 => DecodeDpt16(value),    // String (14 chars)
                17 => DecodeDpt17(value),    // Scene number
                18 => DecodeDpt18(value),    // Scene control
                19 => DecodeDpt19(value),    // Date time
                20 => DecodeDpt20(value),    // 8-bit enum
                _ => ConvertToHex(value)
            };
        }
        catch (Exception)
        {
            return ConvertToHex(value);
        }
    }

    private static int ExtractMainDpt(string dptType)
    {
        // Handle formats: "1.001", "DPT-1", "DPST-1-1", "DPT 9.001", "1"
        // Remove all variants of DPT prefix
        var cleaned = dptType
            .Replace("DPST-", "", StringComparison.OrdinalIgnoreCase)
            .Replace("DPT-", "", StringComparison.OrdinalIgnoreCase)
            .Replace("DPST", "", StringComparison.OrdinalIgnoreCase)
            .Replace("DPT", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        // Split by common delimiters
        var parts = cleaned.Split(new[] { '.', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length > 0 && int.TryParse(parts[0].Trim(), out var mainDpt))
        {
            return mainDpt;
        }
        return 0;
    }

    #region DPT Decoders

    /// <summary>DPT 1.xxx - Boolean (Switch, On/Off, etc.)</summary>
    private static string DecodeDpt1(byte[] value)
    {
        if (value.Length == 0) return "Invalid";
        var bit = (value[0] & 0x01) == 1;
        return bit ? "On" : "Off";
    }

    /// <summary>DPT 2.xxx - 1-bit controlled (with priority)</summary>
    private static string DecodeDpt2(byte[] value)
    {
        if (value.Length == 0) return "Invalid";
        var control = (value[0] & 0x02) == 0x02;
        var state = (value[0] & 0x01) == 0x01;
        return $"{(state ? "On" : "Off")} (Control: {control})";
    }

    /// <summary>DPT 3.xxx - 3-bit controlled (dimming, blinds)</summary>
    private static string DecodeDpt3(byte[] value)
    {
        if (value.Length == 0) return "Invalid";
        var control = (value[0] & 0x08) == 0x08;
        var stepCode = value[0] & 0x07;
        return $"{(control ? "Increase" : "Decrease")} (Steps: {stepCode})";
    }

    /// <summary>DPT 5.xxx - 8-bit unsigned (0-255)</summary>
    private static string DecodeDpt5(byte[] value)
    {
        if (value.Length == 0) return "Invalid";
        var val = value[0];
        // Common sub-types
        // 5.001: Percentage (0-100%)
        // 5.003: Angle (0-360°)
        // 5.004: Percentage (0-255 mapped to 0-100%)
        return $"{val} ({(val * 100.0 / 255.0):F1}%)";
    }

    /// <summary>DPT 6.xxx - 8-bit signed (-128 to 127)</summary>
    private static string DecodeDpt6(byte[] value)
    {
        if (value.Length == 0) return "Invalid";
        var val = (sbyte)value[0];
        return $"{val}";
    }

    /// <summary>DPT 7.xxx - 16-bit unsigned (0-65535)</summary>
    private static string DecodeDpt7(byte[] value)
    {
        if (value.Length < 2) return "Invalid";
        var val = (ushort)((value[0] << 8) | value[1]);
        return $"{val}";
    }

    /// <summary>DPT 8.xxx - 16-bit signed (-32768 to 32767)</summary>
    private static string DecodeDpt8(byte[] value)
    {
        if (value.Length < 2) return "Invalid";
        var val = (short)((value[0] << 8) | value[1]);
        return $"{val}";
    }

    /// <summary>DPT 9.xxx - 16-bit float (2-byte float)</summary>
    private static string DecodeDpt9(byte[] value)
    {
        if (value.Length < 2) return "Invalid";

        var rawValue = (value[0] << 8) | value[1];
        var sign = (rawValue & 0x8000) >> 15;
        var exponent = (rawValue & 0x7800) >> 11;
        var mantissa = rawValue & 0x07FF;

        if (sign == 1)
        {
            mantissa = -(~(mantissa - 1) & 0x07FF);
        }

        var floatValue = (0.01 * mantissa) * Math.Pow(2, exponent);
        return $"{floatValue:F2}";
    }

    /// <summary>DPT 10.xxx - Time (3 bytes: day, hour, minute, second)</summary>
    private static string DecodeDpt10(byte[] value)
    {
        if (value.Length < 3) return "Invalid";

        var day = (value[0] >> 5) & 0x07; // 0 = no day, 1 = Monday, ..., 7 = Sunday
        var hour = value[0] & 0x1F;
        var minute = value[1] & 0x3F;
        var second = value[2] & 0x3F;

        var dayStr = day switch
        {
            0 => "",
            1 => "Mon",
            2 => "Tue",
            3 => "Wed",
            4 => "Thu",
            5 => "Fri",
            6 => "Sat",
            7 => "Sun",
            _ => ""
        };

        return $"{dayStr} {hour:D2}:{minute:D2}:{second:D2}".Trim();
    }

    /// <summary>DPT 11.xxx - Date (3 bytes: day, month, year)</summary>
    private static string DecodeDpt11(byte[] value)
    {
        if (value.Length < 3) return "Invalid";

        var day = value[0] & 0x1F;
        var month = value[1] & 0x0F;
        var year = value[2] & 0x7F;

        // Year is offset from 1900 (if < 90) or 2000 (if >= 90)
        var fullYear = year < 90 ? 2000 + year : 1900 + year;

        return $"{day:D2}.{month:D2}.{fullYear}";
    }

    /// <summary>DPT 12.xxx - 32-bit unsigned (0-4294967295)</summary>
    private static string DecodeDpt12(byte[] value)
    {
        if (value.Length < 4) return "Invalid";
        var val = (uint)((value[0] << 24) | (value[1] << 16) | (value[2] << 8) | value[3]);
        return $"{val}";
    }

    /// <summary>DPT 13.xxx - 32-bit signed (-2147483648 to 2147483647)</summary>
    private static string DecodeDpt13(byte[] value)
    {
        if (value.Length < 4) return "Invalid";
        var val = (value[0] << 24) | (value[1] << 16) | (value[2] << 8) | value[3];
        return $"{val}";
    }

    /// <summary>DPT 14.xxx - 32-bit float (IEEE 754)</summary>
    private static string DecodeDpt14(byte[] value)
    {
        if (value.Length < 4) return "Invalid";
        var floatValue = BitConverter.ToSingle(new[] { value[3], value[2], value[1], value[0] }, 0);
        return $"{floatValue:F2}";
    }

    /// <summary>DPT 16.xxx - String (14 characters, ASCII)</summary>
    private static string DecodeDpt16(byte[] value)
    {
        if (value.Length == 0) return string.Empty;

        try
        {
            // DPT 16 is ASCII or ISO-8859-1 encoded
            var text = Encoding.ASCII.GetString(value).TrimEnd('\0');
            return $"\"{text}\"";
        }
        catch
        {
            return ConvertToHex(value);
        }
    }

    /// <summary>DPT 17.xxx - Scene number (0-63)</summary>
    private static string DecodeDpt17(byte[] value)
    {
        if (value.Length == 0) return "Invalid";
        var sceneNumber = value[0] & 0x3F;
        return $"Scene {sceneNumber}";
    }

    /// <summary>DPT 18.xxx - Scene control (learn/activate)</summary>
    private static string DecodeDpt18(byte[] value)
    {
        if (value.Length == 0) return "Invalid";
        var control = (value[0] & 0x80) == 0x80;
        var sceneNumber = value[0] & 0x3F;
        return $"Scene {sceneNumber} ({(control ? "Learn" : "Activate")})";
    }

    /// <summary>DPT 19.xxx - Date time (8 bytes)</summary>
    private static string DecodeDpt19(byte[] value)
    {
        if (value.Length < 8) return "Invalid";

        try
        {
            var year = value[0];
            var month = value[1] & 0x0F;
            var day = value[2] & 0x1F;
            var dayOfWeek = (value[3] >> 5) & 0x07;
            var hour = value[3] & 0x1F;
            var minute = value[4] & 0x3F;
            var second = value[5] & 0x3F;

            var fullYear = year < 90 ? 2000 + year : 1900 + year;

            return $"{day:D2}.{month:D2}.{fullYear} {hour:D2}:{minute:D2}:{second:D2}";
        }
        catch
        {
            return ConvertToHex(value);
        }
    }

    /// <summary>DPT 20.xxx - 8-bit enumeration</summary>
    private static string DecodeDpt20(byte[] value)
    {
        if (value.Length == 0) return "Invalid";
        // Return the enum value (interpretation depends on sub-type)
        return $"Enum {value[0]}";
    }

    #endregion

    private static string ConvertToHex(byte[] value)
    {
        return "0x" + Convert.ToHexString(value);
    }
}
