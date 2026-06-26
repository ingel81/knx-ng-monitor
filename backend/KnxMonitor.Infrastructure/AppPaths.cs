namespace KnxMonitor.Infrastructure;

/// <summary>
/// Central resolver for the on-disk data directory.
/// Anchored to the executable location (AppContext.BaseDirectory), NOT the current
/// working directory — so the portable single-file binary always finds the same
/// ./data regardless of where it is launched from. In Docker BaseDirectory is /app,
/// so this resolves to /app/data (the mounted VOLUME) unchanged.
/// </summary>
public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(AppContext.BaseDirectory, "data");

    public static string LogsDir => Path.Combine(DataDir, "logs");

    public static string DbPath => Path.Combine(DataDir, "knxmonitor.db");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(LogsDir);
    }
}
