using AlbLib.SaveGame;
using AlbLib.Texts;
using AlbionSaveEditor.Domain;

namespace AlbionSaveEditor.Tests;

public class SaveFileTests
{
    [Fact]
    public void Parse_FindsHeaderXldAndCharacters()
    {
        var fx = new SyntheticSave();
        var save = SaveFile.Parse(fx.Bytes, "SAVE.001");

        Assert.Equal(SyntheticSave.SaveName, save.Header.Name);
        Assert.Equal(new Version(1, 38), save.Header.Version);
        Assert.Equal(3, save.Header.Days);
        Assert.Equal(300, save.Header.MapID);
        Assert.Equal(fx.XldOffset, save.XldOffset);
        Assert.Equal(fx.XldLength, save.XldLength);
        Assert.Equal(5, save.SubfileCount);
        Assert.Equal(SyntheticSave.CharacterIndexes, save.Characters.Keys.ToArray());
        Assert.Equal("Tom", save.Characters[0].Name.German);
        Assert.Equal("", save.Characters[0].Name.English);
        Assert.Equal("Rainer", save.Characters[2].Name.German);
        Assert.Equal(12, save.Characters[3].Level);
    }

    [Fact]
    public void KnownFields_AreReadFromDocumentedOffsets()
    {
        var fx = new SyntheticSave();
        var ch = SaveFile.Parse(fx.Bytes, "SAVE.001").Characters[0];
        var raw = fx.Subfiles[0];

        Assert.Equal(3, ch.Level);
        Assert.Equal(12.5m, ch.Gold);
        Assert.Equal(30, ch.Strength.Value);
        Assert.Equal(50, ch.Strength.MaximumValue);
        Assert.Equal(25, ch.LifePoints.Value);
        Assert.Equal(30, ch.LifePoints.MaximumValue);
        Assert.Equal(1234, ch.Experience);
        Assert.Equal(BitConverter.ToInt16(raw, NPCLayout.Age), ch.Age.Value);
        Assert.Equal(BitConverter.ToInt16(raw, NPCLayout.Age + 2), ch.Age.MaximumValue);
        Assert.Equal(raw[NPCLayout.Morale], ch.Morale);
        Assert.Equal(BitConverter.ToUInt16(raw, NPCLayout.Gold) / 10m, ch.Gold);
        Assert.Equal(BitConverter.ToInt16(raw, NPCLayout.Rations), ch.Rations);
        Assert.Equal(BitConverter.ToInt16(raw, NPCLayout.BonusAttack), ch.BonusAttack);
        Assert.Equal(BitConverter.ToUInt16(raw, NPCLayout.EventSetId), ch.EventSetId);
        Assert.Equal(BitConverter.ToUInt16(raw, NPCLayout.TrainingPointsPerLevel), ch.TrainingPointsPerLevel);
        Assert.Equal(BitConverter.ToInt32(raw, NPCLayout.Spells + 4 * 6), ch.Spells[6]);
        Assert.Equal(BitConverter.ToInt16(raw, NPCLayout.SpellStrengths + 2 * (6 * 30 + 29)), ch.SpellStrengths[6, 29]);
        Assert.Equal(raw[NPCLayout.ActionPoints], ch.ActionPoints);
        Assert.Equal(2, ch.Backpack[0].Count);
        Assert.Equal(17, ch.Backpack[0].Type);
        Assert.Equal(raw[NPCLayout.Equipment + 8 * 6], ch.Equipment.LeftFinger.Count);
    }

    [Fact]
    public void RoundTrip_Unedited_IsByteIdentical()
    {
        var fx = new SyntheticSave();
        var save = SaveFile.Parse(fx.Bytes, "SAVE.001");
        Assert.Equal(fx.Bytes, save.Build());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(99)]
    [InlineData(31337)]
    public void CharacterToRawData_Unedited_IsByteIdentical(int seed)
    {
        var fx = new SyntheticSave(seed);
        var save = SaveFile.Parse(fx.Bytes, "SAVE.001");
        foreach (int i in SyntheticSave.CharacterIndexes)
            Assert.Equal(fx.Subfiles[i], save.Characters[i].ToRawData());
    }

