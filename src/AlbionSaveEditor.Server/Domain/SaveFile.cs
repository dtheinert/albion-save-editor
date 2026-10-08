using AlbLib.SaveGame;
using AlbLib.XLD;

namespace AlbionSaveEditor.Domain;

/// <summary>Thrown when an uploaded file cannot be parsed as an Albion save.</summary>
public sealed class SaveFormatException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// An Albion savegame held in memory.
///
/// Layout: [header (SaveGameInfo)] [opaque bytes] [character XLD] [opaque bytes: other XLDs, game state...].
/// Only the header and the character records are interpreted; every other byte is copied
/// verbatim from the uploaded file when the save is rebuilt.
/// </summary>
public sealed class SaveFile
{
    /// <summary>Fixed part of the header: name length, unknown1, unknown2, version, unknown3[7], 6 x int16.</summary>
    public const int HeaderFixedSize = 2 + 2 + 4 + 1 + 7 + 12;

    private readonly byte[] original;
    private readonly int originalHeaderLength;
    private readonly int xldOffset;
    private readonly int xldLength;
    private readonly XLDFile xld;
    private SaveGameInfo header;

    public string FileName { get; }

    /// <summary>Lock for callers that mutate the save.</summary>
    public object SyncRoot { get; } = new();

    /// <summary>True once the header was replaced; otherwise the original header bytes are written back.</summary>
    public bool HeaderModified { get; private set; }

    /// <summary>Party characters keyed by their subfile index in the character XLD.</summary>
    public SortedDictionary<int, Character> Characters { get; } = new();

    public SaveGameInfo Header => header;

    /// <summary>Absolute offset of the character XLD in the uploaded file.</summary>
    public int XldOffset => xldOffset;

    /// <summary>Length of the character XLD in the uploaded file.</summary>
    public int XldLength => xldLength;

    /// <summary>Number of subfiles in the character XLD (including empty / non-character ones).</summary>
    public int SubfileCount => xld.Count;

    private SaveFile(string fileName, byte[] original, SaveGameInfo header, int headerLength, int xldOffset, int xldLength, XLDFile xld)
    {
        FileName = fileName;
        this.original = original;
        this.header = header;
        originalHeaderLength = headerLength;
        this.xldOffset = xldOffset;
        this.xldLength = xldLength;
        this.xld = xld;
    }

    public static SaveFile Parse(byte[] data, string fileName)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < HeaderFixedSize)
            throw new SaveFormatException("File is too small to be an Albion save.");

        short nameLength = BitConverter.ToInt16(data, 0);
        if (nameLength < 0 || HeaderFixedSize + nameLength > data.Length)
            throw new SaveFormatException("Invalid save header (bad name length).");
        int headerLength = HeaderFixedSize + nameLength;

        SaveGameInfo header;
        try
        {
            header = new SaveGameInfo(new MemoryStream(data, 0, headerLength, false));
        }
        catch (Exception e)
        {
            throw new SaveFormatException("Invalid save header.", e);
        }

        XLDFile xld;
        int xldOffset;
        using (var stream = new MemoryStream(data, false))
        {
            stream.Position = headerLength;
            try
            {
                xld = XLDFile.FindXLD(stream, 0, out xldOffset);
            }
            catch (Exception e)
            {
                throw new SaveFormatException("The character data (XLD) in the save is damaged.", e);
            }
        }
        if (xld == null)
            throw new SaveFormatException("No character data (XLD block) found in the file.");

        int xldLength = 8 + 4 * xld.Count + xld.Subfiles.Sum(s => s?.Length ?? 0);
        if (xldOffset + (long)xldLength > data.Length)
            throw new SaveFormatException("The character data (XLD) in the save is truncated.");

        var save = new SaveFile(fileName, (byte[])data.Clone(), header, headerLength, xldOffset, xldLength, xld);
        for (int i = 0; i < xld.Count; i++)
        {
            var sub = xld.Subfiles[i];
            if (sub?.Data == null || sub.Length < NPCLayout.CharacterSize) continue; // empty slot: kept verbatim
            try
            {
                save.Characters[i] = new Character(sub.Data);
            }
            catch (Exception e)
            {
                throw new SaveFormatException($"Character record {i} could not be read.", e);
            }
        }
        return save;
    }

    /// <summary>Replaces the header (the caller validated it).</summary>
    public void SetHeader(SaveGameInfo newHeader)
    {
        header = newHeader;
        HeaderModified = true;
    }

    /// <summary>
    /// Rebuilds the save: header + original bytes up to the XLD + rebuilt XLD + original bytes after the XLD.
    /// When the header name length changes, everything after the header simply shifts.
    /// </summary>
    public byte[] Build()
    {
        var output = new MemoryStream(original.Length + 64);
        if (HeaderModified)
        {
            byte[] h = header.ToRawData();
            output.Write(h, 0, h.Length);
        }
        else
        {
            output.Write(original, 0, originalHeaderLength);
        }

        output.Write(original, originalHeaderLength, xldOffset - originalHeaderLength);

        var subfiles = new List<XLDSubfile>(xld.Count);
        for (int i = 0; i < xld.Count; i++)
        {
            var sub = xld.Subfiles[i];
            if (Characters.TryGetValue(i, out var ch))
                subfiles.Add(new XLDSubfile(ch.ToRawData(), (short)i));
            else
                subfiles.Add(sub);
        }
        new XLDFile(subfiles).Save(output);

        int after = xldOffset + xldLength;
        output.Write(original, after, original.Length - after);
        return output.ToArray();
    }

    /// <summary>Returns the original bytes of the character XLD subfile (for tests/diagnostics).</summary>
    public byte[] GetOriginalSubfile(int index) => (byte[])xld.Subfiles[index].Data.Clone();
}
