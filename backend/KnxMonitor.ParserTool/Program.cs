using System.CommandLine;
using KnxMonitor.ParserTool.Commands;

namespace KnxMonitor.ParserTool;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("KNX Project Parser CLI Tool")
        {
            new ParseCommand(),
            new DetectCommand()
        };

        rootCommand.Description = @"
KNX Project Parser - Command Line Tool

Parse and analyze ETS .knxproj files from the command line.

Examples:
  knx-parser parse myproject.knxproj
  knx-parser parse myproject.knxproj --password secret123
  knx-parser parse myproject.knxproj --format json > output.json
  knx-parser detect myproject.knxproj
";

        return await rootCommand.InvokeAsync(args);
    }
}
