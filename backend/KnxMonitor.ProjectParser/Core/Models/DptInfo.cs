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

    public static DptInfo? TryParse(string? dptString)
    {
        if (string.IsNullOrWhiteSpace(dptString))
            return null;

        var match = DptRegex.Match(dptString);
        if (!match.Success)
        {
            // Return with only OriginalString for unparseable formats
            return new DptInfo { OriginalString = dptString };
        }

        var main = int.Parse(match.Groups[1].Value);
        var sub = match.Groups[2].Success
            ? int.Parse(match.Groups[2].Value)
            : (int?)null;

        return new DptInfo
        {
            Main = main,
            Sub = sub,
            OriginalString = dptString
        };
    }
}
