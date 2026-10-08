using System.Globalization;
using System.Text.Json;
using AlbLib.SaveGame;
using AlbLib.Texts;
using AlbionSaveEditor.Services;

namespace AlbionSaveEditor.Api;

/// <summary>Maps <see cref="Character"/> (ported AlbLib) to/from <see cref="CharacterDto"/>.</summary>
public sealed class CharacterMapper(GameDataService gameData)
{
    public const int SpellSchools = 7;
    public const int SpellsPerSchool = 30;

    private static readonly string[] AttributeKeys =
        ["strength", "intelligence", "dexterity", "speed", "stamina", "luck", "magicResistance", "magicTalent"];
    private static readonly string[] SkillKeys = ["closeCombat", "rangedCombat", "criticalChance", "lockpicking"];
    private static readonly string[] EquipmentKeys =
        ["neck", "head", "tail", "rightHand", "chest", "leftHand", "rightFinger", "feet", "leftFinger"];

    // ---------------------------------------------------------------- read

    /// <summary>
    /// Real saves only fill the first 16-byte name slot (offset 274, AlbLib calls it "German");
    /// slots 2 and 3 (AlbLib "English"/"French") are empty, so <c>name</c> maps to slot 1.
    /// </summary>
    public static string PrimaryName(Character ch) => ch.Name.German ?? "";

    /// <summary>Unused record: all name slots blank and level 0.</summary>
    public static bool IsEmpty(Character ch)
        => string.IsNullOrWhiteSpace(ch.Name.German) && string.IsNullOrWhiteSpace(ch.Name.English)
           && string.IsNullOrWhiteSpace(ch.Name.French) && ch.Level == 0;

    /// <summary>
    /// Known-spell masks are 1-based: bit (index+1) marks spell <c>index</c> (whose strength is
    /// SpellStrengths[school, index]). Verified on a real save (e.g. mask 0x1A0 with strengths at 4, 6, 7).
    /// </summary>
    public static int SpellBit(int index) => 1 << (index + 1);

    public CharacterSummaryDto ToSummary(int index, Character ch) => new()
    {
        Index = index,
        Name = PrimaryName(ch),
        Empty = IsEmpty(ch),
        Race = EnumMapper.ToName(ch.Race),
        Class = EnumMapper.ToName(ch.Class),
        Level = ch.Level,
    };

