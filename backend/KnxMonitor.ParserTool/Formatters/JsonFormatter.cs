using System.Text.Json;
using System.Text.Json.Serialization;
using KnxMonitor.ProjectParser.Core.Models;

namespace KnxMonitor.ParserTool.Formatters;

public class JsonFormatter : IOutputFormatter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string FormatParseResult(ParseResult result)
    {
        var hasData = (result.GroupAddresses?.Any() == true) || (result.Devices?.Any() == true);

        var output = new
        {
            success = hasData,
            features = result.Features,
            groupAddresses = result.GroupAddresses?.Select(ga => new
            {
                address = ga.Address,
                name = ga.Name,
                description = ga.Description,
                dpt = ga.DatapointType?.OriginalString,
                dataSecure = ga.DataSecure
            }),
            devices = result.Devices?.Select(d => new
            {
                address = d.PhysicalAddress,
                name = d.Name,
                manufacturer = d.Manufacturer,
                productName = d.ProductName
            }),
            groupRanges = result.GroupRanges?.Select(r => new
            {
                name = r.Name,
                rangeStart = r.RangeStart,
                rangeEnd = r.RangeEnd
            }),
            communicationObjects = result.CommunicationObjects?.Select(c => new
            {
                deviceAddress = c.DeviceAddress,
                number = c.Number,
                name = c.Name,
                functionText = c.FunctionText,
                groupAddressLinks = c.GroupAddressLinks,
                datapointType = c.DatapointType,
                flags = c.Flags
            }),
            statistics = result.Statistics != null ? new
            {
                duration = result.Statistics.Duration.TotalMilliseconds,
                groupAddressCount = result.Statistics.GroupAddressCount,
                deviceCount = result.Statistics.DeviceCount,
                warnings = result.Statistics.Warnings
            } : null
        };

        return JsonSerializer.Serialize(output, Options);
    }

    public string FormatFeatures(ProjectFeatures features)
    {
        return JsonSerializer.Serialize(features, Options);
    }

    public string FormatError(string message, Exception? exception = null)
    {
        var error = new
        {
            success = false,
            error = message,
            exception = exception?.Message,
            stackTrace = exception?.StackTrace
        };

        return JsonSerializer.Serialize(error, Options);
    }
}
