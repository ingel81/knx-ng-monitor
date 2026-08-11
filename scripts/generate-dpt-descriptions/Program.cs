// Generates frontend/src/app/shared/grid/dpt-descriptions.ts from the KNX master
// data embedded in the Falcon SDK (official KNX Association knx_master.xml).
// Run from repo root:  dotnet run --project scripts/generate-dpt-descriptions
// The embedded master data only carries English and German texts; all other
// languages fall back to English inside Falcon, so we export exactly these two.

using System.Text;
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

File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
Console.WriteLine($"Wrote {mains} main types + {subs} subtypes to {outPath}");

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
