using System.Text.RegularExpressions;

namespace KnxMonitor.ProjectParser.Core.Models;

public class DptInfo
{
    private static readonly Regex DptRegex = new Regex(
        @"(?:DPST|DPT)-(\d+)(?:-0*(\d+))?",
        RegexOptions.Compiled
    );

    public int Main { get; set; }
    public int? Sub { get; set; }
    public string OriginalString { get; set; } = string.Empty;

    public override string ToString() =>
        Sub.HasValue ? $"DPT {Main}.{Sub:D3}" : $"DPT {Main}";

    /// <summary>
    /// The canonical ETS id of what was actually understood: <c>DPST-9-1</c> or <c>DPT-9</c>.
    /// This is the form to STORE and to hand to a decoder — <see cref="OriginalString"/> keeps the
    /// raw attribute, which may be a whole LIST of ids (<c>"DPT-9 DPST-9-1 DPST-9-2 …"</c>) that a
    /// naive "first two numbers" reader turns into the wrong subtype. A value that parsed to nothing
    /// falls back to the raw string, so an exotic type is passed on rather than dropped.
    /// </summary>
    public string ToDptId() => Main <= 0
        ? OriginalString
        : Sub.HasValue ? $"DPST-{Main}-{Sub.Value}" : $"DPT-{Main}";

    /// <summary>
    /// Parse a <c>DatapointType</c> attribute value. The XSD types that attribute as
    /// <c>knx:IDREFS</c>, so it is a whitespace-separated LIST of ids and ETS really writes several.
    /// The list means two different things depending on how many subtypes it names:
    /// <list type="bullet">
    ///   <item><c>"DPT-5 DPST-5-10"</c> — a main type plus THE subtype. Reading only the first token
    ///         reported DPT 5 and threw the subtype away.</item>
    ///   <item><c>"DPT-9 DPST-9-1 DPST-9-2 … DPST-9-31"</c> — a main type plus every subtype the
    ///         object ACCEPTS. Picking one of them would be a guess, so only the main type survives.</item>
    /// </list>
    /// Returns <c>null</c> for null/empty/whitespace input, and a DptInfo carrying only
    /// <see cref="OriginalString"/> when nothing in the value looks like a datapoint id.
    /// </summary>
    public static DptInfo? TryParse(string? dptString)
    {
        if (string.IsNullOrWhiteSpace(dptString))
            return null;

        var mains = new List<int>();
        var subtyped = new List<(int Main, int Sub)>();

        foreach (Match match in DptRegex.Matches(dptString))
        {
            var main = int.Parse(match.Groups[1].Value);
            mains.Add(main);

            if (match.Groups[2].Success)
            {
                var sub = int.Parse(match.Groups[2].Value);
                if (!subtyped.Contains((main, sub))) subtyped.Add((main, sub));
            }
        }

        if (mains.Count == 0)
        {
            // Return with only OriginalString for unparseable formats
            return new DptInfo { OriginalString = dptString };
        }

        // Exactly one subtype named: that is the most specific statement the value makes.
        if (subtyped.Count == 1)
        {
            return new DptInfo
            {
                Main = subtyped[0].Main,
                Sub = subtyped[0].Sub,
                OriginalString = dptString
            };
        }

        // Several (or no) subtypes: the main type is all that is certain. When even the main types
        // disagree the value is malformed and the first id — the one ETS writes first — stands.
        var distinctMains = mains.Distinct().ToList();

        return new DptInfo
        {
            Main = distinctMains.Count == 1 ? distinctMains[0] : mains[0],
            Sub = null,
            OriginalString = dptString
        };
    }
}
