using System.IO.Compression;
using System.Text;

namespace KnxMonitor.ProjectParser.Tests.Fixtures;

internal static class TestZipBuilder
{
    public static MemoryStream BuildOuter(IEnumerable<(string Path, string Content)> entries)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }
        ms.Position = 0;
        return ms;
    }

    public static MemoryStream BuildOuterBinary(IEnumerable<(string Path, byte[] Data)> entries)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, data) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var s = entry.Open();
                s.Write(data, 0, data.Length);
            }
        }
        ms.Position = 0;
        return ms;
    }
}
