using System.Text;
using AlbLib.SaveGame;
using AlbLib.Texts;

namespace AlbionSaveEditor.Tests;

/// <summary>
/// Builds a synthetic Albion save: header + opaque gap + character XLD + opaque trailer
/// (the trailer contains a second XLD to make sure only the first one is touched).
/// Character records are filled with seeded random bytes (exercising unknown bytes, undefined
/// enum values and flag bits) and then known fields are written at their documented offsets.
/// </summary>
public sealed class SyntheticSave
{
    public byte[] Bytes { get; }
    public int HeaderLength { get; }
    public int XldOffset { get; }
    public int XldLength { get; }
    public byte[][] Subfiles { get; }

    public const string SaveName = "Synthetic save";
    public static readonly string[] CharacterNames = ["Tom", "Rainer", "Sira"];

    /// <summary>
    /// Subfile indexes: 0 = Tom, 1 = zero-length, 2 = Rainer, 3 = Sira (larger than 940 bytes),
    /// 4 = unused all-zero 940-byte record (reported with empty = true).
    /// </summary>
    public static readonly int[] CharacterIndexes = [0, 2, 3, 4];
    public const int EmptyRecordIndex = 4;

    static SyntheticSave()
    {
        // make sure TextCore's static constructor (encoding registration) has run
        _ = TextCore.DefaultEncoding;
    }

    public SyntheticSave(int seed = 1234, string saveName = SaveName)
    {
        var rnd = new Random(seed);
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);

        // ---- header
        byte[] name = TextCore.DefaultEncoding.GetBytes(saveName);
        w.Write((short)name.Length);
        w.Write((short)0x1234);          // unknown1
        w.Write(name);
        w.Write(0x0BADF00D);             // unknown2
        w.Write((byte)138);              // version 1.38
        w.Write(new byte[] { 1, 2, 3, 4, 5, 6, 7 }); // unknown3
        w.Write((short)3);               // days
        w.Write((short)14);              // hours
        w.Write((short)5);               // minutes
        w.Write((short)300);             // map id
        w.Write((short)42);              // party x
        w.Write((short)17);              // party y
        HeaderLength = (int)ms.Length;

        // ---- opaque game state between header and XLD (no 'X' bytes so no false signature)
        w.Write(RandomBytes(rnd, 333, noX: true));

        // ---- character XLD
        Subfiles =
        [
            MakeCharacter(rnd, CharacterNames[0], level: 3, NPCLayout.CharacterSize),
            [],
            MakeCharacter(rnd, CharacterNames[1], level: 7, NPCLayout.CharacterSize),
            MakeCharacter(rnd, CharacterNames[2], level: 12, NPCLayout.CharacterSize + 20),
            new byte[NPCLayout.CharacterSize],
        ];
        XldOffset = (int)ms.Length;
        w.Write(Encoding.ASCII.GetBytes("XLD0I\0"));
        w.Write((ushort)Subfiles.Length);
        foreach (var s in Subfiles) w.Write(s.Length);
        foreach (var s in Subfiles) w.Write(s);
        XldLength = (int)ms.Length - XldOffset;

        // ---- trailer: more game state and a second XLD that must be preserved verbatim
        w.Write(RandomBytes(rnd, 101, noX: true));
        w.Write(Encoding.ASCII.GetBytes("XLD0I\0"));
        w.Write((ushort)1);
        w.Write(10);
        w.Write(RandomBytes(rnd, 10, noX: false));
        w.Write(RandomBytes(rnd, 57, noX: false));

        w.Flush();
        Bytes = ms.ToArray();
    }

    private static byte[] RandomBytes(Random rnd, int n, bool noX)
    {
        var b = new byte[n];
        rnd.NextBytes(b);
        if (noX)
            for (int i = 0; i < n; i++)
                if (b[i] == (byte)'X') b[i] = 0;
        return b;
    }

    public static byte[] MakeCharacter(Random rnd, string name, byte level, int size)
    {
        var d = RandomBytes(rnd, size, noX: true);
        d[NPCLayout.Type] = (byte)NPCType.Character;
        d[NPCLayout.Gender] = (byte)Gender.Man;
        d[NPCLayout.Race] = (byte)Race.Terran;
        d[NPCLayout.Class] = (byte)CharacterClass.Pilot;
        d[NPCLayout.Level] = level;
        Put16(d, NPCLayout.Gold, 125);           // 12.5 gold
        Put16(d, NPCLayout.Strength, 30);
        Put16(d, NPCLayout.Strength + 2, 50);
        Put16(d, NPCLayout.LifePoints, 25);
        Put16(d, NPCLayout.LifePoints + 2, 30);
        BitConverter.GetBytes(1234).CopyTo(d, NPCLayout.Experience);
        // Like real saves, only the first name slot holds the name; slots 2/3 are empty.
        // Each slot gets a NUL terminator followed by non-zero garbage that must survive a round trip.
        var nb = Encoding.ASCII.GetBytes(name);
        nb.CopyTo(d, NPCLayout.Name);
        d[NPCLayout.Name + nb.Length] = 0;
        d[NPCLayout.Name + NPCLayout.NameSlotSize] = 0;
        d[NPCLayout.Name + 2 * NPCLayout.NameSlotSize] = 0;
        // backpack slot 0: 2 x item 17
        int b0 = NPCLayout.Backpack;
        d[b0] = 2; d[b0 + 1] = 0; d[b0 + 2] = 0; d[b0 + 3] = 0;
        Put16(d, b0 + 4, 17);
        return d;
    }

    public static void Put16(byte[] d, int offset, short v) => BitConverter.GetBytes(v).CopyTo(d, offset);
}
