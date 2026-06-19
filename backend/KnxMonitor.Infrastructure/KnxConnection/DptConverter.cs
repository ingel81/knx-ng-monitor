using System.Globalization;
using System.Text.RegularExpressions;
using Knx.Falcon;
using Knx.Falcon.ApplicationData.DatapointTypes;

namespace KnxMonitor.Infrastructure.KnxConnection;

/// <summary>
/// Decodes raw KNX group values into human-readable strings using the Falcon SDK's
/// built-in datapoint-type catalog (full DPT range, sub-type aware, with units and
/// enumeration names). Falls back to a hex representation for unknown/unparsable types.
/// </summary>
/// <remarks>
/// We pass the original <see cref="GroupValue"/> from the bus straight to Falcon's
/// formatter. This preserves the correct bit-size for short telegrams (DPT 1/2/3 are
/// ≤6-bit values embedded in the APCI) — reconstructing a <see cref="GroupValue"/> from
/// a full byte would make Falcon reject it ("Expected 1 bit, but value has 8 bit").
///
/// Formatting uses <see cref="CultureInfo.InvariantCulture"/> so the persisted decoded
/// string is stable (e.g. "21.5 °C"). Per-UI-language number formatting of historical
/// values would require storing the typed value separately — out of scope here.
/// </remarks>
public static partial class DptConverter
{
    private static readonly DptFactory Factory = DptFactory.Default;

    // Falcon's enumerated DPT labels (On/Off, Open/Close, Active/Inactive …) follow the
    // thread's UI culture. On a German host that yields German text ("Aus"/"Inaktiv"),
    // which clashes with the English UI. Pin formatting to English so labels are stable
    // and language-consistent regardless of host locale.
    private static readonly CultureInfo FormatUiCulture = CultureInfo.GetCultureInfo("en");

    /// <summary>
    /// Wired to an ILogger by the connection service so decode failures (e.g. a missing
    /// Falcon master-data dependency under single-file publish) are visible instead of
    /// silently falling back to hex.
    /// </summary>
    public static Action<string?, Exception>? OnError;

    /// <summary>
    /// Decode a group value based on its DPT.
    /// </summary>
    /// <param name="dptType">DPT identifier in any common form ("9.001", "DPST-9-1", "DPT-9", "9").</param>
    /// <param name="value">The group value as received from the bus (carries the correct bit-size).</param>
    public static string Decode(string? dptType, GroupValue? value)
    {
        if (value is null)
            return string.Empty;

        if (string.IsNullOrWhiteSpace(dptType) || !TryParseDpt(dptType, out var main, out var sub))
            return ToHex(value.Value);

        try
        {
            DptBase? dpt = null;
            if (sub.HasValue)
            {
                try { dpt = Factory.Get(main, sub.Value); }
                catch { /* unknown sub-type -> fall back to the main type's default */ }
            }
            dpt ??= Factory.Create(Factory.GetDatapointType(main));

            // Pin UI culture to English for the enum-label lookup; numbers stay invariant
            // (dot decimal) via the IFormatProvider argument.
            var prevUi = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentUICulture = FormatUiCulture;
            try
            {
                return dpt.Format(value, null, CultureInfo.InvariantCulture);
            }
            finally
            {
                CultureInfo.CurrentUICulture = prevUi;
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke(dptType, ex);
            return ToHex(value.Value);
        }
    }

    /// <summary>
    /// Encodes a user-entered value into a <see cref="GroupValue"/> for sending on the bus,
    /// the inverse of <see cref="Decode"/>. Uses Falcon's <c>DptBase.ToGroupValue</c>.
    /// </summary>
    /// <param name="dptType">DPT identifier ("1.001", "9.001", "5", …). Required for typed encoding.</param>
    /// <param name="value">The value as entered by the user (e.g. "1"/"true"/"on", "21.5", "60").</param>
    /// <returns>The encoded group value, or null if it cannot be encoded for the given DPT.</returns>
    /// <remarks>
    /// ⚠ This path writes to a real KNX bus. Per-DPT numeric typing is best-effort: DPT-1 maps
    /// to a boolean; integer-family DPTs (5/6/7/8/12/13) to a long; float-family (9/14) to a
    /// double; everything else is handed to Falcon as the trimmed string. Verify on hardware.
    /// </remarks>
    public static GroupValue? Encode(string? dptType, string value)
    {
        if (value is null)
            return null;

        if (string.IsNullOrWhiteSpace(dptType) || !TryParseDpt(dptType, out var main, out var sub))
        {
            // No DPT: accept a raw hex byte string ("0x01", "01 02") as a last resort.
            return TryParseHex(value, out var bytes) ? new GroupValue(bytes) : null;
        }

        try
        {
            DptBase? dpt = null;
            if (sub.HasValue)
            {
                try { dpt = Factory.Get(main, sub.Value); }
                catch { /* unknown sub-type -> fall back to the main type's default */ }
            }
            dpt ??= Factory.Create(Factory.GetDatapointType(main));

            object typed = CoerceValue(main, value.Trim());
            return dpt.ToGroupValue(typed);
        }
        catch (Exception ex)
        {
            OnError?.Invoke(dptType, ex);
            return null;
        }
    }

    /// <summary>Parses a user string into the CLR type Falcon expects for the DPT main number.</summary>
    private static object CoerceValue(int main, string value)
    {
        // DPT-1: boolean (1-bit). Accept common truthy/falsy spellings.
        if (main == 1)
        {
            if (bool.TryParse(value, out var b)) return b;
            return value is "1" or "on" or "On" or "ON" or "true" or "yes" or "Yes";
        }

        // Float-family DPTs -> double.
        if (main is 9 or 14)
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return d;
        }
        // Integer-family DPTs -> long (Falcon narrows to the DPT's actual width).
        else if (main is 5 or 6 or 7 or 8 or 12 or 13)
        {
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                return l;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return (long)d;
        }

        // Fallback: hand the raw string to Falcon (covers strings, scenes, enums by number).
        return value;
    }

    private static bool TryParseHex(string value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        var cleaned = value.Trim();
        if (cleaned.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[2..];
        cleaned = cleaned.Replace(" ", string.Empty).Replace("-", string.Empty);
        if (cleaned.Length == 0 || cleaned.Length % 2 != 0)
            return false;
        try { bytes = Convert.FromHexString(cleaned); return true; }
        catch { return false; }
    }

    /// <summary>Extracts main (and optional sub) number from any common DPT string form.</summary>
    private static bool TryParseDpt(string dptType, out int main, out int? sub)
    {
        main = 0;
        sub = null;

        var numbers = DigitRun().Matches(dptType);
        if (numbers.Count == 0 || !int.TryParse(numbers[0].Value, out main))
            return false;

        if (numbers.Count > 1 && int.TryParse(numbers[1].Value, out var s))
            sub = s;

        return true;
    }

    private static string ToHex(byte[]? bytes)
        => bytes is { Length: > 0 } ? "0x" + Convert.ToHexString(bytes) : string.Empty;

    [GeneratedRegex(@"\d+")]
    private static partial Regex DigitRun();
}
