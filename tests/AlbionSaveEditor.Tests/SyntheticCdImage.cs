using System.Text;

namespace AlbionSaveEditor.Tests;

/// <summary>
/// Builds a tiny ISO9660 image: /ALBION/XLDLIBS/{ITEMNAME.DAT;1, ITEMLIST.DAT;1, ENGLISH/SYSTEXTS;1}.
/// Can be emitted as plain 2048-byte sectors or as raw 2352-byte sectors (Mode 1 or Mode 2 Form 1).
/// </summary>
public static class SyntheticCdImage
{
    public const int Sector = 2048;

    public static byte[] ItemNames(params (string De, string En, string Fr)[] items)
    {
        var data = new byte[items.Length * 60];
        for (int i = 0; i < items.Length; i++)
        {
            Encoding.Latin1.GetBytes(items[i].De).CopyTo(data, i * 60);
            Encoding.Latin1.GetBytes(items[i].En).CopyTo(data, i * 60 + 20);
            Encoding.Latin1.GetBytes(items[i].Fr).CopyTo(data, i * 60 + 40);
        }
        return data;
    }

    public static byte[] Build(byte[] itemName, byte[] itemList, string sysTexts, int rawSectorSize = 2048, int dataOffset = 0)
    {
        // sectors: 16 PVD, 17 terminator, 18 root, 19 ALBION, 20 XLDLIBS, 21 ENGLISH, 22.. files
        byte[] sys = Encoding.Latin1.GetBytes(sysTexts);
        int itemNameLba = 22;
        int itemListLba = itemNameLba + Sectors(itemName.Length);
        int sysLba = itemListLba + Sectors(itemList.Length);
        int total = sysLba + Sectors(sys.Length) + 1;
        var img = new byte[total * Sector];

        var pvd = new byte[Sector];
        pvd[0] = 1; Encoding.ASCII.GetBytes("CD001").CopyTo(pvd, 1); pvd[6] = 1;
        Record(18, Sector, true, [0]).CopyTo(pvd, 156);
        pvd.CopyTo(img, 16 * Sector);
        img[17 * Sector] = 255; Encoding.ASCII.GetBytes("CD001").CopyTo(img, 17 * Sector + 1);

        WriteDir(img, 18, 18, 18, [Record(19, Sector, true, Encoding.ASCII.GetBytes("ALBION"))]);
        WriteDir(img, 19, 19, 18, [Record(20, Sector, true, Encoding.ASCII.GetBytes("XLDLIBS"))]);
        WriteDir(img, 20, 20, 19,
        [
            Record(21, Sector, true, Encoding.ASCII.GetBytes("ENGLISH")),
            Record(itemListLba, itemList.Length, false, Encoding.ASCII.GetBytes("ITEMLIST.DAT;1")),
            Record(itemNameLba, itemName.Length, false, Encoding.ASCII.GetBytes("ITEMNAME.DAT;1")),
        ]);
        WriteDir(img, 21, 21, 20, [Record(sysLba, sys.Length, false, Encoding.ASCII.GetBytes("SYSTEXTS.;1"))]);
        itemName.CopyTo(img, itemNameLba * Sector);
        itemList.CopyTo(img, itemListLba * Sector);
        sys.CopyTo(img, sysLba * Sector);

        if (rawSectorSize == Sector) return img;
        var raw = new byte[total * rawSectorSize];
        for (int s = 0; s < total; s++)
        {
            // fill sync/header/EDC area with junk so offsets must be right
            for (int k = 0; k < rawSectorSize; k++) raw[s * rawSectorSize + k] = 0xAA;
            Array.Copy(img, s * Sector, raw, s * rawSectorSize + dataOffset, Sector);
        }
        return raw;
    }

    private static int Sectors(int bytes) => Math.Max(1, (bytes + Sector - 1) / Sector);

    private static void WriteDir(byte[] img, int lba, int self, int parent, byte[][] entries)
    {
        int pos = lba * Sector;
        foreach (var r in new[] { Record(self, Sector, true, [0]), Record(parent, Sector, true, [1]) }.Concat(entries))
        {
            r.CopyTo(img, pos);
            pos += r.Length;
        }
    }

    private static byte[] Record(int lba, int size, bool dir, byte[] name)
    {
        int len = 33 + name.Length;
        if (len % 2 == 1) len++;
        var r = new byte[len];
        r[0] = (byte)len;
        BitConverter.GetBytes((uint)lba).CopyTo(r, 2);
        BitConverter.GetBytes((uint)lba).Reverse().ToArray().CopyTo(r, 6);
        BitConverter.GetBytes((uint)size).CopyTo(r, 10);
        BitConverter.GetBytes((uint)size).Reverse().ToArray().CopyTo(r, 14);
        r[25] = (byte)(dir ? 2 : 0);
        r[28] = 1;
        r[32] = (byte)name.Length;
        name.CopyTo(r, 33);
        return r;
    }
}
