using System.Text;
using KnxMonitor.ProjectParser.Core.Models;

namespace KnxMonitor.ParserTool.Formatters;

public class ConsoleFormatter : IOutputFormatter
{
    public string FormatParseResult(ParseResult result)
    {
        var sb = new StringBuilder();

        var hasData = (result.GroupAddresses?.Any() == true) || (result.Devices?.Any() == true);

        if (!hasData)
        {
            sb.AppendLine("Parse failed: No data found");
            return sb.ToString();
        }

        sb.AppendLine("Parse successful!");
        sb.AppendLine();

        // Features
        if (result.Features != null)
        {
            sb.AppendLine("Project Features:");
            sb.AppendLine($"  Project ID:    {result.Features.ProjectId}");
            sb.AppendLine($"  ETS Version:   {result.Features.EtsVersion}");
            sb.AppendLine($"  Password:      {(result.Features.HasPassword ? "Yes" : "No")}");
            sb.AppendLine($"  KNX Secure:    {(result.Features.HasKnxSecure ? "Yes" : "No")}");
            sb.AppendLine($"  Addressing:    {result.Features.AddressingStyle}");
            sb.AppendLine();
        }

        // Statistics
        if (result.Statistics != null)
        {
            sb.AppendLine("Statistics:");
            sb.AppendLine($"  Duration:      {result.Statistics.Duration.TotalMilliseconds:F1} ms");
            sb.AppendLine($"  Group Addrs:   {result.Statistics.GroupAddressCount}");
            sb.AppendLine($"  Devices:       {result.Statistics.DeviceCount}");
            if (result.Statistics.Warnings.Any())
            {
                sb.AppendLine($"  Warnings:      {result.Statistics.Warnings.Count}");
            }
            sb.AppendLine();
        }

        // Group Addresses Summary
        if (result.GroupAddresses?.Any() == true)
        {
            sb.AppendLine($"Group Addresses ({result.GroupAddresses.Count}):");
            var sample = result.GroupAddresses.Take(5);
            foreach (var ga in sample)
            {
                var dpt = ga.DatapointType?.OriginalString ?? "unknown";
                var secure = ga.DataSecure ? " [SECURE]" : "";
                sb.AppendLine($"  {ga.Address,-12} {ga.Name,-30} (DPT {dpt}){secure}");
            }
            if (result.GroupAddresses.Count > 5)
            {
                sb.AppendLine($"  ... and {result.GroupAddresses.Count - 5} more");
            }
            sb.AppendLine();
        }

        // Devices Summary
        if (result.Devices?.Any() == true)
        {
            sb.AppendLine($"Devices ({result.Devices.Count}):");
            var sample = result.Devices.Take(5);
            foreach (var device in sample)
            {
                sb.AppendLine($"  {device.PhysicalAddress,-8} {device.Name,-30} ({device.Manufacturer})");
            }
            if (result.Devices.Count > 5)
            {
                sb.AppendLine($"  ... and {result.Devices.Count - 5} more");
            }
        }

        return sb.ToString();
    }

    public string FormatFeatures(ProjectFeatures features)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Project Features:");
        sb.AppendLine($"  Project ID:    {features.ProjectId}");
        sb.AppendLine($"  ETS Version:   {features.EtsVersion}");
        sb.AppendLine($"  Password:      {(features.HasPassword ? "Yes" : "No")}");
        sb.AppendLine($"  KNX Secure:    {(features.HasKnxSecure ? "Yes" : "No")}");
        sb.AppendLine($"  Addressing:    {features.AddressingStyle}");
        return sb.ToString();
    }

    public string FormatError(string message, Exception? exception = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Error: {message}");
        if (exception != null)
        {
            sb.AppendLine($"  Exception: {exception.Message}");
            if (!string.IsNullOrEmpty(exception.StackTrace))
            {
                sb.AppendLine($"  Stack Trace:");
                sb.AppendLine($"    {exception.StackTrace}");
            }
        }
        return sb.ToString();
    }
}
