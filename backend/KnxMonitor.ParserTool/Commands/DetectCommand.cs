using System.CommandLine;
using System.CommandLine.Invocation;
using KnxMonitor.ParserTool.Formatters;
using Microsoft.Extensions.Logging;

namespace KnxMonitor.ParserTool.Commands;

public class DetectCommand : Command
{
    public DetectCommand() : base("detect", "Detect project features without full parsing")
    {
        var fileArgument = new Argument<FileInfo>(
            "file",
            "Path to the .knxproj file to analyze"
        ).ExistingOnly();

        var formatOption = new Option<string>(
            aliases: new[] { "--format", "-f" },
            getDefaultValue: () => "console",
            description: "Output format: console, json, csv"
        );

        var verboseOption = new Option<bool>(
            aliases: new[] { "--verbose", "-v" },
            description: "Enable verbose logging"
        );

        AddArgument(fileArgument);
        AddOption(formatOption);
        AddOption(verboseOption);

        this.SetHandler(async (InvocationContext context) =>
        {
            var file = context.ParseResult.GetValueForArgument(fileArgument);
            var format = context.ParseResult.GetValueForOption(formatOption) ?? "console";
            var verbose = context.ParseResult.GetValueForOption(verboseOption);

            var exitCode = await ExecuteAsync(file, format, verbose, context.GetCancellationToken());
            context.ExitCode = exitCode;
        });
    }

    private static async Task<int> ExecuteAsync(
        FileInfo file,
        string format,
        bool verbose,
        CancellationToken cancellationToken)
    {
        // Setup logging
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Warning);
        });

        // Create formatter
        IOutputFormatter formatter = format.ToLowerInvariant() switch
        {
            "json" => new JsonFormatter(),
            "csv" => new CsvFormatter(),
            _ => new ConsoleFormatter()
        };

        try
        {
            // Create feature detector
            var featureDetector = new ProjectParser.Services.FeatureDetector(
                loggerFactory.CreateLogger<ProjectParser.Services.FeatureDetector>()
            );

            // Detect features
            await using var fileStream = file.OpenRead();
            var features = await featureDetector.DetectAsync(fileStream, cancellationToken);

            // Output result
            var output = formatter.FormatFeatures(features);
            Console.WriteLine(output);

            return 0;
        }
        catch (Exception ex)
        {
            var output = formatter.FormatError("Failed to detect features", ex);
            Console.Error.WriteLine(output);
            return 1;
        }
    }
}
