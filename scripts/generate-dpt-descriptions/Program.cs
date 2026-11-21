// Generates frontend/src/app/shared/grid/dpt-descriptions.ts from the KNX master
// data embedded in the Falcon SDK (official KNX Association knx_master.xml).
// Run from repo root:  dotnet run --project scripts/generate-dpt-descriptions
// The embedded master data only carries English and German texts; all other
// languages fall back to English inside Falcon, so we export exactly these two.

using System.Globalization;
using System.Text;
using Knx.Falcon;
using Knx.Falcon.ApplicationData.DatapointTypes;

var outPath = args.Length > 0
    ? args[0]
    : Path.Combine(FindRepoRoot(), "frontend", "src", "app", "shared", "grid", "dpt-descriptions.ts");

var factory = DptFactory.Default;
var sb = new StringBuilder();

sb.AppendLine("// AUTO-GENERATED — do not edit by hand.");
sb.AppendLine("// Source: KNX master data embedded in Knx.Falcon.Sdk (official KNX Association knx_master.xml).");
sb.AppendLine("// Regenerate with:  dotnet run --project scripts/generate-dpt-descriptions");
sb.AppendLine();
sb.AppendLine("export interface DptDescription {");
sb.AppendLine("  /** Official DPT name, e.g. \"DPT_Switch\" */");
sb.AppendLine("  n: string;");
sb.AppendLine("  en: string;");
sb.AppendLine("  de: string;");
sb.AppendLine("}");
sb.AppendLine();
sb.AppendLine("/** Keys: \"1\" (main type) and \"1.001\" (subtype). */");
sb.AppendLine("export const DPT_DESCRIPTIONS: Record<string, DptDescription> = {");

var mains = 0;
var subs = 0;
foreach (var dpt in factory.AllDatapointTypes.OrderBy(d => d.MainTypeNumber))
{
    Append(sb, dpt.MainTypeNumber.ToString(), dpt.Name, dpt.GetTranslatedText("en"), dpt.GetTranslatedText("de"));
    mains++;
    foreach (var sub in dpt.SubTypes.OrderBy(s => s.SubTypeNumber))
    {
        Append(sb, $"{dpt.MainTypeNumber}.{sub.SubTypeNumber:D3}", sub.Name, sub.GetTranslatedText("en"), sub.GetTranslatedText("de"));
        subs++;
    }
}

sb.AppendLine("};");
sb.AppendLine();

// Value labels of the 1-bit subtypes ("Up"/"Down" for 1.008, …) as Falcon formats them,
// so the write dropdown offers the same words the decoded value column shows.
sb.AppendLine("export interface Dpt1Labels {");
sb.AppendLine("  /** [label for 0, label for 1] */");
sb.AppendLine("  en: [string, string];");
sb.AppendLine("  de: [string, string];");
sb.AppendLine("}");
sb.AppendLine();
sb.AppendLine("/** Keys: \"1.001\" (1-bit subtypes only). */");
sb.AppendLine("export const DPT1_LABELS: Record<string, Dpt1Labels> = {");
var labels = 0;
foreach (var sub in factory.AllDatapointTypes.Where(d => d.MainTypeNumber == 1).SelectMany(d => d.SubTypes).OrderBy(s => s.SubTypeNumber))
{
    var dpt = factory.Get(1, sub.SubTypeNumber);
    string Label(bool v, string culture)
    {
        var prev = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try { return dpt.Format(new GroupValue(v), null, CultureInfo.InvariantCulture); }
        finally { CultureInfo.CurrentUICulture = prev; }
    }
    sb.AppendLine($"  '1.{sub.SubTypeNumber:D3}': {{ en: [{Q(Label(false, "en"))}, {Q(Label(true, "en"))}], de: [{Q(Label(false, "de"))}, {Q(Label(true, "de"))}] }},");
    labels++;
}
sb.AppendLine("};");

File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
Console.WriteLine($"Wrote {mains} main types + {subs} subtypes + {labels} 1-bit label pairs to {outPath}");

static void Append(StringBuilder sb, string? key, string? name, string? en, string? de) =>
    sb.AppendLine($"  '{key}': {{ n: {Q(name)}, en: {Q(en)}, de: {Q(de)} }},");

static string Q(string? s) => "'" + (s ?? "")
    .Replace("\\", "\\\\").Replace("'", "\\'")
    .Replace("\r", "\\r").Replace("\n", "\\n")
    .Replace("\u2028", "\\u2028").Replace("\u2029", "\\u2029") + "'";

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(Environment.CurrentDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Dockerfile")))
        dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("Repo root not found — pass the output path as argument.");
}
