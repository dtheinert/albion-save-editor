namespace AlbionSaveEditor.Api;

// All numeric inputs are long/decimal so out-of-range values reach our validator
// (and produce 400 {error, field}) instead of failing inside the JSON deserializer.
// Properties are nullable so partial PUT bodies keep the current value for missing fields.

public sealed class HeaderDto
{
    public string? Name { get; set; }
    public string? Version { get; set; }
    public long? Days { get; set; }
    public long? Hours { get; set; }
    public long? Minutes { get; set; }
    public long? MapId { get; set; }
    public long? PartyX { get; set; }
    public long? PartyY { get; set; }
}

public sealed class CharacterSummaryDto
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    /// <summary>True for unused records (blank names, level 0).</summary>
    public bool Empty { get; set; }
    public string Race { get; set; } = "";
    public string Class { get; set; } = "";
    public int Level { get; set; }
}

public sealed class SaveDto
{
    public string SessionId { get; set; } = "";
    public string FileName { get; set; } = "";
    public HeaderDto Header { get; set; } = new();
    public List<CharacterSummaryDto> Characters { get; set; } = new();
}

public sealed class AttributeDto
{
    public long? Current { get; set; }
    public long? Max { get; set; }
}

public sealed class SpellDto
{
    public long? School { get; set; }
    public long? Id { get; set; }
    public string? Name { get; set; }
    public bool? Known { get; set; }
    public long? Strength { get; set; }
}

public sealed class ItemStackDto
{
    public long? ItemId { get; set; }
    public string? ItemName { get; set; }
    public long? Count { get; set; }
    public long? Charges { get; set; }
    public long? Enchantment { get; set; }
    public List<string>? Flags { get; set; }
}

public sealed class CharacterDto
{
    public int? Index { get; set; }
    public string? Name { get; set; }
    /// <summary>Output only (ignored on PUT): true for unused records (blank names, level 0).</summary>
    public bool? Empty { get; set; }
    public string? Type { get; set; }
    public string? Gender { get; set; }
    public string? Race { get; set; }
    public string? Class { get; set; }
    public List<string>? Magic { get; set; }
    public List<string>? Language { get; set; }
    public long? Level { get; set; }
    public long? ActionPoints { get; set; }
    public long? Appearance { get; set; }
    public long? Face { get; set; }
    public long? InventoryPicture { get; set; }
    public long? TrainingPoints { get; set; }
    public decimal? Gold { get; set; }
    /// <summary>Morale 0..100 (offset 15).</summary>
    public long? Morale { get; set; }
    /// <summary>Age as a {current, max} pair (offset 106).</summary>
    public AttributeDto? Age { get; set; }
    public long? Rations { get; set; }
    public List<string>? Conditions { get; set; }
    public long? Experience { get; set; }
    public AttributeDto? LifePoints { get; set; }
    public AttributeDto? SpellPoints { get; set; }
    public Dictionary<string, AttributeDto>? Attributes { get; set; }
    public Dictionary<string, AttributeDto>? Skills { get; set; }
    public List<SpellDto>? Spells { get; set; }
    public Dictionary<string, ItemStackDto>? Equipment { get; set; }
    public List<ItemStackDto>? Backpack { get; set; }
    /// <summary>Extra scalars. Output values are numbers/strings; input values arrive as JsonElement.</summary>
    public Dictionary<string, object?>? Other { get; set; }
}

public sealed class GameDataRequest
{
    public string? Path { get; set; }
}
