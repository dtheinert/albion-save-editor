using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using AlbionSaveEditor.Domain;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AlbionSaveEditor.Tests;

public class ApiTests(EditorFactory factory) : IClassFixture<EditorFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    internal static async Task<JsonObject> Upload(HttpClient client, byte[] bytes, string fileName = "SAVE.001")
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, "file", fileName);
        var res = await client.PostAsync("/api/saves", form);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    internal static async Task<byte[]> Download(HttpClient client, string sessionId)
    {
        var res = await client.GetAsync($"/api/saves/{sessionId}/download");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("application/octet-stream", res.Content.Headers.ContentType?.MediaType);
        return await res.Content.ReadAsByteArrayAsync();
    }

    private async Task<JsonObject> GetCharacter(string sid, int index)
    {
        var res = await client.GetAsync($"/api/saves/{sid}/characters/{index}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    [Fact]
    public async Task Upload_ReturnsHeaderAndCharacters()
    {
        var save = await Upload(client, new SyntheticSave().Bytes);
        Assert.False(string.IsNullOrEmpty((string?)save["sessionId"]));
        Assert.Equal("SAVE.001", (string?)save["fileName"]);
        var header = save["header"]!.AsObject();
        Assert.Equal(SyntheticSave.SaveName, (string?)header["name"]);
        Assert.Equal("1.38", (string?)header["version"]);
        Assert.Equal(300, (int)header["mapId"]!);
        var chars = save["characters"]!.AsArray();
        Assert.Equal(new[] { 0, 2, 3, 4 }, chars.Select(c => (int)c!["index"]!).ToArray());
        Assert.Equal(new[] { false, false, false, true }, chars.Select(c => (bool)c!["empty"]!).ToArray());
        Assert.Equal("Tom", (string?)chars[0]!["name"]);
        Assert.Equal("Terran", (string?)chars[0]!["race"]);
        Assert.Equal("Pilot", (string?)chars[0]!["class"]);
        Assert.Equal(3, (int)chars[0]!["level"]!);
    }

    [Fact]
    public async Task Upload_Garbage_Returns400()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(new byte[100]), "file", "junk.bin");
        var res = await client.PostAsync("/api/saves", form);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonObject>();
        Assert.False(string.IsNullOrEmpty((string?)body!["error"]));
    }

    [Fact]
    public async Task Download_Unedited_IsByteIdentical()
    {
        var fx = new SyntheticSave();
        var sid = (string)(await Upload(client, fx.Bytes))["sessionId"]!;
        Assert.Equal(fx.Bytes, await Download(client, sid));
    }

    [Fact]
    public async Task GetThenPutEveryCharacterAndHeader_Unchanged_IsByteIdentical()
    {
        var fx = new SyntheticSave(seed: 777);
        var save = await Upload(client, fx.Bytes);
        var sid = (string)save["sessionId"]!;

        var hdr = await client.PutAsJsonAsync($"/api/saves/{sid}/header", save["header"]);
        Assert.Equal(HttpStatusCode.OK, hdr.StatusCode);

        foreach (int i in SyntheticSave.CharacterIndexes)
        {
            var ch = await GetCharacter(sid, i);
            var put = await client.PutAsJsonAsync($"/api/saves/{sid}/characters/{i}", ch);
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            var returned = await put.Content.ReadFromJsonAsync<JsonObject>();
            Assert.True(JsonNode.DeepEquals(ch, returned));
        }
        Assert.Equal(fx.Bytes, await Download(client, sid));
    }

    [Fact]
    public async Task Character_HasContractShape()
    {
        var sid = (string)(await Upload(client, new SyntheticSave().Bytes))["sessionId"]!;
        var ch = await GetCharacter(sid, 0);
        foreach (var key in new[] { "index", "name", "type", "gender", "race", "class", "magic", "language", "level",
                     "actionPoints", "appearance", "face", "inventoryPicture", "trainingPoints", "gold", "rations",
                     "conditions", "experience", "lifePoints", "spellPoints", "age", "morale", "attributes", "skills", "spells",
                     "equipment", "backpack", "other" })
            Assert.True(ch.ContainsKey(key), $"missing {key}");
        Assert.Equal(12.5m, (decimal)ch["gold"]!);
        Assert.False((bool)ch["empty"]!);
        Assert.Equal("", (string?)ch["other"]!["nameSlot2"]);
        Assert.Equal("", (string?)ch["other"]!["nameSlot3"]);
        Assert.True((bool)(await GetCharacter(sid, SyntheticSave.EmptyRecordIndex))["empty"]!);
        Assert.Equal(30, (int)ch["attributes"]!["strength"]!["current"]!);
        Assert.Equal(50, (int)ch["attributes"]!["strength"]!["max"]!);
        Assert.Equal(8, ch["attributes"]!.AsObject().Count);
        Assert.Equal(4, ch["skills"]!.AsObject().Count);
        Assert.Equal(9, ch["equipment"]!.AsObject().Count);
        Assert.Equal(24, ch["backpack"]!.AsArray().Count);
        Assert.Equal(210, ch["spells"]!.AsArray().Count);
        var item = ch["backpack"]![0]!;
        Assert.Equal(17, (int)item["itemId"]!);
        Assert.Equal(2, (int)item["count"]!);
        Assert.Equal("", (string?)item["itemName"]);
        Assert.Equal(JsonValueKind.Array, item["flags"]!.GetValueKind());
    }

    [Fact]
    public async Task EditCharacter_ThenDownloadAndReload_HasNewValues()
    {
        var fx = new SyntheticSave();
        var sid = (string)(await Upload(client, fx.Bytes))["sessionId"]!;
        var ch = await GetCharacter(sid, 2);
        ch["level"] = 99;
        ch["gold"] = 1000.5m;
        ch["name"] = "Rainer X";
        ch["conditions"] = new JsonArray("Poisoned", "Blind");
        ch["attributes"]!["luck"]!["current"] = 55;
        ch["attributes"]!["luck"]!["max"] = 99;
        ch["backpack"]![10]!["itemId"] = 42;
        ch["backpack"]![10]!["count"] = 3;
        ch["backpack"]![10]!["enchantment"] = 7;
        ch["backpack"]![10]!["flags"] = new JsonArray("Cursed");
        ch["age"] = new JsonObject { ["current"] = 61, ["max"] = 80 };
        ch["morale"] = 42;
        var spell = ch["spells"]!.AsArray().First(s => (int)s!["school"]! == 2 && (int)s["id"]! == 4)!;
        spell["known"] = true;
        spell["strength"] = 250;

        var put = await client.PutAsJsonAsync($"/api/saves/{sid}/characters/2", ch);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var reloaded = SaveFile.Parse(await Download(client, sid), "SAVE.001").Characters[2];
        Assert.Equal(99, reloaded.Level);
        Assert.Equal(1000.5m, reloaded.Gold);
        Assert.Equal("Rainer X", reloaded.Name.German);
        Assert.True(reloaded.Conditions.HasFlag(AlbLib.SaveGame.ConditionFlags.Poisoned));
        Assert.True(reloaded.Conditions.HasFlag(AlbLib.SaveGame.ConditionFlags.Blind));
        Assert.False(reloaded.Conditions.HasFlag(AlbLib.SaveGame.ConditionFlags.Asleep));
        Assert.Equal(55, reloaded.Luck.Value);
        Assert.Equal(42, reloaded.Backpack[10].Type);
        Assert.Equal(3, reloaded.Backpack[10].Count);
        Assert.Equal(7, reloaded.Backpack[10].NumRecharged);
        // named bits replaced, undocumented bits (random fixture data) preserved
        var defined = AlbLib.SaveGame.ItemFlags.ShowMoreInfo | AlbLib.SaveGame.ItemFlags.Broken | AlbLib.SaveGame.ItemFlags.Cursed;
        Assert.Equal(AlbLib.SaveGame.ItemFlags.Cursed, reloaded.Backpack[10].Flags & defined);
        Assert.Equal(new AlbLib.SaveGame.CharacterAttribute(61, 80), reloaded.Age);
        Assert.Equal(42, reloaded.Morale);
        Assert.NotEqual(0, reloaded.Spells[2] & (1 << 5)); // masks are 1-based: spell index 4 = bit 5
        Assert.Equal(250, reloaded.SpellStrengths[2, 4]);

        // a second session from the downloaded file shows the same values
        var after = await GetCharacter((string)(await Upload(client, await Download(client, sid)))["sessionId"]!, 2);
        Assert.Equal(99, (int)after["level"]!);
        Assert.Equal("Rainer X", (string?)after["name"]);
    }

    [Theory]
    [InlineData("level", 256, "level")]
    [InlineData("level", -1, "level")]
    [InlineData("trainingPoints", 32768, "trainingPoints")]
    [InlineData("experience", 3000000000, "experience")]
    [InlineData("attributes.strength.current", 40000, "attributes.strength.current")]
    [InlineData("skills.lockpicking.max", -40000, "skills.lockpicking.max")]
    [InlineData("lifePoints.max", 70000, "lifePoints.max")]
    [InlineData("backpack.3.count", 256, "backpack[3].count")]
    [InlineData("equipment.head.itemId", -5, "equipment.head.itemId")]
    [InlineData("age.max", 101, "age.max")]
    [InlineData("morale", 101, "morale")]
    [InlineData("spells.5.strength", 40000, "spells[5].strength")]
    public async Task PutCharacter_OutOfRange_Returns400WithField(string path, long value, string expectedField)
    {
        var fx = new SyntheticSave();
        var sid = (string)(await Upload(client, fx.Bytes))["sessionId"]!;
        var ch = await GetCharacter(sid, 0);
        SetPath(ch, path, value);

        var res = await client.PutAsJsonAsync($"/api/saves/{sid}/characters/0", ch);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = (await res.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(expectedField, (string?)body["field"]);
        Assert.False(string.IsNullOrEmpty((string?)body["error"]));

        // failed update must not change anything
        Assert.Equal(fx.Bytes, await Download(client, sid));
    }

    [Theory]
    [InlineData("gold", "6553.6")]
    [InlineData("gold", "-0.1")]
    [InlineData("gold", "1.25")]
    [InlineData("race", "\"Klingon\"")]
    [InlineData("magic", "[\"Necromancy\"]")]
    [InlineData("name", "\"ThisNameIsWayTooLong\"")]
    [InlineData("level", "\"abc\"")]
    [InlineData("level", "1.5")]
    public async Task PutCharacter_InvalidValue_Returns400WithField(string field, string json)
    {
        var sid = (string)(await Upload(client, new SyntheticSave().Bytes))["sessionId"]!;
        var ch = await GetCharacter(sid, 0);
        ch[field] = JsonNode.Parse(json);
        var res = await client.PutAsJsonAsync($"/api/saves/{sid}/characters/0", ch);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = (await res.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.StartsWith(field, (string?)body["field"]);
    }

    [Fact]
    public async Task PutHeader_ChangesNameAndValidatesRange()
    {
        var fx = new SyntheticSave();
        var save = await Upload(client, fx.Bytes);
        var sid = (string)save["sessionId"]!;
        var header = save["header"]!.AsObject();

        header["minutes"] = 40000;
        var bad = await client.PutAsJsonAsync($"/api/saves/{sid}/header", header);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("minutes", (string?)(await bad.Content.ReadFromJsonAsync<JsonObject>())!["field"]);

        header["minutes"] = 30;
        header["name"] = "Renamed by the editor!";
        header["version"] = "9.99"; // read-only, ignored
        var ok = await client.PutAsJsonAsync($"/api/saves/{sid}/header", header);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var returned = (await ok.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal("1.38", (string?)returned["version"]);

        var bytes = await Download(client, sid);
        var reloaded = SaveFile.Parse(bytes, "SAVE.001");
        Assert.Equal("Renamed by the editor!", reloaded.Header.Name);
        Assert.Equal(30, reloaded.Header.Minutes);
        Assert.Equal(fx.Bytes.Length + ("Renamed by the editor!".Length - SyntheticSave.SaveName.Length), bytes.Length);
        foreach (int i in SyntheticSave.CharacterIndexes)
            Assert.Equal(fx.Subfiles[i], reloaded.Characters[i].ToRawData());
    }

    [Fact]
    public async Task UnknownSessionOrCharacter_Returns404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/saves/nope")).StatusCode);
        var sid = (string)(await Upload(client, new SyntheticSave().Bytes))["sessionId"]!;
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/saves/{sid}/characters/1")).StatusCode);
    }

    [Fact]
    public async Task Meta_ListsEnumsFromLibrary()
    {
        var meta = (await client.GetFromJsonAsync<JsonObject>("/api/meta"))!;
        var enums = meta["enums"]!.AsObject();
        foreach (var name in new[] { "gender", "race", "class", "type", "magic", "language", "conditions", "itemFlags" })
            Assert.True(enums.ContainsKey(name), name);
        Assert.False((bool)enums["gender"]!["flags"]!);
        Assert.True((bool)enums["conditions"]!["flags"]!);
        var races = enums["race"]!["values"]!.AsArray().Select(v => (string)v!["name"]!).ToList();
        Assert.Equal(Enum.GetNames<AlbLib.SaveGame.Race>().Length, races.Count);
        Assert.Contains("Iskai", races);
        Assert.Equal(255, (int)meta["limits"]!["level"]!["max"]!);
        Assert.Equal(JsonValueKind.Array, meta["items"]!.GetValueKind());
    }

    [Fact]
    public async Task GameData_InvalidPath_Returns400()
    {
        var res = await client.PostAsJsonAsync("/api/gamedata", new { path = Path.Combine(Path.GetTempPath(), "definitely-not-albion-" + Guid.NewGuid()) });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    private static void SetPath(JsonNode root, string path, long value)
    {
        var parts = path.Split('.');
        JsonNode node = root;
        for (int i = 0; i < parts.Length - 1; i++)
            node = int.TryParse(parts[i], out int idx) ? node[idx]! : node[parts[i]]!;
        string last = parts[^1];
        if (int.TryParse(last, out int li)) node[li] = value; else node[last] = value;
    }
}
