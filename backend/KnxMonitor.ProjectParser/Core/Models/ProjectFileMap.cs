namespace KnxMonitor.ProjectParser.Core.Models;

public sealed class ProjectFileMap
{
    private readonly Dictionary<string, byte[]> _files;

    public ProjectFileMap(Dictionary<string, byte[]> files)
    {
        _files = new Dictionary<string, byte[]>(files, StringComparer.OrdinalIgnoreCase);
    }

    public int Count => _files.Count;

    public IEnumerable<string> FilePaths => _files.Keys;

    public bool Contains(string path) => _files.ContainsKey(path);

    public byte[] GetBytes(string path)
    {
        if (!_files.TryGetValue(path, out var bytes))
        {
            throw new FileNotFoundException($"File '{path}' not found in project archive");
        }
        return bytes;
    }

    public Stream OpenRead(string path) => new MemoryStream(GetBytes(path), writable: false);

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

    private static bool PathEndsWithName(string path, string fileName)
    {
        if (path.Equals(fileName, StringComparison.OrdinalIgnoreCase)) return true;
        if (path.Length <= fileName.Length) return false;
        var sep = path[path.Length - fileName.Length - 1];
        if (sep != '/' && sep != '\\') return false;
        return path.EndsWith(fileName, StringComparison.OrdinalIgnoreCase);
    }
}
