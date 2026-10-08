using System.Text;

namespace AlbionSaveEditor.Services;

/// <summary>
/// Minimal read-only ISO9660 reader for CD images (GOG's <c>game.gog</c>, .iso, .bin).
/// Supports plain 2048-byte sector images and raw 2352-byte images in Mode 1 (user data at +16)
/// or Mode 2 Form 1 (user data at +24). The layout is detected by probing the primary volume
/// descriptor at sector 16. Files are looked up by path, case-insensitively, ignoring ";1".
/// </summary>
public sealed class CdImageReader : IDisposable
{
    private const int UserDataSize = 2048;
    private static readonly byte[] PvdMagic = [1, (byte)'C', (byte)'D', (byte)'0', (byte)'0', (byte)'1'];

    private readonly Stream stream;
    private readonly int sectorSize;
    private readonly int dataOffset;
    private readonly DirEntry root;

    public string Layout => $"{sectorSize}-byte sectors, user data at +{dataOffset}";

    private sealed record DirEntry(string Name, uint Lba, uint Size, bool IsDirectory);

    private CdImageReader(Stream stream, int sectorSize, int dataOffset)
    {
        this.stream = stream;
        this.sectorSize = sectorSize;
        this.dataOffset = dataOffset;
        byte[] pvd = ReadSector(16);
        root = ParseRecord(pvd, 156) ?? throw new InvalidDataException("Invalid root directory record.");
    }

    /// <summary>Opens an image file (read-only, shared). Returns null when it is not an ISO9660 image.</summary>
    public static CdImageReader? TryOpen(string path)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var reader = TryOpen(fs);
        if (reader == null) fs.Dispose();
        return reader;
    }

    /// <summary>Wraps a seekable stream. Returns null when no ISO9660 volume descriptor is found.</summary>
    public static CdImageReader? TryOpen(Stream stream)
    {
        foreach (var (size, offset) in new[] { (2048, 0), (2352, 24), (2352, 16) })
        {
            long pos = 16L * size + offset;
            if (pos + PvdMagic.Length > stream.Length) continue;
            stream.Position = pos;
            var magic = new byte[PvdMagic.Length];
            stream.ReadExactly(magic);
            if (magic.AsSpan().SequenceEqual(PvdMagic))
            {
                try { return new CdImageReader(stream, size, offset); }
                catch (Exception e) when (e is InvalidDataException or EndOfStreamException) { return null; }
            }
        }
        return null;
    }

    /// <summary>Reads a file by path such as "/ALBION/XLDLIBS/ITEMNAME.DAT". Returns null when not found.</summary>
    public byte[]? ReadFile(string path)
    {
        var entry = Find(path);
        if (entry == null || entry.IsDirectory) return null;
        return ReadExtent(entry.Lba, entry.Size);
    }

    public bool FileExists(string path) => Find(path) is { IsDirectory: false };

    /// <summary>Lists names in a directory (for diagnostics/tests).</summary>
    public IReadOnlyList<string> List(string path)
    {
        var dir = Find(path);
        return dir is { IsDirectory: true } ? ReadDirectory(dir).Select(e => e.Name).ToList() : [];
    }

    private DirEntry? Find(string path)
    {
        var current = root;
        foreach (var part in path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!current.IsDirectory) return null;
            string wanted = StripVersion(part);
            var next = ReadDirectory(current).FirstOrDefault(e => string.Equals(e.Name, wanted, StringComparison.OrdinalIgnoreCase));
            if (next == null) return null;
            current = next;
        }
        return current;
    }

    private IEnumerable<DirEntry> ReadDirectory(DirEntry dir)
    {
        byte[] data = ReadExtent(dir.Lba, dir.Size);
        int pos = 0;
        while (pos < data.Length)
        {
            int len = data[pos];
            if (len == 0)
            {
                // records never span sectors: skip to the next sector
                pos = (pos / UserDataSize + 1) * UserDataSize;
                continue;
            }
            if (pos + len > data.Length) yield break;
            var e = ParseRecord(data, pos);
            pos += len;
            if (e != null && e.Name != "\0" && e.Name != "\u0001") yield return e;
        }
    }

    private static DirEntry? ParseRecord(byte[] d, int pos)
    {
        int len = d[pos];
        if (len < 34 || pos + len > d.Length) return null;
        uint lba = BitConverter.ToUInt32(d, pos + 2);
        uint size = BitConverter.ToUInt32(d, pos + 10);
        bool isDir = (d[pos + 25] & 2) != 0;
        int nameLen = d[pos + 32];
        if (pos + 33 + nameLen > d.Length) return null;
        string name = nameLen == 1 && d[pos + 33] <= 1
            ? ((char)d[pos + 33]).ToString()
            : StripVersion(Encoding.ASCII.GetString(d, pos + 33, nameLen));
        return new DirEntry(name, lba, size, isDir);
    }

    private static string StripVersion(string name)
    {
        int semi = name.IndexOf(';');
        if (semi >= 0) name = name[..semi];
        return name.EndsWith('.') ? name[..^1] : name; // "FILE." for names without extension
    }

    private byte[] ReadSector(long lba)
    {
        var buffer = new byte[UserDataSize];
        stream.Position = lba * sectorSize + dataOffset;
        stream.ReadExactly(buffer);
        return buffer;
    }

    private byte[] ReadExtent(uint lba, uint size)
    {
        if (size > 64 * 1024 * 1024) throw new InvalidDataException("File too large.");
        var result = new byte[size];
        int done = 0;
        for (long sector = lba; done < size; sector++)
        {
            byte[] s = ReadSector(sector);
            int n = (int)Math.Min(UserDataSize, size - done);
            Array.Copy(s, 0, result, done, n);
            done += n;
        }
        return result;
    }

    public void Dispose() => stream.Dispose();
}
