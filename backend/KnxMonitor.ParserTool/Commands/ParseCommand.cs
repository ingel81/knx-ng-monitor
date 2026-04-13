using System.CommandLine;
using System.CommandLine.Invocation;
using KnxMonitor.ProjectParser.Core.Interfaces;
using KnxMonitor.ProjectParser.Core.Models;
using KnxMonitor.ParserTool.Formatters;
using Microsoft.Extensions.Logging;

namespace KnxMonitor.ParserTool.Commands;

public class ParseCommand : Command
{
    public ParseCommand() : base("parse", "Parse a .knxproj file and display results")
    {
        var fileArgument = new Argument<FileInfo>(
            "file",
            "Path to the .knxproj file to parse"
        ).ExistingOnly();

        var passwordOption = new Option<string?>(
            aliases: new[] { "--password", "-p" },
            description: "Password for encrypted projects"
        );

        var keyringOption = new Option<FileInfo?>(
            aliases: new[] { "--keyring", "-k" },
            description: "Path to a .knxkeys file (KNX Secure)"
        ).ExistingOnly();

        var keyringPasswordOption = new Option<string?>(
            aliases: new[] { "--keyring-password", "-K" },
            description: "Password for the keyring (.knxkeys) file"
        );

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
        AddOption(passwordOption);
        AddOption(keyringOption);
        AddOption(keyringPasswordOption);
        AddOption(formatOption);
        AddOption(verboseOption);

        this.SetHandler(async (InvocationContext context) =>
        {
            var file = context.ParseResult.GetValueForArgument(fileArgument);
            var password = context.ParseResult.GetValueForOption(passwordOption);
            var keyring = context.ParseResult.GetValueForOption(keyringOption);
            var keyringPassword = context.ParseResult.GetValueForOption(keyringPasswordOption);
            var format = context.ParseResult.GetValueForOption(formatOption) ?? "console";
            var verbose = context.ParseResult.GetValueForOption(verboseOption);

            var exitCode = await ExecuteAsync(file, password, keyring, keyringPassword, format, verbose, context.GetCancellationToken());
            context.ExitCode = exitCode;
        });
    }

    private static async Task<int> ExecuteAsync(
        FileInfo file,
        string? password,
        FileInfo? keyring,
        string? keyringPassword,
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
            // Create parser services
            var featureDetector = new ProjectParser.Services.FeatureDetector(
                loggerFactory.CreateLogger<ProjectParser.Services.FeatureDetector>()
            );

            var ets4Loader = new ProjectParser.Loaders.Ets4ProjectLoader(
                loggerFactory.CreateLogger<ProjectParser.Loaders.Ets4ProjectLoader>()
            );

            var ets5Loader = new ProjectParser.Loaders.Ets5ProjectLoader(
                loggerFactory.CreateLogger<ProjectParser.Loaders.Ets5ProjectLoader>()
            );

            var ets6Loader = new ProjectParser.Loaders.Ets6ProjectLoader(
                loggerFactory.CreateLogger<ProjectParser.Loaders.Ets6ProjectLoader>()
            );

            var keyringReader = new ProjectParser.Services.KeyringReader(
                loggerFactory.CreateLogger<ProjectParser.Services.KeyringReader>()
            );

            var parser = new ProjectParser.Services.ProjectParser(
                new IProjectLoader[] { ets4Loader, ets5Loader, ets6Loader },
                featureDetector,
                keyringReader,
                loggerFactory.CreateLogger<ProjectParser.Services.ProjectParser>()
            );

            // Parse file
            await using var fileStream = file.OpenRead();
            await using var keyringStream = keyring?.OpenRead();

            var options = new ParserOptions
            {
                Password = password,
                KeyringStream = keyringStream,
                KeyringPassword = keyringPassword
            };

            var progress = new Progress<ParserProgress>(p =>
            {
                if (verbose)
                {
                    Console.Error.WriteLine($"[{p.Step}] {p.Message} ({p.PercentComplete}%)");
                }
            });

            var result = await parser.ParseAsync(fileStream, options, progress, cancellationToken);

            // Output result
            var output = formatter.FormatParseResult(result);
            Console.WriteLine(output);

            if (result.Keyring != null)
            {
                Console.WriteLine();
                Console.WriteLine($"Keyring: {result.Keyring.Devices.Count} device(s), {result.Keyring.GroupAddresses.Count} GA key(s), backbone={(result.Keyring.BackboneKey != null ? "yes" : "no")}");
            }

            var hasData = (result.GroupAddresses?.Any() == true) || (result.Devices?.Any() == true);
            return hasData ? 0 : 1;
        }
        catch (Exception ex)
        {
            var output = formatter.FormatError("Failed to parse file", ex);
            Console.Error.WriteLine(output);
            return 1;
        }
    }
}