    [Fact]
    public void CharacterFromScratch_RoundTripsThroughReader()
    {
        var ch = new Character
        {
            Level = 9,
            Gold = 99.9m,
            Name = new LanguageTerm("Ölaf", "Olaf", "Olaf"),
            Strength = new CharacterAttribute(11, 22),
            Experience = 123456,
        };
        var raw = ch.ToRawData();
        Assert.Equal(NPCLayout.CharacterSize, raw.Length);
        var back = new Character(raw);
        Assert.Equal(9, back.Level);
        Assert.Equal(99.9m, back.Gold);
        Assert.Equal("Ölaf", back.Name.German);
        Assert.Equal(new CharacterAttribute(11, 22), back.Strength);
        Assert.Equal(123456, back.Experience);
        Assert.Equal(raw, back.ToRawData());
    }

    [Fact]
    public void EditFields_ThenReload_ChangesOnlyThoseBytes()
    {
        var fx = new SyntheticSave();
        var save = SaveFile.Parse(fx.Bytes, "SAVE.001");
        var ch = save.Characters[2];
        ch.Level = 42;
        ch.Gold = 321.7m;
        ch.Experience = 99999;
        ch.Strength = new CharacterAttribute(77, 88);
        ch.Name = new LanguageTerm("Rainer H.", ch.Name.English, ch.Name.French);
        var stack = ch.Backpack[5];
        stack.Type = 123; stack.Count = 4; stack.Flags = ItemFlags.Broken;
        ch.Backpack[5] = stack;

        byte[] rebuilt = save.Build();
        Assert.Equal(fx.Bytes.Length, rebuilt.Length);

        var reloaded = SaveFile.Parse(rebuilt, "SAVE.001");
        var r = reloaded.Characters[2];
        Assert.Equal(42, r.Level);
        Assert.Equal(321.7m, r.Gold);
        Assert.Equal(99999, r.Experience);
        Assert.Equal(new CharacterAttribute(77, 88), r.Strength);
        Assert.Equal("Rainer H.", r.Name.German);
        Assert.Equal("", r.Name.English);
        Assert.Equal(123, r.Backpack[5].Type);
        Assert.Equal(4, r.Backpack[5].Count);
        Assert.Equal(ItemFlags.Broken, r.Backpack[5].Flags);

        // other characters untouched
        Assert.Equal(fx.Subfiles[0], reloaded.Characters[0].ToRawData());
        Assert.Equal(fx.Subfiles[3], reloaded.Characters[3].ToRawData());

        // every changed byte lies inside one of the edited fields
        int recordStart = fx.XldOffset + 8 + 4 * fx.Subfiles.Length + fx.Subfiles[0].Length + fx.Subfiles[1].Length;
        var allowed = new List<(int Start, int Len)>
        {
            (NPCLayout.Level, 1), (NPCLayout.Gold, 2), (NPCLayout.Experience, 4), (NPCLayout.Strength, 4),
            (NPCLayout.Name, NPCLayout.NameSlotSize), (NPCLayout.Backpack + 5 * 6, 6),
        };
        for (int i = 0; i < rebuilt.Length; i++)
        {
            if (rebuilt[i] == fx.Bytes[i]) continue;
            int rel = i - recordStart;
            Assert.True(allowed.Any(a => rel >= a.Start && rel < a.Start + a.Len), $"unexpected change at file offset {i} (record offset {rel})");
        }
    }

