using KnxMonitor.ProjectParser.Core.Models;

namespace KnxMonitor.ParserTool.Formatters;

public interface IOutputFormatter
{
    /// <summary>
    /// Formats a parse result for output
    /// </summary>
    string FormatParseResult(ParseResult result);

    /// <summary>
    /// Formats project features for output
    /// </summary>
    string FormatFeatures(ProjectFeatures features);

    /// <summary>
    /// Formats an error message
    /// </summary>
    string FormatError(string message, Exception? exception = null);
}
