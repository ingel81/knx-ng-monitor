namespace KnxMonitor.ProjectParser.Tests.Fixtures;

internal static class TestSamples
{
    public static string Path(string fileName) =>
        System.IO.Path.Combine("TestData", fileName);

    public static bool Exists(string fileName) =>
        File.Exists(Path(fileName));
}
