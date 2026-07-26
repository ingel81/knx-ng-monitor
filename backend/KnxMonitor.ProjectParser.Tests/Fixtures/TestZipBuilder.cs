using System.IO.Compression;
using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using KnxMonitor.ProjectParser.Services;

namespace KnxMonitor.ProjectParser.Tests.Fixtures;

internal static class TestZipBuilder
{
    /// <summary>
    /// Re-pack an existing plain .knxproj the way ETS stores a password-protected project: the whole
    /// P-XXXX/ folder moves into an AES-encrypted inner P-XXXX.zip whose ZIP password is the
    /// PBKDF2-derived form of the user password (ETS6 scheme). Lets a real-world sample exercise the
    /// password path without shipping a second binary fixture.
    /// <paramref name="includeMasterData"/> = false additionally drops knx_master.xml, which removes
    /// the last version marker readable before decryption.
    /// </summary>
    public static MemoryStream RepackAsPasswordProtected(
        string sourcePath,
        string userPassword,
        bool includeMasterData = true)
    {
        using var source = System.IO.Compression.ZipFile.OpenRead(sourcePath);

        var projectId = source.Entries
            .Select(e => e.FullName)
            .Where(n => n.StartsWith("P-", StringComparison.OrdinalIgnoreCase) && n.Contains('/'))
            .Select(n => n.Split('/')[0])
            .First();

        var projectPrefix = projectId + "/";

        using var inner = new MemoryStream();
        using (var zipStream = new ZipOutputStream(inner) { IsStreamOwner = false })
        {
            zipStream.Password = ZipHandler.DeriveEts6Password(userPassword);

            foreach (var entry in source.Entries.Where(e =>
                         e.FullName.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase)
                         && e.Length > 0))
            {
                var zipEntry = new ZipEntry(entry.FullName)
                {
                    DateTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    AESKeySize = 256
                };
                zipStream.PutNextEntry(zipEntry);
                using var entryStream = entry.Open();
                entryStream.CopyTo(zipStream);
                zipStream.CloseEntry();
            }
        }

        var outerEntries = new List<(string Path, byte[] Data)>();

        foreach (var entry in source.Entries.Where(e =>
                     !e.FullName.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase)
                     && e.Length > 0))
        {
            if (!includeMasterData &&
                entry.Name.Equals("knx_master.xml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var entryStream = entry.Open();
            using var buffer = new MemoryStream();
            entryStream.CopyTo(buffer);
            outerEntries.Add((entry.FullName, buffer.ToArray()));
        }

        outerEntries.Add(($"{projectId}.zip", inner.ToArray()));

        return BuildOuterBinary(outerEntries);
    }

    /// <summary>
    /// Build a .knxproj-shaped archive whose inner P-XXXX.zip is encrypted, mirroring how ETS stores
    /// password-protected projects. <paramref name="zipPassword"/> is the password as the ZIP sees it
    /// (for ETS6 that is the PBKDF2-derived Base64 string, for ETS4/5 the plain user password).
    /// </summary>
    public static MemoryStream BuildPasswordProtected(
        string projectId,
        string zipPassword,
        IEnumerable<(string Path, string Content)> innerEntries,
        IEnumerable<(string Path, string Content)>? outerEntries = null,
        int aesKeySize = 256)
    {
        var inner = new MemoryStream();
        using (var zipStream = new ZipOutputStream(inner) { IsStreamOwner = false })
        {
            zipStream.Password = zipPassword;
            foreach (var (path, content) in innerEntries)
            {
                var entry = new ZipEntry(path)
                {
                    DateTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    AESKeySize = aesKeySize
                };
                zipStream.PutNextEntry(entry);
                var bytes = Encoding.UTF8.GetBytes(content);
                zipStream.Write(bytes, 0, bytes.Length);
                zipStream.CloseEntry();
            }
        }

        var entries = new List<(string Path, byte[] Data)>
        {
            ($"{projectId}.signature", Array.Empty<byte>()),
            ($"{projectId}.zip", inner.ToArray())
        };

        foreach (var (path, content) in outerEntries ?? Array.Empty<(string, string)>())
        {
            entries.Add((path, Encoding.UTF8.GetBytes(content)));
        }

        inner.Dispose();
        return BuildOuterBinary(entries);
    }

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