    public CharacterDto ToDto(int index, Character ch)
    {
        var dto = new CharacterDto
        {
            Index = index,
            Name = PrimaryName(ch),
            Empty = IsEmpty(ch),
            Type = EnumMapper.ToName(ch.Type),
            Gender = EnumMapper.ToName(ch.Gender),
            Race = EnumMapper.ToName(ch.Race),
            Class = EnumMapper.ToName(ch.Class),
            Magic = EnumMapper.ToFlagNames(ch.Magic),
            Language = EnumMapper.ToFlagNames(ch.Language),
            Level = ch.Level,
            ActionPoints = ch.ActionPoints,
            Appearance = ch.Appearance,
            Face = ch.Face,
            InventoryPicture = ch.InventoryPicture,
            TrainingPoints = ch.TrainingPoints,
            Gold = ch.Gold,
            Morale = ch.Morale,
            Age = Attr(ch.Age),
            Rations = ch.Rations,
            Conditions = EnumMapper.ToFlagNames(ch.Conditions),
            Experience = ch.Experience,
            LifePoints = Attr(ch.LifePoints),
            SpellPoints = Attr(ch.SpellPoints),
            Attributes = new Dictionary<string, AttributeDto>
            {
                ["strength"] = Attr(ch.Strength),
                ["intelligence"] = Attr(ch.Intelligence),
                ["dexterity"] = Attr(ch.Dexterity),
                ["speed"] = Attr(ch.Speed),
                ["stamina"] = Attr(ch.Stamina),
                ["luck"] = Attr(ch.Luck),
                ["magicResistance"] = Attr(ch.MagicResistance),
                ["magicTalent"] = Attr(ch.MagicTalent),
            },
            Skills = new Dictionary<string, AttributeDto>
            {
                ["closeCombat"] = Attr(ch.CloseRangeCombat),
                ["rangedCombat"] = Attr(ch.LongRangeCombat),
                ["criticalChance"] = Attr(ch.CriticalHit),
                ["lockpicking"] = Attr(ch.Lockpicking),
            },
            Spells = new List<SpellDto>(SpellSchools * SpellsPerSchool),
            Equipment = new Dictionary<string, ItemStackDto>(),
            Backpack = new List<ItemStackDto>(ch.Backpack.Capacity),
            Other = new Dictionary<string, object?>
            {
                ["nameSlot2"] = ch.Name.English ?? "",
                ["nameSlot3"] = ch.Name.French ?? "",
                // read-only (see Limits.ReadOnly)
                ["eventSetId"] = (int)ch.EventSetId,
                ["wordSetId"] = (int)ch.WordSetId,
                ["baseDefense"] = (int)ch.BaseDefense,
                ["bonusDefense"] = (int)ch.BonusDefense,
                ["baseAttack"] = (int)ch.BaseAttack,
                ["bonusAttack"] = (int)ch.BonusAttack,
                ["levelsPerActionPoint"] = (int)ch.LevelsPerActionPoint,
                ["lifePointsPerLevel"] = (int)ch.LifePointsPerLevel,
                ["spellPointsPerLevel"] = (int)ch.SpellPointsPerLevel,
                ["trainingPointsPerLevel"] = (int)ch.TrainingPointsPerLevel,
            },
        };

        for (int school = 0; school < SpellSchools; school++)
        {
            for (int id = 0; id < SpellsPerSchool; id++)
            {
                dto.Spells.Add(new SpellDto
                {
                    School = school,
                    Id = id,
                    Name = gameData.GetSpellName(school, id),
                    Known = (ch.Spells[school] & SpellBit(id)) != 0,
                    Strength = ch.SpellStrengths[school, id],
                });
            }
        }

        for (int i = 0; i < EquipmentKeys.Length; i++)
            dto.Equipment[EquipmentKeys[i]] = Item(ch.Equipment[i]);
        for (int i = 0; i < ch.Backpack.Capacity; i++)
            dto.Backpack.Add(Item(ch.Backpack[i]));
        return dto;
    }

    private static AttributeDto Attr(CharacterAttribute a) => new() { Current = a.Value, Max = a.MaximumValue };

    private ItemStackDto Item(ItemStack s) => new()
    {
        ItemId = s.Type,
        ItemName = gameData.GetItemName(s.Type),
        Count = s.Count,
        Charges = s.Charges,
        Enchantment = s.NumRecharged,
        Flags = EnumMapper.ToFlagNames(s.Flags),
    };

    // ---------------------------------------------------------------- write

