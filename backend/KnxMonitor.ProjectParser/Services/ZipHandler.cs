using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ICSharpCode.SharpZipLib.Checksum;
using ICSharpCode.SharpZipLib.Zip;
using SharpZipFile = ICSharpCode.SharpZipLib.Zip.ZipFile;
using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Core.Models;

namespace KnxMonitor.ProjectParser.Services;

public static class ZipHandler
{
    /// <summary>
    /// Index the archive and hand back a <see cref="ProjectFileMap"/> over it. Nothing but the
    /// central directory (and, for a password-protected project, the encrypted inner container) is
    /// read here — entries are decompressed on first access. The returned map owns the archive
    /// handles and must be disposed; <paramref name="stream"/> has to stay open until then.
    /// </summary>
    public static async Task<ProjectFileMap> LoadAsync(
        Stream stream,
        ProjectFeatures features,
        string? password,
        IProgress<ParserProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var owned = new List<IDisposable>();

        try
        {
            var outerArchive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            owned.Add(outerArchive);

            var entries = new Dictionary<string, Func<Stream>>(StringComparer.OrdinalIgnoreCase);

            if (!features.HasPassword)
            {
                AddEntries(entries, outerArchive);
                return new ProjectFileMap(entries, owned);
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
                Message = "Opening password-protected archive"
            });

            var nestedZipEntry = outerArchive.Entries
                .FirstOrDefault(e => e.FullName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                                     && e.Name.StartsWith("P-", StringComparison.OrdinalIgnoreCase));

            if (nestedZipEntry == null)
            {
                throw new InvalidOperationException("Nested project archive not found");
            }

            // The encrypted inner container is the one thing we do pull into memory: SharpZipLib needs
            // a seekable source, and the compressed container is a fraction of its contents (6.7 MB
            // versus 128 MB unpacked on a real project). Its entries stay encrypted until read.
            var nestedBuffer = new MemoryStream();
            owned.Add(nestedBuffer);
            await using (var nestedStream = nestedZipEntry.Open())
            {
                await nestedStream.CopyToAsync(nestedBuffer, cancellationToken);
            }
            nestedBuffer.Position = 0;

            var innerArchive = OpenWithPasswordCandidates(
                nestedBuffer, features.EtsVersion, password, cancellationToken);
            owned.Add(innerArchive);

            // Only the P-XXXX/ project folder is encrypted. knx_master.xml and the M-XXXX/ manufacturer
            // folders (Hardware.xml, Catalog.xml) stay in the OUTER archive — without them devices have
            // no product name and no manufacturer, so a password-protected project used to import with
            // devices named after their bare address ("1.1.0"). Outer files first, inner ones win.
            AddEntries(entries, outerArchive, skip: nestedZipEntry.FullName);
            AddEntries(entries, innerArchive);

            progress?.Report(new ParserProgress
            {
                Step = ParseStep.CheckPassword,
                PercentComplete = 100,
                Message = "Archive decrypted successfully"
            });

            return new ProjectFileMap(entries, owned);
        }
        catch
        {
            for (var i = owned.Count - 1; i >= 0; i--)
            {
                try { owned[i].Dispose(); } catch { /* the original failure is the interesting one */ }
            }
            throw;
        }
    }

    private static void AddEntries(
        Dictionary<string, Func<Stream>> sink,
        ZipArchive archive,
        string? skip = null)
    {
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            if (skip != null && entry.FullName.Equals(skip, StringComparison.OrdinalIgnoreCase)) continue;

            var captured = entry;
            sink[entry.FullName] = () => captured.Open();
        }
    }

    private static void AddEntries(Dictionary<string, Func<Stream>> sink, SharpZipFile archive)
    {
        foreach (ZipEntry entry in archive)
        {
            if (!entry.IsFile) continue;

            var captured = entry;
            sink[entry.Name.Replace('\\', '/')] = () => archive.GetInputStream(captured);
        }
    }

    /// <summary>
    /// Open the inner archive, trying every plausible password encoding. ETS 6 does not hand the
    /// user password to the ZIP directly but runs it through PBKDF2 first (see
    /// <see cref="DeriveEts6Password"/>), ETS 4/5 use it verbatim — so the correct variant depends on
    /// the detected ETS version. When detection is uncertain (<see cref="EtsVersion.Unknown"/>, e.g. a
    /// password-protected project whose knx_master.xml carries no usable schema marker) BOTH variants
    /// are tried instead of failing outright; the detected version only decides the order. The extra
    /// attempt costs one PBKDF2 run and only happens when the first variant fails.
    /// </summary>
    private static SharpZipFile OpenWithPasswordCandidates(
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

            SharpZipFile? archive = null;
            try
            {
                archive = new SharpZipFile(nestedBuffer) { Password = candidate(), IsStreamOwner = false };
                VerifyPassword(archive, cancellationToken);
                return archive;
            }
            catch (Exception ex)
            {
                // Close on EVERY failure path: at this point the handle is not in `owned` yet, so a
                // cancellation would otherwise leak it until the finalizer runs.
                archive?.Close();
                if (ex is OperationCanceledException) throw;
                firstFailure ??= ex;
            }
        }

        throw new InvalidOperationException(
            "Could not decrypt the password-protected project archive - the project password is wrong "
            + "or the archive uses an unsupported encryption.",
            firstFailure);
    }

    /// <summary>
    /// A wrong password no longer surfaces while unpacking (nothing is unpacked here), so prove it
    /// up front by decrypting the smallest entry and checking it against its stored CRC.
    /// <para>
    /// The CRC is the part that matters for ZipCrypto (ETS 4/5): it only has a one-byte check value,
    /// so roughly one wrong password in 256 gets past it. Inflating a Deflated entry would then
    /// almost certainly fail — but a Stored entry produces garbage without complaint, and the import
    /// would die later on an opaque XML error instead of saying "wrong password".
    /// <c>ZipFile.GetInputStream</c> does not validate the CRC itself (only <c>ZipInputStream</c>
    /// does), so it is computed here.
    /// </para>
    /// <para>
    /// AES entries are excluded from that comparison: AE-2 stores a zero in the CRC field by design,
    /// the password is already rejected from the entry header, and reading the stream to its end
    /// checks the authentication code. Comparing against the zeroed field would fail every correct
    /// AES password.
    /// </para>
    /// </summary>
    private static void VerifyPassword(SharpZipFile archive, CancellationToken cancellationToken)
    {
        ZipEntry? probe = null;
        foreach (ZipEntry entry in archive)
        {
            if (!entry.IsFile || entry.Size <= 0) continue;
            if (probe == null || entry.Size < probe.Size) probe = entry;
        }

        if (probe == null) return; // nothing decryptable in here; let the loaders report the real problem

        var crc = new Crc32();
        using var stream = archive.GetInputStream(probe);
        var buffer = new byte[8192];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            crc.Update(new ArraySegment<byte>(buffer, 0, read));
        }

        var isAes = probe.AESKeySize > 0;

        if (!isAes && probe.HasCrc && crc.Value != probe.Crc)
        {
            throw new InvalidOperationException(
                $"Decrypted entry '{probe.Name}' does not match its checksum");
        }
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
}
