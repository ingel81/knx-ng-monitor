using System.Text;
using KnxMonitor.ProjectParser.Core.Models;

namespace KnxMonitor.ParserTool.Formatters;

public class CsvFormatter : IOutputFormatter
{
    public string FormatParseResult(ParseResult result)
    {
        var sb = new StringBuilder();

        // Group Addresses
        if (result.GroupAddresses?.Any() == true)
        {
            sb.AppendLine("# Group Addresses");
            sb.AppendLine("Address,Name,Description,DPT,DataSecure");
            foreach (var ga in result.GroupAddresses)
            {
                sb.AppendLine($"{EscapeCsv(ga.Address)},{EscapeCsv(ga.Name)},{EscapeCsv(ga.Description)},{EscapeCsv(ga.DatapointType?.OriginalString)},{ga.DataSecure}");
            }
            sb.AppendLine();
        }

        // Devices
        if (result.Devices?.Any() == true)
        {
            sb.AppendLine("# Devices");
            sb.AppendLine("Address,Name,Manufacturer,Product");
            foreach (var device in result.Devices)
            {
                sb.AppendLine($"{EscapeCsv(device.PhysicalAddress)},{EscapeCsv(device.Name)},{EscapeCsv(device.Manufacturer)},{EscapeCsv(device.ProductName)}");
            }
        }

        return sb.ToString();
    }

    public string FormatFeatures(ProjectFeatures features)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Feature,Value");
        sb.AppendLine($"ProjectId,{EscapeCsv(features.ProjectId)}");
        sb.AppendLine($"EtsVersion,{features.EtsVersion}");
        sb.AppendLine($"HasPassword,{features.HasPassword}");
        sb.AppendLine($"HasKnxSecure,{features.HasKnxSecure}");
        return sb.ToString();
    }

    public string FormatError(string message, Exception? exception = null)
    {
        return $"Error,{EscapeCsv(message)},{EscapeCsv(exception?.Message)}";
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }
}
