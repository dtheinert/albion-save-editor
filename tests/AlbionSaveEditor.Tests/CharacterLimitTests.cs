using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AlbLib.SaveGame;
using AlbionSaveEditor.Domain;

namespace AlbionSaveEditor.Tests;

/// <summary>"Character field limits &amp; corrections": changed-only range checks, read-only fields, current &lt;= max.</summary>
public class CharacterLimitTests(EditorFactory factory) : IClassFixture<EditorFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    /// <summary>
    /// Synthetic save where Tom (record 0) has sane known values plus some values that are already
    /// out of range (must be accepted while untouched): intelligence 150/120, rations -5, header hours 30.
    /// </summary>
    private static byte[] Save()
    {
        var fx = new SyntheticSave();
        var save = SaveFile.Parse(fx.Bytes, "SAVE.001");
        var tom = save.Characters[0];
        tom.Level = 5;
        tom.Morale = 50;
        tom.Gold = 100m;
        tom.Rations = -5;                                 // out of range, untouched
        tom.TrainingPoints = 10;
        tom.Experience = 400;
        tom.Strength = new CharacterAttribute(30, 50);
        tom.Intelligence = new CharacterAttribute(150, 120); // out of range, untouched
        tom.CloseRangeCombat = new CharacterAttribute(35, 80);
        tom.Age = new CharacterAttribute(28, 80);
        tom.LifePoints = new CharacterAttribute(12, 20);
        tom.SpellPoints = new CharacterAttribute(0, 0);
        tom.EventSetId = 981;
        tom.BonusAttack = -5;
        var h = new SaveGameInfo(new MemoryStream(save.Header.ToRawData())) { Hours = 30 }; // out of range, untouched
        save.SetHeader(h);
        return save.Build();
    }

    private async Task<(string Sid, JsonObject Tom, JsonObject Header, byte[] Bytes)> Load()
    {
        var bytes = Save();
        var up = await ApiTests.Upload(client, bytes);
        var sid = (string)up["sessionId"]!;
        var tom = (await client.GetFromJsonAsync<JsonObject>($"/api/saves/{sid}/characters/0"))!;
        return (sid, tom, up["header"]!.AsObject(), bytes);
    }

    private async Task<HttpResponseMessage> Put(string sid, JsonObject ch)
        => await client.PutAsJsonAsync($"/api/saves/{sid}/characters/0", ch);

    private static void Set(JsonNode root, string path, JsonNode? value)
    {
        var parts = path.Split('.');
        var node = root;
        foreach (var p in parts[..^1]) node = node[p]!;
        node[parts[^1]] = value;
    }

    [Fact]
    public async Task Get_ExposesCorrectedFields()
    {
        var (_, tom, _, _) = await Load();
        Assert.Equal(28, (int)tom["age"]!["current"]!);
        Assert.Equal(80, (int)tom["age"]!["max"]!);
        Assert.Equal(50, (int)tom["morale"]!);
        Assert.Equal(981, (int)tom["other"]!["eventSetId"]!);
        Assert.Equal(-5, (int)tom["other"]!["bonusAttack"]!);
        foreach (var k in new[] { "wordSetId", "baseDefense", "bonusDefense", "baseAttack", "levelsPerActionPoint",
                     "lifePointsPerLevel", "spellPointsPerLevel", "trainingPointsPerLevel" })
            Assert.True(tom["other"]!.AsObject().ContainsKey(k), k);
        foreach (var k in new[] { "dialogueOptions", "responseOptions", "baseProtection", "protection", "baseDamage", "damage", "age" })
            Assert.False(tom["other"]!.AsObject().ContainsKey(k), k);
    }

    [Fact]
    public async Task UntouchedOutOfRangeValues_AreAccepted_AndByteIdentical()
    {
        var (sid, tom, header, bytes) = await Load();
        Assert.Equal(HttpStatusCode.OK, (await Put(sid, tom)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/saves/{sid}/header", header)).StatusCode);
        Assert.Equal(bytes, await ApiTests.Download(client, sid));
    }

    [Theory]
    [InlineData("attributes.strength.max", "100", "attributes.strength.max")]
    [InlineData("attributes.strength.max", "-1", "attributes.strength.max")]
    [InlineData("attributes.strength.current", "51", "attributes.strength.current")]
    [InlineData("attributes.strength.current", "-1", "attributes.strength.current")]
    [InlineData("attributes.strength.max", "20", "attributes.strength.max")] // would leave current 30 > max 20
    [InlineData("skills.closeCombat.max", "100", "skills.closeCombat.max")]
    [InlineData("skills.closeCombat.current", "81", "skills.closeCombat.current")]
    [InlineData("age.max", "101", "age.max")]
    [InlineData("age.current", "81", "age.current")]
    [InlineData("lifePoints.max", "32768", "lifePoints.max")]
    [InlineData("lifePoints.current", "21", "lifePoints.current")]
    [InlineData("spellPoints.current", "1", "spellPoints.current")]
    [InlineData("gold", "6553.6", "gold")]
    [InlineData("gold", "-0.1", "gold")]
    [InlineData("rations", "32768", "rations")]
    [InlineData("rations", "-6", "rations")]
    [InlineData("trainingPoints", "-1", "trainingPoints")]
    [InlineData("trainingPoints", "32768", "trainingPoints")]
    [InlineData("experience", "-1", "experience")]
    [InlineData("level", "256", "level")]
    [InlineData("actionPoints", "256", "actionPoints")]
    [InlineData("appearance", "-1", "appearance")]
    [InlineData("face", "256", "face")]
    [InlineData("inventoryPicture", "256", "inventoryPicture")]
    [InlineData("morale", "101", "morale")]
    [InlineData("attributes.intelligence.current", "121", "attributes.intelligence.current")] // touching an out-of-range pair
    public async Task ChangedOutOfRange_Returns400(string path, string json, string field)
    {
        var (sid, tom, _, bytes) = await Load();
        Set(tom, path, JsonNode.Parse(json));
        var res = await Put(sid, tom);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = (await res.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(field, (string?)body["field"]);
        Assert.Equal(bytes, await ApiTests.Download(client, sid)); // atomic
    }

    [Theory]
    [InlineData("attributes.strength.max", "99")]
    [InlineData("attributes.strength.current", "50")]
    [InlineData("age.max", "100")]
    [InlineData("gold", "6553.5")]
    [InlineData("gold", "0")]
    [InlineData("rations", "32767")]
    [InlineData("experience", "2147483647")]
    [InlineData("morale", "100")]
    [InlineData("lifePoints.max", "32767")]
    public async Task ChangedInRange_IsAccepted(string path, string json)
    {
        var (sid, tom, _, _) = await Load();
        Set(tom, path, JsonNode.Parse(json));
        Assert.Equal(HttpStatusCode.OK, (await Put(sid, tom)).StatusCode);
    }

    [Fact]
    public async Task LoweringMaxTogetherWithCurrent_IsAccepted()
    {
        var (sid, tom, _, _) = await Load();
        tom["attributes"]!["strength"]!["current"] = 20;
        tom["attributes"]!["strength"]!["max"] = 20;
        Assert.Equal(HttpStatusCode.OK, (await Put(sid, tom)).StatusCode);
    }

    [Theory]
    [InlineData("eventSetId")]
    [InlineData("wordSetId")]
    [InlineData("baseDefense")]
    [InlineData("bonusDefense")]
    [InlineData("baseAttack")]
    [InlineData("bonusAttack")]
    [InlineData("levelsPerActionPoint")]
    [InlineData("lifePointsPerLevel")]
    [InlineData("spellPointsPerLevel")]
    [InlineData("trainingPointsPerLevel")]
    public async Task ChangingReadOnlyField_Returns400(string key)
    {
        var (sid, tom, _, _) = await Load();
        tom["other"]![key] = (int)tom["other"]![key]! + 1;
        var res = await Put(sid, tom);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = (await res.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal($"other.{key}", (string?)body["field"]);
        Assert.Equal($"'{key}' is read-only", (string?)body["error"]);
    }

    [Theory]
    [InlineData("hours", 24)]
    [InlineData("hours", -1)]
    [InlineData("minutes", 60)]
    [InlineData("days", -1)]
    [InlineData("mapId", -1)]
    [InlineData("partyX", -1)]
    [InlineData("partyY", -1)]
    public async Task HeaderChangedOutOfRange_Returns400(string key, int value)
    {
        var (sid, _, header, _) = await Load();
        header[key] = value;
        var res = await client.PutAsJsonAsync($"/api/saves/{sid}/header", header);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(key, (string?)(await res.Content.ReadFromJsonAsync<JsonObject>())!["field"]);
    }

    [Fact]
    public async Task EditedMoraleAgeGold_LandAtTheirOffsets()
    {
        var (sid, tom, _, _) = await Load();
        tom["morale"] = 77;
        tom["age"] = new JsonObject { ["current"] = 30, ["max"] = 90 };
        tom["gold"] = 6000.5m;
        Assert.Equal(HttpStatusCode.OK, (await Put(sid, tom)).StatusCode);
        var bytes = await ApiTests.Download(client, sid);
        var save = SaveFile.Parse(bytes, "x");
        var raw = save.Characters[0].ToRawData();
        Assert.Equal(77, raw[NPCLayout.Morale]);
        Assert.Equal(30, BitConverter.ToInt16(raw, NPCLayout.Age));
        Assert.Equal(90, BitConverter.ToInt16(raw, NPCLayout.Age + 2));
        Assert.Equal(60005, BitConverter.ToUInt16(raw, NPCLayout.Gold)); // unsigned tenths
        Assert.Equal(6000.5m, save.Characters[0].Gold);
    }

    [Fact]
    public async Task Meta_HasPathLimitsReadOnlyAndRules()
    {
        var meta = (await client.GetFromJsonAsync<JsonObject>("/api/meta"))!;
        var limits = meta["limits"]!;
        Assert.Equal(99, (int)limits["attributes.max"]!["max"]!);
        Assert.Equal(99, (int)limits["skills.max"]!["max"]!);
        Assert.Equal(100, (int)limits["age.max"]!["max"]!);
        Assert.Equal(32767, (int)limits["lifePoints.max"]!["max"]!);
        Assert.Equal(6553.5m, (decimal)limits["gold"]!["max"]!);
        Assert.Equal(0, (int)limits["gold"]!["min"]!);
        Assert.Equal(100, (int)limits["morale"]!["max"]!);
        Assert.Equal(23, (int)limits["hours"]!["max"]!);
        Assert.Equal(59, (int)limits["minutes"]!["max"]!);
        var ro = meta["readOnly"]!.AsArray().Select(n => (string)n!).ToList();
        Assert.Equal(10, ro.Count);
        Assert.Contains("eventSetId", ro);
        Assert.Contains("trainingPointsPerLevel", ro);
        Assert.Equal("currentLeMax", (string?)meta["rules"]![0]!["type"]);
    }

    [Fact]
    public void GoldIsUnsigned()
    {
        var raw = SyntheticSave.MakeCharacter(new Random(1), "X", 1, NPCLayout.CharacterSize);
        raw[NPCLayout.Gold] = 0xFF; raw[NPCLayout.Gold + 1] = 0xFF;
        var ch = new Character(raw);
        Assert.Equal(6553.5m, ch.Gold);
        Assert.Equal(raw, ch.ToRawData());
    }

    [Fact]
    public void RealSave_CorrectedFields()
    {
        // Exact values recorded from the user's unedited GOG SAVE.001. The live save may have been edited
        // with this tool since, so the exact checks use ALBION_SAVE_ORIGINAL (a pristine copy, e.g. a backup);
        // with only ALBION_SAVE set, the read-only fields and plausibility are checked.
        string? original = Environment.GetEnvironmentVariable("ALBION_SAVE_ORIGINAL");
        string? path = !string.IsNullOrWhiteSpace(original) ? original : Environment.GetEnvironmentVariable("ALBION_SAVE");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(path), "ALBION_SAVE / ALBION_SAVE_ORIGINAL is not set.");
        var save = SaveFile.Parse(File.ReadAllBytes(path!), "SAVE");
        var tom = save.Characters[0];
        Assert.Equal("Tom", AlbionSaveEditor.Api.CharacterMapper.PrimaryName(tom));
        Assert.Equal(981, tom.EventSetId);
        Assert.Equal(319, save.Characters[5].WordSetId);    // Harriet
        Assert.InRange(tom.Age.MaximumValue, (short)1, (short)100);
        Assert.All(save.Characters.Values, c => Assert.InRange(c.Morale, (byte)0, (byte)100));
        if (!string.IsNullOrWhiteSpace(original))
        {
            Assert.Equal(new CharacterAttribute(42, 70), tom.Strength);
            Assert.Equal(new CharacterAttribute(28, 80), tom.Age);
            Assert.Equal(new CharacterAttribute(35, 80), tom.CloseRangeCombat);
            Assert.Equal(-5, save.Characters[1].BonusAttack);   // Rainer
            var siobhan = save.Characters[9];
            Assert.Equal(2, siobhan.BaseDefense);
            Assert.Equal(9, siobhan.BonusDefense);
            Assert.Equal(4, siobhan.BaseAttack);
            Assert.Equal(-10, siobhan.BonusAttack);
            Assert.All(save.Characters.Values, c => Assert.Equal(0, c.Morale));
        }
        TestContext.Current.SendDiagnosticMessage("growth (Tom): " +
            $"{tom.LevelsPerActionPoint}/{tom.LifePointsPerLevel}/{tom.SpellPointsPerLevel}/{tom.TrainingPointsPerLevel}");
    }
}
