using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AlbionSaveEditor.Domain;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AlbionSaveEditor.Tests;

/// <summary>
/// Runs against a real Albion save when the ALBION_SAVE environment variable points to one
/// (e.g. ALBION_SAVE=C:\ALBION\SAVES\SAVE.001). Skipped otherwise. The file is only read.
/// </summary>
[Collection("GameData")]
public class RealSaveTests(EditorFactory factory) : IClassFixture<EditorFactory>
{
    private static byte[] LoadOrSkip()
    {
        string? path = Environment.GetEnvironmentVariable("ALBION_SAVE");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(path), "ALBION_SAVE is not set.");
        Assert.SkipUnless(File.Exists(path), $"ALBION_SAVE file '{path}' does not exist.");
        return File.ReadAllBytes(path!);
    }

    private static string GamePathOrSkip()
    {
        string? path = Environment.GetEnvironmentVariable("ALBION_PATH");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(path), "ALBION_PATH is not set.");
        return path!;
    }

    [Fact]
    public void RealSave_KnownOffsets_HavePlausibleValues()
    {
        var save = SaveFile.Parse(LoadOrSkip(), "SAVE");
        string[] party = ["Tom", "Rainer", "Drirr", "Sira", "Mellthas", "Harriet", "Joe", "", "Khunag", "Siobhan"];
        for (int i = 0; i < party.Length; i++)
        {
            if (!save.Characters.TryGetValue(i, out var ch)) continue;
            Assert.Equal(party[i], AlbionSaveEditor.Api.CharacterMapper.PrimaryName(ch));
            Assert.Equal("", ch.Name.English);
            Assert.Equal("", ch.Name.French);
            if (party[i] == "")
            {
                Assert.True(AlbionSaveEditor.Api.CharacterMapper.IsEmpty(ch));
                continue;
            }
            Assert.InRange(ch.Level, (byte)1, (byte)99);
            Assert.InRange(ch.LifePoints.MaximumValue, (short)1, (short)999);
            Assert.InRange(ch.LifePoints.Value, (short)0, ch.LifePoints.MaximumValue);
            Assert.InRange(ch.SpellPoints.Value, (short)0, ch.SpellPoints.MaximumValue);
            Assert.InRange(ch.Experience, 0, 10_000_000);
            Assert.InRange(ch.Gold, 0m, 3276.7m);
            Assert.InRange(ch.Rations, (short)0, (short)999);
            Assert.InRange(ch.Age.Value, (short)1, ch.Age.MaximumValue);
            Assert.InRange(ch.Age.MaximumValue, (short)1, (short)100);
            Assert.InRange(ch.Strength.Value, (short)1, ch.Strength.MaximumValue);
            Assert.Equal((byte)(i + 1), ch.Appearance); // PlayableCharacter ids
        }
        Assert.Equal("Tom", AlbionSaveEditor.Api.CharacterMapper.PrimaryName(save.Characters[0]));
    }

    [Fact]
    public void RealSave_TomsItems_ResolveToNames()
    {
        var save = SaveFile.Parse(LoadOrSkip(), "SAVE");
        var gd = new AlbionSaveEditor.Services.GameDataService();
        var r = gd.Configure(GamePathOrSkip());
        Assert.True(r.ItemCount > 400);
        var tom = save.Characters[0];
        var names = tom.Equipment.Concat(tom.Backpack).Where(s => !s.IsEmpty).Select(s => (s.Type, Name: gd.GetItemName(s.Type))).ToList();
        TestContext.Current.SendDiagnosticMessage("Tom's items: " + string.Join(", ", names.Select(n => $"{n.Type}={n.Name}")));
        Assert.All(names, n => Assert.False(string.IsNullOrEmpty(n.Name), $"item {n.Type} has no name"));
    }

    [Fact]
    public void RealGameData_LoadsItemsAndSpells()
    {
        var gd = new AlbionSaveEditor.Services.GameDataService();
        var r = gd.Configure(GamePathOrSkip());
        TestContext.Current.SendDiagnosticMessage($"{r.Source}: {r.ItemCount} items, {r.SpellCount} spells");
        Assert.Equal("knife", gd.GetItemName(1));
        Assert.Equal("overall", gd.GetItemName(378));
        Assert.Equal("boots", gd.GetItemName(74));
        Assert.True(r.SpellCount > 0);
        Assert.Equal("Thorn snare", gd.GetSpellName(0, 0));
    }

    [Fact]
    public void RealSave_RoundTrip_IsByteIdentical()
    {
        var bytes = LoadOrSkip();
        var save = SaveFile.Parse(bytes, "SAVE");
        Assert.NotEmpty(save.Characters);
        Assert.Equal(bytes, save.Build());
        foreach (var (index, ch) in save.Characters)
            Assert.Equal(save.GetOriginalSubfile(index), ch.ToRawData());
    }

    [Fact]
    public async Task RealSave_GetPutAllCharacters_IsByteIdentical()
    {
        var bytes = LoadOrSkip();
        var client = factory.CreateClient();
        var save = await ApiTests.Upload(client, bytes);
        string sid = (string)save["sessionId"]!;
        foreach (var c in save["characters"]!.AsArray())
        {
            int i = (int)c!["index"]!;
            var ch = await client.GetFromJsonAsync<JsonObject>($"/api/saves/{sid}/characters/{i}");
            var res = await client.PutAsJsonAsync($"/api/saves/{sid}/characters/{i}", ch);
            Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        }
        Assert.Equal(bytes, await ApiTests.Download(client, sid));
    }
}