    /// <summary>
    /// Applies <paramref name="dto"/> to <paramref name="ch"/>. Missing (null) properties keep their value.
    /// Throws <see cref="ApiValidationException"/> on the first invalid field; callers apply to a copy
    /// so a failed update leaves the session untouched.
    /// </summary>
    public void Apply(CharacterDto dto, Character ch)
    {
        ch.Name = new LanguageTerm(
            ValidateName(dto.Name, ch.Name.German, "name"),
            ch.Name.English,
            ch.Name.French);

        ch.Type = EnumMapper.ParseName(dto.Type, ch.Type, "type");
        ch.Gender = EnumMapper.ParseName(dto.Gender, ch.Gender, "gender");
        ch.Race = EnumMapper.ParseName(dto.Race, ch.Race, "race");
        ch.Class = EnumMapper.ParseName(dto.Class, ch.Class, "class");
        ch.Magic = EnumMapper.ParseFlags(dto.Magic, ch.Magic, "magic");
        ch.Language = EnumMapper.ParseFlags(dto.Language, ch.Language, "language");
        ch.Conditions = EnumMapper.ParseFlags(dto.Conditions, ch.Conditions, "conditions");

        ch.Level = Limits.ToByte(dto.Level, ch.Level, "level");
        ch.ActionPoints = Limits.ToByte(dto.ActionPoints, ch.ActionPoints, "actionPoints");
        ch.Appearance = Limits.ToByte(dto.Appearance, ch.Appearance, "appearance");
        ch.Face = Limits.ToByte(dto.Face, ch.Face, "face");
        ch.InventoryPicture = Limits.ToByte(dto.InventoryPicture, ch.InventoryPicture, "inventoryPicture");
        ch.Morale = Limits.ToByte(dto.Morale, ch.Morale, "morale", Limits.Morale);
        ch.TrainingPoints = Limits.ToInt16(dto.TrainingPoints, ch.TrainingPoints, "trainingPoints", Limits.NonNegative16);
        ch.Rations = Limits.ToInt16(dto.Rations, ch.Rations, "rations", Limits.NonNegative16);
        ch.Experience = Limits.ToInt32(dto.Experience, ch.Experience, "experience", Limits.NonNegative32);

        if (dto.Gold is { } gold && gold != ch.Gold)
        {
            decimal tenths = gold * 10m;
            if (tenths != decimal.Truncate(tenths))
                throw new ApiValidationException("gold", "Gold has at most one decimal place.");
            if (gold < Limits.Gold.Min || gold > Limits.Gold.Max)
                throw new ApiValidationException("gold", $"Value {gold.ToString(CultureInfo.InvariantCulture)} is out of range ({Limits.Gold.Min}..{Limits.Gold.Max}).");
            ch.Gold = tenths / 10m;
        }

        ch.LifePoints = ApplyAttr(dto.LifePoints, ch.LifePoints, "lifePoints", Limits.NonNegative16);
        ch.SpellPoints = ApplyAttr(dto.SpellPoints, ch.SpellPoints, "spellPoints", Limits.NonNegative16);
        ch.Age = ApplyAttr(dto.Age, ch.Age, "age", Limits.AgeMax);

        if (dto.Attributes != null)
        {
            CheckKeys(dto.Attributes.Keys, AttributeKeys, "attributes");
            ch.Strength = ApplyAttr(dto.Attributes.GetValueOrDefault("strength"), ch.Strength, "attributes.strength", Limits.AttributeMax);
            ch.Intelligence = ApplyAttr(dto.Attributes.GetValueOrDefault("intelligence"), ch.Intelligence, "attributes.intelligence", Limits.AttributeMax);
            ch.Dexterity = ApplyAttr(dto.Attributes.GetValueOrDefault("dexterity"), ch.Dexterity, "attributes.dexterity", Limits.AttributeMax);
            ch.Speed = ApplyAttr(dto.Attributes.GetValueOrDefault("speed"), ch.Speed, "attributes.speed", Limits.AttributeMax);
            ch.Stamina = ApplyAttr(dto.Attributes.GetValueOrDefault("stamina"), ch.Stamina, "attributes.stamina", Limits.AttributeMax);
            ch.Luck = ApplyAttr(dto.Attributes.GetValueOrDefault("luck"), ch.Luck, "attributes.luck", Limits.AttributeMax);
            ch.MagicResistance = ApplyAttr(dto.Attributes.GetValueOrDefault("magicResistance"), ch.MagicResistance, "attributes.magicResistance", Limits.AttributeMax);
            ch.MagicTalent = ApplyAttr(dto.Attributes.GetValueOrDefault("magicTalent"), ch.MagicTalent, "attributes.magicTalent", Limits.AttributeMax);
        }

        if (dto.Skills != null)
        {
            CheckKeys(dto.Skills.Keys, SkillKeys, "skills");
            ch.CloseRangeCombat = ApplyAttr(dto.Skills.GetValueOrDefault("closeCombat"), ch.CloseRangeCombat, "skills.closeCombat", Limits.AttributeMax);
            ch.LongRangeCombat = ApplyAttr(dto.Skills.GetValueOrDefault("rangedCombat"), ch.LongRangeCombat, "skills.rangedCombat", Limits.AttributeMax);
            ch.CriticalHit = ApplyAttr(dto.Skills.GetValueOrDefault("criticalChance"), ch.CriticalHit, "skills.criticalChance", Limits.AttributeMax);
            ch.Lockpicking = ApplyAttr(dto.Skills.GetValueOrDefault("lockpicking"), ch.Lockpicking, "skills.lockpicking", Limits.AttributeMax);
        }

        if (dto.Spells != null)
        {
            for (int i = 0; i < dto.Spells.Count; i++)
            {
                var s = dto.Spells[i];
                string f = $"spells[{i}]";
                if (s == null) continue;
                if (s.School is not { } school || school < 0 || school >= SpellSchools)
                    throw new ApiValidationException($"{f}.school", $"School must be 0..{SpellSchools - 1}.");
                if (s.Id is not { } id || id < 0 || id >= SpellsPerSchool)
                    throw new ApiValidationException($"{f}.id", $"Spell id must be 0..{SpellsPerSchool - 1}.");
                if (s.Known is { } known)
                {
                    int mask = SpellBit((int)id);
                    ch.Spells[school] = known ? ch.Spells[school] | mask : ch.Spells[school] & ~mask;
                }
                ch.SpellStrengths[school, id] = Limits.ToInt16(s.Strength, ch.SpellStrengths[school, id], $"{f}.strength");
            }
        }

        if (dto.Equipment != null)
        {
            CheckKeys(dto.Equipment.Keys, EquipmentKeys, "equipment");
            for (int i = 0; i < EquipmentKeys.Length; i++)
            {
                if (dto.Equipment.TryGetValue(EquipmentKeys[i], out var item) && item != null)
                    ch.Equipment[i] = ApplyItem(item, ch.Equipment[i], $"equipment.{EquipmentKeys[i]}");
            }
        }

        if (dto.Backpack != null)
        {
            if (dto.Backpack.Count > ch.Backpack.Capacity)
                throw new ApiValidationException("backpack", $"The backpack has {ch.Backpack.Capacity} slots.");
            for (int i = 0; i < dto.Backpack.Count; i++)
            {
                if (dto.Backpack[i] != null)
                    ch.Backpack[i] = ApplyItem(dto.Backpack[i], ch.Backpack[i], $"backpack[{i}]");
            }
        }

        if (dto.Other != null) ApplyOther(dto.Other, ch);
    }

