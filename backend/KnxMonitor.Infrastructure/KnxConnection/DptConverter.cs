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
