using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AlbLib.Items;
using AlbLib.SaveGame;
using AlbionSaveEditor.Domain;
using AlbionSaveEditor.Services;

namespace AlbionSaveEditor.Tests;

/// <summary>Synthetic ITEMLIST.DAT / ITEMNAME.DAT with a sword, a stackable drink and a charged wand.</summary>
public static class SyntheticItems
{
    public const int Sword = 1, Stimdrink = 2, Wand = 3;

    public static readonly byte[] Names = SyntheticCdImage.ItemNames(
        ("Schwert", "sword", "epee"), ("Stimdrink", "stimdrink", "stimdrink"), ("Stab", "wand", "baguette"));

    public static byte[] List()
    {
        var d = new byte[3 * 40];
        Def(d, Sword, ItemClass.CloseRangeWeapon, ItemSlot.RightHand, 0, 0, ItemTypeFlags.None);
        Def(d, Stimdrink, ItemClass.Drink, ItemSlot.Inventory, 0, 0, ItemTypeFlags.Stackable);
        Def(d, Wand, ItemClass.MagicalItem, ItemSlot.RightHand, maxCharges: 10, maxEnchant: 3, ItemTypeFlags.PlotItem | ItemTypeFlags.Cursed);
        return d;
    }

    private static void Def(byte[] d, int id, ItemClass cls, ItemSlot slot, byte maxCharges, byte maxEnchant, ItemTypeFlags flags)
    {
        int o = (id - 1) * 40;
        d[o + 1] = (byte)cls;
        d[o + 2] = (byte)slot;
        d[o + 25] = maxEnchant;
        d[o + 26] = maxCharges;
        BitConverter.GetBytes((ushort)flags).CopyTo(d, o + 27);
    }

    /// <summary>Writes XLDLIBS/ITEMNAME.DAT + ITEMLIST.DAT under a new temp folder and returns it.</summary>
    public static string CreateInstallFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), "albion-items-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "XLDLIBS"));
        File.WriteAllBytes(Path.Combine(root, "XLDLIBS", "ITEMNAME.DAT"), Names);
        File.WriteAllBytes(Path.Combine(root, "XLDLIBS", "ITEMLIST.DAT"), List());
        return root;
    }
}

[Collection("GameData")]
public class ItemLimitTests : IClassFixture<EditorFactory>, IDisposable
{
    private readonly HttpClient client;
    private readonly HttpClient plainClient;
    private readonly string root = SyntheticItems.CreateInstallFolder();

    public ItemLimitTests(EditorFactory factory)
    {
        // a separate host instance with game data, so other test classes keep running without it
        client = factory.WithWebHostBuilder(_ => { }).CreateClient();
        plainClient = factory.CreateClient();
        var res = client.PostAsJsonAsync("/api/gamedata", new { path = root }).GetAwaiter().GetResult();
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch (IOException) { }
    }

    /// <summary>
    /// Synthetic save where Tom's backpack holds: [0] item 17 x2 (unknown id), [1] sword x5 (over the limit),
    /// [2] stimdrink x3, [3] wand with 4 charges.
    /// </summary>
    private static byte[] SaveWithItems()
    {
        var fx = new SyntheticSave();
        var save = SaveFile.Parse(fx.Bytes, "SAVE.001");
        var tom = save.Characters[0];
        tom.Backpack[1] = new ItemStack(5, SyntheticItems.Sword);
        tom.Backpack[2] = new ItemStack(3, SyntheticItems.Stimdrink);
        tom.Backpack[3] = new ItemStack(1, SyntheticItems.Wand) { Charges = 4 };
        tom.Backpack[4] = default;
        return save.Build();
    }

    private async Task<(string Sid, JsonObject Tom, byte[] Original)> Load(HttpClient c)
    {
        var bytes = SaveWithItems();
        var sid = (string)(await ApiTests.Upload(c, bytes))["sessionId"]!;
        var tom = (await c.GetFromJsonAsync<JsonObject>($"/api/saves/{sid}/characters/0"))!;
        return (sid, tom, bytes);
    }

