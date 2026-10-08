namespace AlbionSaveEditor.Api;

/// <summary>
/// Value ranges, shared by validation and /api/meta. All checks apply only to values that differ
/// from the current server state: untouched values from a save are never rejected.
/// </summary>
public static class Limits
{
    public sealed record Range(decimal Min, decimal Max, decimal? Step = null);

    public static readonly Range Byte = new(0, byte.MaxValue);
    public static readonly Range Int16 = new(short.MinValue, short.MaxValue);
    public static readonly Range NonNegative16 = new(0, short.MaxValue);
    public static readonly Range NonNegative32 = new(0, int.MaxValue);
    /// <summary>Attribute and skill maximum (every value in real data is &lt;= 99).</summary>
    public static readonly Range AttributeMax = new(0, 99);
    public static readonly Range AgeMax = new(0, 100);
    public static readonly Range Morale = new(0, 100);
    /// <summary>Gold is stored as unsigned int16 tenths.</summary>
    public static readonly Range Gold = new(0, ushort.MaxValue / 10m, 0.1m);
    /// <summary>Item type is an int16; negative ids are meaningless.</summary>
    public static readonly Range ItemId = new(0, short.MaxValue);
    public static readonly Range Hours = new(0, 23);
    public static readonly Range Minutes = new(0, 59);

    /// <summary>Each name slot is 16 bytes; we keep room for the NUL terminator.</summary>
    public const int CharacterNameMaxLength = 15;
    /// <summary>Header name length prefix is an int16; we cap it at a sane length.</summary>
    public const int SaveNameMaxLength = 255;

    /// <summary>Character fields shown but never changed by the editor (all live in <c>other</c>).</summary>
    public static readonly string[] ReadOnly =
    [
        "eventSetId", "wordSetId", "baseDefense", "bonusDefense", "baseAttack", "bonusAttack",
        "levelsPerActionPoint", "lifePointsPerLevel", "spellPointsPerLevel", "trainingPointsPerLevel",
    ];

    /// <summary>
    /// Limits keyed by leaf name or by path (array indexes and attribute names removed), e.g.
    /// "attributes.max", "age.current", "backpack[3].count" -> "count".
    /// </summary>
    public static Dictionary<string, object> ForMeta()
    {
        object R(Range r) => r.Step is { } s ? new { min = r.Min, max = r.Max, step = s } : new { min = r.Min, max = r.Max };
        return new Dictionary<string, object>
        {
            // character
            ["level"] = R(Byte),
            ["actionPoints"] = R(Byte),
            ["appearance"] = R(Byte),
            ["face"] = R(Byte),
            ["inventoryPicture"] = R(Byte),
            ["morale"] = R(Morale),
            ["trainingPoints"] = R(NonNegative16),
            ["gold"] = R(Gold),
            ["rations"] = R(NonNegative16),
            ["experience"] = R(NonNegative32),
            // {current, max} pairs: current is additionally limited to 0..max (meta.rules currentLeMax)
            ["attributes"] = R(AttributeMax),
            ["attributes.max"] = R(AttributeMax),
            ["attributes.current"] = R(AttributeMax),
            ["skills"] = R(AttributeMax),
            ["skills.max"] = R(AttributeMax),
            ["skills.current"] = R(AttributeMax),
            ["age"] = R(AgeMax),
            ["age.max"] = R(AgeMax),
            ["age.current"] = R(AgeMax),
            ["lifePoints"] = R(NonNegative16),
            ["lifePoints.max"] = R(NonNegative16),
            ["lifePoints.current"] = R(NonNegative16),
            ["spellPoints"] = R(NonNegative16),
            ["spellPoints.max"] = R(NonNegative16),
            ["spellPoints.current"] = R(NonNegative16),
            ["spells.strength"] = R(Int16),
            ["name"] = new { maxLength = CharacterNameMaxLength },
            ["nameSlot2"] = new { maxLength = CharacterNameMaxLength },
            ["nameSlot3"] = new { maxLength = CharacterNameMaxLength },
            // item stacks
            ["itemId"] = R(ItemId),
            ["count"] = R(Byte),
            ["charges"] = R(Byte),
            ["enchantment"] = R(Byte),
            // per-slot stack cap for stackable items (per-item limits are in meta.items[])
            ["stackMax"] = Services.GameDataService.StackMax,
            // header
            ["header.name"] = new { maxLength = SaveNameMaxLength },
            ["days"] = R(NonNegative16),
            ["hours"] = R(Hours),
            ["minutes"] = R(Minutes),
            ["mapId"] = R(NonNegative16),
            ["partyX"] = R(NonNegative16),
            ["partyY"] = R(NonNegative16),
        };
    }

    public static byte ToByte(long? value, byte current, string field, Range? range = null)
        => value is { } v && v != current ? (byte)Check(v, range ?? Byte, field) : current;

    public static short ToInt16(long? value, short current, string field, Range? range = null)
        => value is { } v && v != current ? (short)Check(v, range ?? Int16, field) : current;

    public static int ToInt32(long? value, int current, string field, Range? range = null)
        => value is { } v && v != current ? (int)Check(v, range ?? NonNegative32, field) : current;

    public static long Check(long value, Range range, string field)
    {
        if (value < range.Min || value > range.Max)
            throw new ApiValidationException(field, $"Value {value} is out of range ({range.Min}..{range.Max}).");
        return value;
    }

    /// <summary>Read-only field: accepted when absent or unchanged, otherwise 400.</summary>
    public static void ReadOnlyField(long? value, long current, string key, string field)
    {
        if (value is { } v && v != current)
            throw new ApiValidationException(field, $"'{key}' is read-only");
    }
}