    private static void ApplyOther(Dictionary<string, object?> other, Character ch)
    {
        foreach (var (key, raw) in other)
        {
            string f = $"other.{key}";
            if (raw == null) continue;
            switch (key)
            {
                case "nameSlot2":
                    ch.Name = new LanguageTerm(ch.Name.German, ValidateName(AsString(raw, f), ch.Name.English, f), ch.Name.French);
                    break;
                case "nameSlot3":
                    ch.Name = new LanguageTerm(ch.Name.German, ch.Name.English, ValidateName(AsString(raw, f), ch.Name.French, f));
                    break;
                // read-only: accepted only when unchanged
                case "eventSetId": Limits.ReadOnlyField(AsLong(raw, f), ch.EventSetId, key, f); break;
                case "wordSetId": Limits.ReadOnlyField(AsLong(raw, f), ch.WordSetId, key, f); break;
                case "baseDefense": Limits.ReadOnlyField(AsLong(raw, f), ch.BaseDefense, key, f); break;
                case "bonusDefense": Limits.ReadOnlyField(AsLong(raw, f), ch.BonusDefense, key, f); break;
                case "baseAttack": Limits.ReadOnlyField(AsLong(raw, f), ch.BaseAttack, key, f); break;
                case "bonusAttack": Limits.ReadOnlyField(AsLong(raw, f), ch.BonusAttack, key, f); break;
                case "levelsPerActionPoint": Limits.ReadOnlyField(AsLong(raw, f), ch.LevelsPerActionPoint, key, f); break;
                case "lifePointsPerLevel": Limits.ReadOnlyField(AsLong(raw, f), ch.LifePointsPerLevel, key, f); break;
                case "spellPointsPerLevel": Limits.ReadOnlyField(AsLong(raw, f), ch.SpellPointsPerLevel, key, f); break;
                case "trainingPointsPerLevel": Limits.ReadOnlyField(AsLong(raw, f), ch.TrainingPointsPerLevel, key, f); break;
                default: throw new ApiValidationException(f, $"Unknown field '{key}'.");
            }
        }
    }