    private async Task<JsonObject> PutExpect400(HttpClient c, string sid, JsonObject ch, string field)
    {
        var res = await c.PutAsJsonAsync($"/api/saves/{sid}/characters/0", ch);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = (await res.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(field, (string?)body["field"]);
        return body;
    }

    [Fact]
    public async Task Meta_ItemsCarryLimits()
    {
        var meta = (await client.GetFromJsonAsync<JsonObject>("/api/meta"))!;
        Assert.Equal(99, (int)meta["limits"]!["stackMax"]!);
        var items = meta["items"]!.AsArray();
        Assert.Equal(3, items.Count);
        var drink = items[1]!;
        Assert.Equal("stimdrink", (string?)drink["name"]);
        Assert.True((bool)drink["stackable"]!);
        Assert.Equal(99, (int)drink["maxCount"]!);
        Assert.Equal("Drink", (string?)drink["class"]);
        Assert.Equal("Inventory", (string?)drink["slot"]);
        var sword = items[0]!;
        Assert.False((bool)sword["stackable"]!);
        Assert.Equal(1, (int)sword["maxCount"]!);
        Assert.Equal("RightHand", (string?)sword["slot"]);
        var wand = items[2]!;
        Assert.Equal(10, (int)wand["maxCharges"]!);
        Assert.Equal(3, (int)wand["maxEnchantments"]!);
        Assert.True((bool)wand["plotItem"]!);
        Assert.True((bool)wand["cursed"]!);
    }

    [Fact]
    public async Task UntouchedOverLimitStacks_AreAccepted_AndByteIdentical()
    {
        var (sid, tom, original) = await Load(client);
        Assert.Equal(5, (int)tom["backpack"]![1]!["count"]!); // sword x5 from the save
        var res = await client.PutAsJsonAsync($"/api/saves/{sid}/characters/0", tom);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(original, await ApiTests.Download(client, sid));
    }

    [Fact]
    public async Task StackableCountAbove99_Returns400()
    {
        var (sid, tom, _) = await Load(client);
        tom["backpack"]![2]!["count"] = 100;
        await PutExpect400(client, sid, tom, "backpack[2].count");
        tom["backpack"]![2]!["count"] = 99;
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/saves/{sid}/characters/0", tom)).StatusCode);
    }

    [Fact]
    public async Task NonStackableCount2_Returns400()
    {
        var (sid, tom, _) = await Load(client);
        tom["backpack"]![10]!["itemId"] = SyntheticItems.Sword;
        tom["backpack"]![10]!["count"] = 2;
        await PutExpect400(client, sid, tom, "backpack[10].count");
        // count 0 with a real item is not allowed either
        tom["backpack"]![10]!["count"] = 0;
        await PutExpect400(client, sid, tom, "backpack[10].count");
    }

    [Fact]
    public async Task ChargesAndEnchantmentAboveMax_Return400()
    {
        var (sid, tom, _) = await Load(client);
        tom["backpack"]![3]!["charges"] = 11;
        await PutExpect400(client, sid, tom, "backpack[3].charges");
        tom["backpack"]![3]!["charges"] = 10;
        tom["backpack"]![3]!["enchantment"] = 4;
        await PutExpect400(client, sid, tom, "backpack[3].enchantment");
        tom["backpack"]![3]!["enchantment"] = 3;
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/saves/{sid}/characters/0", tom)).StatusCode);
    }

    [Fact]
    public async Task UnknownItemId_Returns400()
    {
        var (sid, tom, _) = await Load(client);
        tom["equipment"]!["head"]!["itemId"] = 50;
        tom["equipment"]!["head"]!["count"] = 1;
        await PutExpect400(client, sid, tom, "equipment.head.itemId");
    }

    [Fact]
    public async Task EmptiedSlot_IsNormalisedToZero()
    {
        var (sid, tom, _) = await Load(client);
        tom["backpack"]![3]!["itemId"] = 0; // wand with 4 charges -> empty
        var res = await client.PutAsJsonAsync($"/api/saves/{sid}/characters/0", tom);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var slot = SaveFile.Parse(await ApiTests.Download(client, sid), "x").Characters[0].Backpack[3];
        Assert.Equal(default(ItemStack), slot);
    }

    [Fact]
    public async Task WithoutGameData_OnlyByteRangesApply()
    {
        var (sid, tom, _) = await Load(plainClient);
        tom["backpack"]![10]!["itemId"] = SyntheticItems.Sword;
        tom["backpack"]![10]!["count"] = 200;
        tom["backpack"]![10]!["charges"] = 250;
        Assert.Equal(HttpStatusCode.OK, (await plainClient.PutAsJsonAsync($"/api/saves/{sid}/characters/0", tom)).StatusCode);
        tom["backpack"]![10]!["count"] = 256;
        await PutExpect400(plainClient, sid, tom, "backpack[10].count");
    }

    [Fact]
    public void ItemType_FlagsReadFromOffset27()
    {
        var types = ItemType.ReadItemTypes(new MemoryStream(SyntheticItems.List()));
        Assert.False(types[0].IsStackable);
        Assert.True(types[1].IsStackable);
        Assert.Equal(ItemTypeFlags.PlotItem | ItemTypeFlags.Cursed, types[2].Flags);
        Assert.Equal(10, types[2].MaxCharges);
        Assert.Equal(3, types[2].MaxNumRecharged);
    }

    [Fact]
    public void RealGameData_StackableItems()
    {
        string? path = Environment.GetEnvironmentVariable("ALBION_PATH");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(path), "ALBION_PATH is not set.");
        var gd = new GameDataService();
        gd.Configure(path!);
        Assert.True(gd.HasItemDefinitions);
        var drink = gd.GetItem(291)!;
        Assert.Equal("stimdrink", drink.Name);
        Assert.True(drink.Stackable);
        Assert.Equal(99, drink.MaxCount);
        var overall = gd.GetItem(378)!;
        Assert.Equal("overall", overall.Name);
        Assert.False(overall.Stackable);
        Assert.Equal(1, overall.MaxCount);
        Assert.Equal(225, gd.Items.Count(i => i.Stackable));
    }
}
