namespace KnxMonitor.ProjectParser.Core.Models;

/// <summary>
/// The inner files of a .knxproj, addressed by their archive path.
/// <para>
/// Entries are <b>lazy</b>: the map knows every path up front but only decompresses an entry when it
/// is actually read. That matters because a project archive is dominated by the manufacturer
/// application programs (<c>M-XXXX/*_A-*.xml</c>, single files up to ~60 MB, 87 to 99 % of the
/// uncompressed volume) while an import reads only a handful of them. Materialising everything as
/// <c>byte[]</c> up front turned a 6.7 MB project into 128 MB of managed heap and risked an OOM on a
/// Pi or a memory-capped container.
/// </para>
/// <para>
/// The map therefore OWNS the archive handles it was built from and must be disposed by its creator;
/// reading from a disposed map throws. <see cref="AccessedPaths"/> records what was really read and
/// exists so tests can prove the application programs stay untouched.
/// </para>
/// </summary>
public sealed class ProjectFileMap : IDisposable
{
    private readonly Dictionary<string, Entry> _files;
    private readonly IDisposable[] _owned;
    private readonly HashSet<string> _accessed = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private bool _disposed;

    private readonly record struct Entry(byte[]? Bytes, Func<Stream>? Open);

    /// <summary>
    /// Build a fully materialised map from in-memory content. Used by tests and by callers that
    /// already hold the bytes; nothing is owned, so disposing is a no-op.
    /// </summary>
    public ProjectFileMap(Dictionary<string, byte[]> files)
    {
        _files = new Dictionary<string, Entry>(files.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (path, bytes) in files)
        {
            _files[path] = new Entry(bytes, null);
        }
        _owned = Array.Empty<IDisposable>();
    }

    /// <summary>
    /// Build a lazy map. Each opener returns a fresh readable stream over one archive entry;
    /// <paramref name="owned"/> are the archive handles keeping those openers alive and are disposed
    /// in reverse order together with the map.
    /// </summary>
    public ProjectFileMap(
        IReadOnlyDictionary<string, Func<Stream>> openers,
        IEnumerable<IDisposable>? owned = null)
    {
        _files = new Dictionary<string, Entry>(openers.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (path, open) in openers)
        {
            _files[path] = new Entry(null, open);
        }
        _owned = owned?.ToArray() ?? Array.Empty<IDisposable>();
    }

    public int Count => _files.Count;

    public IEnumerable<string> FilePaths => _files.Keys;

    /// <summary>Paths that have been opened at least once, in no particular order.</summary>
    public IReadOnlyCollection<string> AccessedPaths
    {
        get
        {
            lock (_gate)
            {
                return _accessed.ToArray();
            }
        }
    }

    public bool Contains(string path) => _files.ContainsKey(path);

    public byte[] GetBytes(string path)
    {
        var entry = GetEntry(path);
        if (entry.Bytes != null)
        {
            MarkAccessed(path);
            return entry.Bytes;
        }

        using var stream = OpenEntry(path, entry);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Open one entry for reading. For a lazy entry this is the live (forward-only) decompression
    /// stream, so a 50 MB application program can be fed to an <c>XmlReader</c> without ever being
    /// held in memory as a whole. The caller disposes the returned stream.
    /// </summary>
    public Stream OpenRead(string path)
    {
        var entry = GetEntry(path);
        if (entry.Bytes == null)
        {
            return OpenEntry(path, entry);
        }

        MarkAccessed(path);
        return new MemoryStream(entry.Bytes, writable: false);
    }

    public IEnumerable<string> FindPaths(Func<string, bool> predicate)
    {
        foreach (var path in _files.Keys)
        {
            if (predicate(path)) yield return path;
        }
    }

    public string? FindFirstByName(string fileName)
    {
        foreach (var path in _files.Keys)
        {
            if (PathEndsWithName(path, fileName)) return path;
        }
        return null;
    }

    public IEnumerable<string> FindAllByName(string fileName)
    {
        foreach (var path in _files.Keys)
        {
            if (PathEndsWithName(path, fileName)) yield return path;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        // Reverse order: the readers built on top of a buffer go before the buffer itself.
        for (var i = _owned.Length - 1; i >= 0; i--)
        {
            try
            {
                _owned[i].Dispose();
            }
            catch
            {
                // A failing archive handle must not mask the real result of the import.
            }
        }
    }

    private Entry GetEntry(string path)
    {
        if (!_files.TryGetValue(path, out var entry))
        {
            throw new FileNotFoundException($"File '{path}' not found in project archive");
        }
        return entry;
    }

    private Stream OpenEntry(string path, Entry entry)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _accessed.Add(path);
            return entry.Open!();
        }
    }

    private void MarkAccessed(string path)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _accessed.Add(path);
        }
    }

    private static bool PathEndsWithName(string path, string fileName)
    {
        if (path.Equals(fileName, StringComparison.OrdinalIgnoreCase)) return true;
        if (path.Length <= fileName.Length) return false;
        var sep = path[path.Length - fileName.Length - 1];
        if (sep != '/' && sep != '\\') return false;
        return path.EndsWith(fileName, StringComparison.OrdinalIgnoreCase);
    }
}