    [Fact]
    public void HeaderNameChange_ShiftsDataAndPreservesEverythingElse()
    {
        var fx = new SyntheticSave();
        var save = SaveFile.Parse(fx.Bytes, "SAVE.001");
        var h = new SaveGameInfo(new MemoryStream(save.Header.ToRawData()));
        const string longer = "A much longer save game name";
        h.Name = longer;
        h.Days = 99;
        save.SetHeader(h);
        save.Characters[0].Level = 50;

        byte[] rebuilt = save.Build();
        int delta = longer.Length - SyntheticSave.SaveName.Length;
        Assert.Equal(fx.Bytes.Length + delta, rebuilt.Length);

        var reloaded = SaveFile.Parse(rebuilt, "SAVE.001");
        Assert.Equal(longer, reloaded.Header.Name);
        Assert.Equal(99, reloaded.Header.Days);
        Assert.Equal(14, reloaded.Header.Hours);
        Assert.Equal(fx.XldOffset + delta, reloaded.XldOffset);
        Assert.Equal(50, reloaded.Characters[0].Level);

        // header unknowns preserved (bytes 2..3 and the 12 bytes after the name)
        Assert.Equal(fx.Bytes.AsSpan(2, 2).ToArray(), rebuilt.AsSpan(2, 2).ToArray());
        int afterOld = 4 + SyntheticSave.SaveName.Length, afterNew = 4 + longer.Length;
        Assert.Equal(fx.Bytes.AsSpan(afterOld, 12).ToArray(), rebuilt.AsSpan(afterNew, 12).ToArray());

        // gap between header and XLD and everything after the XLD preserved verbatim
        Assert.Equal(fx.Bytes.AsSpan(fx.HeaderLength, fx.XldOffset - fx.HeaderLength).ToArray(),
                     rebuilt.AsSpan(fx.HeaderLength + delta, fx.XldOffset - fx.HeaderLength).ToArray());
        int tail = fx.XldOffset + fx.XldLength;
        Assert.Equal(fx.Bytes.AsSpan(tail).ToArray(), rebuilt.AsSpan(tail + delta).ToArray());
    }

    [Fact]
    public void HeaderOnlyRewrite_WithSameValues_IsByteIdentical()
    {
        var fx = new SyntheticSave();
        var save = SaveFile.Parse(fx.Bytes, "SAVE.001");
        save.SetHeader(new SaveGameInfo(new MemoryStream(save.Header.ToRawData())));
        Assert.Equal(fx.Bytes, save.Build());
    }

    [Fact]
    public void DataAfterXld_IsPreserved_WhenCharactersChange()
    {
        var fx = new SyntheticSave();
        var save = SaveFile.Parse(fx.Bytes, "SAVE.001");
        foreach (var ch in save.Characters.Values) { ch.Level = 1; ch.Rations = 999; }
        byte[] rebuilt = save.Build();
        int tail = fx.XldOffset + fx.XldLength;
        Assert.Equal(fx.Bytes.AsSpan(0, fx.XldOffset).ToArray(), rebuilt.AsSpan(0, fx.XldOffset).ToArray());
        Assert.Equal(fx.Bytes.AsSpan(tail).ToArray(), rebuilt.AsSpan(tail).ToArray());
        // XLD header (signature, count, lengths) unchanged too
        int xldHeader = 8 + 4 * fx.Subfiles.Length;
        Assert.Equal(fx.Bytes.AsSpan(fx.XldOffset, xldHeader).ToArray(), rebuilt.AsSpan(fx.XldOffset, xldHeader).ToArray());
    }

    [Fact]
    public void TextEncoding_IsLosslessForAllBytes()
    {
        var enc = TextCore.DefaultEncoding;
        var all = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        Assert.Equal(all, enc.GetBytes(enc.GetString(all)));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3 })]
    public void Parse_Garbage_Throws(byte[] data)
    {
        Assert.Throws<SaveFormatException>(() => SaveFile.Parse(data, "x"));
    }

    [Fact]
    public void Parse_TruncatedXld_Throws()
    {
        var fx = new SyntheticSave();
        var cut = fx.Bytes.AsSpan(0, fx.XldOffset + 500).ToArray();
        Assert.Throws<SaveFormatException>(() => SaveFile.Parse(cut, "x"));
    }

    [Fact]
    public void Parse_NoXld_Throws()
    {
        var fx = new SyntheticSave();
        var cut = fx.Bytes.AsSpan(0, fx.XldOffset).ToArray();
        Assert.Throws<SaveFormatException>(() => SaveFile.Parse(cut, "x"));
    }
}