    private static long? AsLong(object raw, string field)
    {
        if (raw is JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Null) return null;
            if (e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out long l)) return l;
            if (e.ValueKind == JsonValueKind.Number) throw new ApiValidationException(field, "Whole numbers only (or value out of range).");
            throw new ApiValidationException(field, "A number is required.");
        }
        return Convert.ToInt64(raw, CultureInfo.InvariantCulture);
    }

    private static string? AsString(object raw, string field)
    {
        if (raw is JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Null) return null;
            if (e.ValueKind == JsonValueKind.String) return e.GetString();
            throw new ApiValidationException(field, "A string is required.");
        }
        return raw as string ?? throw new ApiValidationException(field, "A string is required.");
    }

    /// <summary>Validates a name only when it changed (unchanged names may legally use all 16 bytes).</summary>
    private static string ValidateName(string? value, string current, string field)
    {
        if (value == null || value == current) return current;
        if (value.Length > Limits.CharacterNameMaxLength)
            throw new ApiValidationException(field, $"Name can be at most {Limits.CharacterNameMaxLength} characters.");
        if (value.Contains('\0'))
            throw new ApiValidationException(field, "Name must not contain NUL characters.");
        var enc = TextCore.DefaultEncoding;
        if (enc.GetString(enc.GetBytes(value)) != value)
            throw new ApiValidationException(field, "Name contains characters the game cannot display.");
        return value;
    }

    /// <summary>
    /// Applies a {current, max} pair. max must lie in <paramref name="maxRange"/> when changed;
    /// 0 &lt;= current &lt;= max is checked when either side changed. Untouched pairs are never rejected.
    /// </summary>
    private static CharacterAttribute ApplyAttr(AttributeDto? dto, CharacterAttribute current, string field, Limits.Range maxRange)
    {
        if (dto == null) return current;
        long cur = dto.Current ?? current.Value;
        long max = dto.Max ?? current.MaximumValue;
        bool curChanged = cur != current.Value, maxChanged = max != current.MaximumValue;
        if (!curChanged && !maxChanged) return current;
        if (maxChanged) Limits.Check(max, maxRange, $"{field}.max");
        if (cur < 0 || cur > max)
            throw new ApiValidationException(curChanged ? $"{field}.current" : $"{field}.max",
                $"Current value {cur} must be between 0 and the maximum ({max}).");
        return new CharacterAttribute((short)cur, (short)max);
    }

    private ItemStack ApplyItem(ItemStackDto dto, ItemStack current, string field)
    {
        var s = current;
        // unchanged values are always accepted (random/legacy data may hold negative ids)
        if (dto.ItemId is { } id && id != s.Type) s.Type = (short)Limits.Check(id, Limits.ItemId, $"{field}.itemId");
        s.Count = Limits.ToByte(dto.Count, s.Count, $"{field}.count");
        s.Charges = Limits.ToByte(dto.Charges, s.Charges, $"{field}.charges");
        s.NumRecharged = Limits.ToByte(dto.Enchantment, s.NumRecharged, $"{field}.enchantment");
        s.Flags = EnumMapper.ParseFlags(dto.Flags, s.Flags, $"{field}.flags");

        if (s == current) return current; // untouched stacks are never rejected or rewritten

        if (s.Type == 0)
            return default; // emptied slot: normalise count/charges/enchantment/flags to 0

        if (gameData.HasItemDefinitions)
        {
            bool idChanged = s.Type != current.Type;
            var item = gameData.GetItem(s.Type);
            if (item?.Def == null)
                throw new ApiValidationException($"{field}.itemId", $"Unknown item id {s.Type} (known: 1..{gameData.Items.Count}).");
            if ((idChanged || s.Count != current.Count) && (s.Count < 1 || s.Count > item.MaxCount))
                throw new ApiValidationException($"{field}.count", item.Stackable
                    ? $"'{item.Name}' stacks up to {item.MaxCount}; count must be 1..{item.MaxCount}."
                    : $"'{item.Name}' is not stackable; count must be 1.");
            if ((idChanged || s.Charges != current.Charges) && s.Charges > item.MaxCharges)
                throw new ApiValidationException($"{field}.charges", $"'{item.Name}' holds at most {item.MaxCharges} charges.");
            if ((idChanged || s.NumRecharged != current.NumRecharged) && s.NumRecharged > item.MaxEnchantments)
                throw new ApiValidationException($"{field}.enchantment", $"'{item.Name}' allows at most {item.MaxEnchantments} enchantments.");
        }
        return s;
    }

    private static void CheckKeys(IEnumerable<string> keys, string[] allowed, string field)
    {
        foreach (var k in keys)
        {
            if (!allowed.Contains(k))
                throw new ApiValidationException($"{field}.{k}", $"Unknown key '{k}'. Allowed: {string.Join(", ", allowed)}.");
        }
    }
}
