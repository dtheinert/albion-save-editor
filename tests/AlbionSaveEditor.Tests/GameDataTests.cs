using AlbionSaveEditor.Services;

namespace AlbionSaveEditor.Tests;

// GameDataService pushes names into AlbLib's static caches, so these tests must not run in
// parallel with each other or with the real-data tests.
[CollectionDefinition("GameData", DisableParallelization = true)]
public class GameDataCollection;

[Collection("GameData")]
public class GameDataTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "albion-editor-test-" + Guid.NewGuid().ToString("N"));

    private static readonly byte[] Names = SyntheticCdImage.ItemNames(
        ("Messer", "knife", "couteau"), ("Fackel", "torch", "torche"), ("Stiefel", "boots", "bottes"));
    private static readonly byte[] List = new byte[3 * 40];
    private const string SysTexts = "\t*** Spell names for class 0 ***\r\n[0203:Thorn snare]\r\n[0204:DELETED]\r\n[0233:Regeneration]\r\n";

    public GameDataTests() => Directory.CreateDirectory(dir);
    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    [Theory]
    [InlineData(2048, 0)]
    [InlineData(2352, 24)] // Mode 2 Form 1 (GOG game.gog)
    [InlineData(2352, 16)] // Mode 1
    public void CdImageReader_FindsFilesCaseInsensitive(int sectorSize, int offset)
    {
        var img = SyntheticCdImage.Build(Names, List, SysTexts, sectorSize, offset);
        using var reader = CdImageReader.TryOpen(new MemoryStream(img))!;
        Assert.NotNull(reader);
        Assert.Equal(Names, reader.ReadFile("/ALBION/XLDLIBS/ITEMNAME.DAT"));
        Assert.Equal(Names, reader.ReadFile("albion\\xldlibs\\itemname.dat;1"));
        Assert.Equal(List, reader.ReadFile("ALBION/XLDLIBS/ITEMLIST.DAT"));
        Assert.Equal(SysTexts, System.Text.Encoding.Latin1.GetString(reader.ReadFile("ALBION/XLDLIBS/ENGLISH/SYSTEXTS")!));
        Assert.Null(reader.ReadFile("ALBION/XLDLIBS/NOPE.DAT"));
        Assert.Contains("ITEMNAME.DAT", reader.List("ALBION/XLDLIBS"));
    }

    [Fact]
    public void CdImageReader_RejectsNonIso()
    {
        Assert.Null(CdImageReader.TryOpen(new MemoryStream(new byte[100_000])));
    }

    [Fact]
    public void Configure_FromFolderWithGameGog_LoadsEnglishItemAndSpellNames()
    {
        File.WriteAllBytes(Path.Combine(dir, "game.gog"), SyntheticCdImage.Build(Names, List, SysTexts, 2352, 24));
        Directory.CreateDirectory(Path.Combine(dir, "XLDLIBS")); // like GOG: XLDLIBS without item data
        var svc = new GameDataService();
        var r = svc.Configure(dir);
        Assert.Equal(3, r.ItemCount);
        Assert.Equal(2, r.SpellCount); // DELETED is skipped
        Assert.Contains("game.gog", r.Source);
        Assert.Equal("torch", svc.GetItemName(2));
        Assert.Equal("", svc.GetItemName(99));
        Assert.Equal("Thorn snare", svc.GetSpellName(0, 0));
        Assert.Equal("", svc.GetSpellName(0, 1));
        Assert.Equal("Regeneration", svc.GetSpellName(1, 0));
        Assert.Equal("boots", AlbLib.Texts.TextCore.GetItemName(3)); // library sees in-memory names too
    }

    [Fact]
    public void Configure_DirectImagePath_And_PlainFolder()
    {
        string iso = Path.Combine(dir, "albion.iso");
        File.WriteAllBytes(iso, SyntheticCdImage.Build(Names, List, SysTexts));
        Assert.Equal(3, new GameDataService().Configure(iso).ItemCount);

        string root = Path.Combine(dir, "install");
        Directory.CreateDirectory(Path.Combine(root, "XLDLIBS"));
        File.WriteAllBytes(Path.Combine(root, "XLDLIBS", "ITEMNAME.DAT"), Names);
        var svc = new GameDataService();
        var r = svc.Configure(root);
        Assert.StartsWith("folder", r.Source);
        Assert.Equal("knife", svc.GetItemName(1));
        Assert.Equal(0, r.SpellCount);
    }

    [Fact]
    public void Configure_NothingFound_ThrowsReadableError()
    {
        var e = Assert.Throws<ArgumentException>(() => new GameDataService().Configure(dir));
        Assert.Contains("ITEMNAME.DAT", e.Message);
    }
}
