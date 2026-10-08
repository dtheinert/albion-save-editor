namespace AlbionSaveEditor.Services;

/// <summary>
/// A place Albion game files can be read from (read-only). Paths are relative to the game's
/// root folder, e.g. "XLDLIBS/ITEMNAME.DAT".
/// </summary>
public interface IGameDataSource : IDisposable
{
    string Description { get; }
    byte[]? ReadFile(string relativePath);
}

/// <summary>An installed game folder (root containing XLDLIBS, or XLDLIBS itself).</summary>
public sealed class FolderGameDataSource(string root) : IGameDataSource
{
    public string Description => $"folder {root}";

    public byte[]? ReadFile(string relativePath)
    {
        string rel = relativePath.Replace('/', Path.DirectorySeparatorChar);
        foreach (var candidate in new[]
                 {
                     Path.Combine(root, rel),
                     // root may be XLDLIBS itself
                     Path.Combine(root, StripFirst(rel, "XLDLIBS")),
                 })
        {
            if (File.Exists(candidate))
            {
                using var fs = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var data = new byte[fs.Length];
                fs.ReadExactly(data);
                return data;
            }
        }
        return null;
    }

    private static string StripFirst(string rel, string dir)
    {
        string prefix = dir + Path.DirectorySeparatorChar;
        return rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? rel[prefix.Length..] : rel;
    }

    public void Dispose() { }
}

/// <summary>A CD image (game.gog / .iso / .bin). Files live under /ALBION/ on the Albion CD.</summary>
public sealed class CdImageGameDataSource(string imagePath, CdImageReader reader) : IGameDataSource
{
    public string Description => $"CD image {imagePath} ({reader.Layout})";

    public byte[]? ReadFile(string relativePath)
        => reader.ReadFile("ALBION/" + relativePath) ?? reader.ReadFile(relativePath);

    public void Dispose() => reader.Dispose();

    public static readonly string[] ImageExtensions = [".gog", ".iso", ".bin"];
}
