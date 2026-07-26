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
            return new ProjectFileMap(await ExtractAllAsync(outerArchive, cancellationToken));
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

        var innerFiles = await ExtractWithPasswordCandidatesAsync(
            nestedBuffer, features.EtsVersion, password, cancellationToken);

        // Only the P-XXXX/ project folder is encrypted. knx_master.xml and the M-XXXX/ manufacturer
        // folders (Hardware.xml, Catalog.xml) stay in the OUTER archive — without them devices have
        // no product name and no manufacturer, so a password-protected project used to import with
        // devices named after their bare address ("1.1.0"). Outer files first, inner ones win.
        var files = await ExtractAllAsync(outerArchive, cancellationToken, skip: nestedZipEntry.FullName);
        foreach (var (path, data) in innerFiles)
        {
            files[path] = data;
        }

        progress?.Report(new ParserProgress
        {
            Step = ParseStep.CheckPassword,
            PercentComplete = 100,
            Message = "Archive decrypted successfully"
        });

        return new ProjectFileMap(files);
    }

    private static async Task<Dictionary<string, byte[]>> ExtractAllAsync(
        ZipArchive archive,
        CancellationToken cancellationToken,
        string? skip = null)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            if (skip != null && entry.FullName.Equals(skip, StringComparison.OrdinalIgnoreCase)) continue;
            await using var entryStream = entry.Open();
            using var ms = new MemoryStream();
            await entryStream.CopyToAsync(ms, cancellationToken);
            files[entry.FullName] = ms.ToArray();
        }
        return files;
    }

    /// <summary>
    /// Decrypt the inner archive, trying every plausible password encoding. ETS 6 does not hand the
    /// user password to the ZIP directly but runs it through PBKDF2 first (see
    /// <see cref="DeriveEts6Password"/>), ETS 4/5 use it verbatim — so the correct variant depends on
    /// the detected ETS version. When detection is uncertain (<see cref="EtsVersion.Unknown"/>, e.g. a
    /// password-protected project whose knx_master.xml carries no usable schema marker) BOTH variants
    /// are tried instead of failing outright; the detected version only decides the order. The extra
    /// attempt costs one PBKDF2 run and only happens when the first variant fails.
    /// </summary>
    private static async Task<Dictionary<string, byte[]>> ExtractWithPasswordCandidatesAsync(
        MemoryStream nestedBuffer,
        EtsVersion etsVersion,
        string password,
        CancellationToken cancellationToken)
    {
        // ETS6 first unless we positively know this is an ETS 4/5 project. Deferred via Func so the
        // PBKDF2 run (65536 iterations) only happens when that candidate is actually reached.
        var ets6First = etsVersion is EtsVersion.Ets6 or EtsVersion.Unknown;
        var candidates = ets6First
            ? new Func<string>[] { () => DeriveEts6Password(password), () => password }
            : new Func<string>[] { () => password, () => DeriveEts6Password(password) };

        Exception? firstFailure = null;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            nestedBuffer.Position = 0;

            try
            {
                return await ExtractPasswordProtectedAsync(nestedBuffer, candidate(), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                firstFailure ??= ex;
            }
        }

        throw new InvalidOperationException(
            "Could not decrypt the password-protected project archive - the project password is wrong "
            + "or the archive uses an unsupported encryption.",
            firstFailure);
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
