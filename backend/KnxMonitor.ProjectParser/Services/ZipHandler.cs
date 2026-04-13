using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using SharpZipFile = ICSharpCode.SharpZipLib.Zip.ZipFile;
using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Core.Models;

namespace KnxMonitor.ProjectParser.Services;

public static class ZipHandler
{
    public static async Task<ProjectFileMap> LoadAsync(
        Stream stream,
        ProjectFeatures features,
        string? password,
        IProgress<ParserProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var outerArchive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        if (!features.HasPassword)
        {
            return await ExtractAllAsync(outerArchive, cancellationToken);
        }

        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException(
                "Project is password-protected but no password provided");
        }

        progress?.Report(new ParserProgress
        {
            Step = ParseStep.CheckPassword,
            PercentComplete = 0,
            Message = "Extracting password-protected archive"
        });

        var nestedZipEntry = outerArchive.Entries
            .FirstOrDefault(e => e.FullName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                                 && e.Name.StartsWith("P-", StringComparison.OrdinalIgnoreCase));

        if (nestedZipEntry == null)
        {
            throw new InvalidOperationException("Nested project archive not found");
        }

        await using var nestedStream = nestedZipEntry.Open();
        using var nestedBuffer = new MemoryStream();
        await nestedStream.CopyToAsync(nestedBuffer, cancellationToken);
        nestedBuffer.Position = 0;

        var zipPassword = features.EtsVersion == EtsVersion.Ets6
            ? DeriveEts6Password(password)
            : password;

        var files = await ExtractPasswordProtectedAsync(nestedBuffer, zipPassword, cancellationToken);

        progress?.Report(new ParserProgress
        {
            Step = ParseStep.CheckPassword,
            PercentComplete = 100,
            Message = "Archive decrypted successfully"
        });

        return new ProjectFileMap(files);
    }

    private static async Task<ProjectFileMap> ExtractAllAsync(
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            await using var entryStream = entry.Open();
            using var ms = new MemoryStream();
            await entryStream.CopyToAsync(ms, cancellationToken);
            files[entry.FullName] = ms.ToArray();
        }
        return new ProjectFileMap(files);
    }

    internal static string DeriveEts6Password(string plainPassword)
    {
        // ETS6 zip password derivation (matches xknxproject):
        //   PBKDF2-HMAC-SHA256(password=UTF16-LE, salt="21.project.ets.knx.org", iter=65536, keylen=32)
        //   -> Base64
        var passwordBytes = Encoding.Unicode.GetBytes(plainPassword);
        var salt = Encoding.UTF8.GetBytes("21.project.ets.knx.org");
        using var pbkdf2 = new Rfc2898DeriveBytes(passwordBytes, salt, 65536, HashAlgorithmName.SHA256);
        var hash = pbkdf2.GetBytes(32);
        return Convert.ToBase64String(hash);
    }

    private static async Task<Dictionary<string, byte[]>> ExtractPasswordProtectedAsync(
        Stream seekableSource,
        string password,
        CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using var zipFile = new SharpZipFile(seekableSource) { Password = password, IsStreamOwner = false };

        foreach (ZipEntry entry in zipFile)
        {
            if (!entry.IsFile) continue;
            var entryName = entry.Name.Replace('\\', '/');
            await using var entryStream = zipFile.GetInputStream(entry);
            using var ms = new MemoryStream();
            await entryStream.CopyToAsync(ms, cancellationToken);
            files[entryName] = ms.ToArray();
        }

        return files;
    }
}
