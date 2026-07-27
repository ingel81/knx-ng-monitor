namespace KnxMonitor.Infrastructure;

/// <summary>
/// Central resolver for the on-disk data directory.
/// Anchored to the real executable, NOT the current working directory — so the portable
/// single-file binary always finds the same ./data regardless of where it is launched from.
/// In Docker this resolves to /app/data (the mounted VOLUME).
/// </summary>
public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(ResolveAppDirectory(), "data");

    public static string LogsDir => Path.Combine(DataDir, "logs");

    public static string DbPath => Path.Combine(DataDir, "knxmonitor.db");

    /// <summary>
    /// Directory of the running executable.
    /// <para>
    /// <see cref="AppContext.BaseDirectory"/> must NOT be used here. We publish with
    /// <c>PublishSingleFile</c> + <c>EnableCompressionInSingleFile</c>, so the whole bundle is
    /// extracted at startup and <c>BaseDirectory</c> points at that extraction directory —
    /// <c>$DOTNET_BUNDLE_EXTRACT_BASE_DIR/KnxMonitor.Api/&lt;bundle-hash&gt;/</c>, i.e. under
    /// <c>/tmp/.net/</c> in the container image. Data written there bypasses the mounted volume
    /// entirely and is lost when the container is recreated; and because that directory name is a
    /// hash over the bundle contents, every new version would start from an empty directory anyway.
    /// </para>
    /// <para>
    /// <see cref="Environment.ProcessPath"/> is the host executable itself and stays put. The one
    /// case it does not answer is <c>dotnet run</c> / <c>dotnet app.dll</c>, where the process is
    /// the SDK host — there we fall back to <c>BaseDirectory</c>, which is the (unextracted)
    /// build output directory and therefore correct.
    /// </para>
    /// </summary>
    private static string ResolveAppDirectory()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath)
            && !string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet",
                StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(processPath);
            if (!string.IsNullOrEmpty(directory))
            {
                return directory;
            }
        }

        return AppContext.BaseDirectory;
    }

    /// <summary>
    /// Builds 0.8.0 - 0.8.2 resolved the data directory through <see cref="AppContext.BaseDirectory"/>,
    /// which for the single-file bundle is the extraction directory. Returns the database left
    /// behind there when one exists and the location actually differs, so startup can point the
    /// user at it instead of silently coming up empty. Returns null on every healthy setup.
    /// </summary>
    public static string? FindStrandedDbPath()
    {
        var legacyDir = Path.Combine(AppContext.BaseDirectory, "data");
        if (string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(legacyDir)),
                          Path.TrimEndingDirectorySeparator(Path.GetFullPath(DataDir)),
                          StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var legacyDb = Path.Combine(legacyDir, "knxmonitor.db");
        return File.Exists(legacyDb) ? legacyDb : null;
    }

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(LogsDir);
    }
}
