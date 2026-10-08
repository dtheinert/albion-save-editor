using AlbLib.Items;
using AlbLib.SaveGame;
using AlbLib.Texts;

namespace AlbionSaveEditor.Services;

/// <summary>
/// Optional Albion game data (item and spell names), loaded into memory from an installed game
/// folder or from the game's CD image (GOG's game.gog). Everything degrades to empty names when no
/// game data is configured. Game files are only ever read, never written.
/// </summary>
public sealed class GameDataService
{
    /// <summary>Per-slot stack cap (UAlbion ItemSlot.MaxItemCount; hard-coded in the game, not in the data files).</summary>
    public const int StackMax = 99;

    /// <summary>Item name plus its ITEMLIST.DAT definition (null when ITEMLIST.DAT was not found).</summary>
    public sealed record ItemInfo(int Id, string Name, ItemType? Def)
    {
        public bool Stackable => Def?.IsStackable ?? false;
        public int MaxCount => Stackable ? StackMax : 1;
        public int MaxCharges => Def?.MaxCharges ?? 0;
        public int MaxEnchantments => Def?.MaxNumRecharged ?? 0;
        public bool PlotItem => Def != null && (Def.Flags & ItemTypeFlags.PlotItem) != 0;
        public bool Cursed => Def != null && (Def.Flags & ItemTypeFlags.Cursed) != 0;
    }
    public sealed record LoadResult(int ItemCount, int SpellCount, string Source);

    public const string ItemNamesFile = "XLDLIBS/ITEMNAME.DAT";
    public const string ItemListFile = "XLDLIBS/ITEMLIST.DAT";
    public const string SystemTextsFile = "XLDLIBS/ENGLISH/SYSTEXTS";

    /// <summary>System text id of the first spell name (AlbLib: Spell.GetName = SYSTEXTS 203 + school*30 + index).</summary>
    public const int FirstSpellTextId = 203;

    /// <summary>Install folders probed on startup when ALBION_PATH is not set.</summary>
    public static readonly string[] DefaultInstallPaths =
    [
        @"C:\Program Files (x86)\GOG Galaxy\Games\Albion",
        @"C:\GOG Games\Albion",
        @"C:\Program Files (x86)\GOG.com\Albion",
        @"C:\Program Files (x86)\Steam\steamapps\common\Albion",
        @"C:\Program Files\Steam\steamapps\common\Albion",
    ];

    private readonly object sync = new();
    private IReadOnlyList<ItemInfo> items = Array.Empty<ItemInfo>();
    private Dictionary<int, string> itemNames = new();
    private Dictionary<int, string> spellNames = new();

    public bool IsConfigured { get; private set; }
    public string? Source { get; private set; }
    public IReadOnlyList<ItemInfo> Items => items;

    /// <summary>True when item definitions (ITEMLIST.DAT) are loaded, i.e. per-item limits can be enforced.</summary>
    public bool HasItemDefinitions { get; private set; }

    /// <summary>Item info for a known id (1..n), or null.</summary>
    public ItemInfo? GetItem(int id) => id >= 1 && id <= items.Count ? items[id - 1] : null;
    public int SpellCount => spellNames.Count;

    public string GetItemName(int id) => id != 0 && itemNames.TryGetValue(id, out var n) ? n : "";

    /// <param name="school">Magic school 0..6.</param>
    /// <param name="index">Spell index 0..29 within the school.</param>
    public string GetSpellName(int school, int index)
        => spellNames.TryGetValue(school * 30 + index, out var n) ? n : "";

    /// <summary>
    /// Loads game data from <paramref name="path"/>: a game folder (with XLDLIBS/ITEMNAME.DAT or a CD image
    /// such as game.gog inside), an XLDLIBS folder, or a CD image file. Throws <see cref="ArgumentException"/>
    /// with a readable message when nothing usable is found.
    /// </summary>
    public LoadResult Configure(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.");
        path = Path.GetFullPath(path.Trim().Trim('"'));
        if (!File.Exists(path) && !Directory.Exists(path))
            throw new ArgumentException($"'{path}' does not exist.");

        var tried = new List<string>();
        foreach (var source in EnumerateSources(path, tried))
        {
            using (source)
            {
                byte[]? names = source.ReadFile(ItemNamesFile);
                if (names == null)
                {
                    tried.Add($"{source.Description}: no {ItemNamesFile}");
                    continue;
                }
                return Load(source, names);
            }
        }
        throw new ArgumentException(
            $"No Albion item data (ITEMNAME.DAT) found at '{path}'. Point to the Albion folder " +
            "(containing XLDLIBS with ITEMNAME.DAT, or the CD image game.gog / *.iso / *.bin) or to the image itself." +
            (tried.Count > 0 ? " Tried: " + string.Join("; ", tried) : ""));
    }

    /// <summary>Tries <see cref="DefaultInstallPaths"/>; returns the first that loads, or null.</summary>
    public LoadResult? TryAutoDetect(ILogger? logger = null)
    {
        foreach (var p in DefaultInstallPaths)
        {
            if (!Directory.Exists(p)) continue;
            try
            {
                var r = Configure(p);
                logger?.LogInformation("Game data auto-detected: {Source} ({Items} items, {Spells} spell names)", r.Source, r.ItemCount, r.SpellCount);
                return r;
            }
            catch (Exception e)
            {
                logger?.LogInformation("Game data not usable at {Path}: {Message}", p, e.Message);
            }
        }
        logger?.LogInformation("No Albion game data found; item and spell names will be empty.");
        return null;
    }

    private static IEnumerable<IGameDataSource> EnumerateSources(string path, List<string> tried)
    {
        if (File.Exists(path))
        {
            if (OpenImage(path, tried) is { } img) yield return img;
            yield break;
        }

        yield return new FolderGameDataSource(path);

        IEnumerable<string> images;
        try
        {
            images = Directory.EnumerateFiles(path)
                .Where(f => CdImageGameDataSource.ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .OrderBy(f => Path.GetFileName(f).Equals("game.gog", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            tried.Add($"{path}: {e.Message}");
            yield break;
        }
        foreach (var f in images)
        {
            if (OpenImage(f, tried) is { } img) yield return img;
        }
    }

    private static IGameDataSource? OpenImage(string file, List<string> tried)
    {
        try
        {
            var reader = CdImageReader.TryOpen(file);
            if (reader != null) return new CdImageGameDataSource(file, reader);
            tried.Add($"{file}: not an ISO9660 image");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            tried.Add($"{file}: {e.Message}");
        }
        return null;
    }

    private LoadResult Load(IGameDataSource source, byte[] itemNameData)
    {
        var names = TextCore.ReadItemNames(new MemoryStream(itemNameData, false));

        ItemType[]? types = null;
        if (source.ReadFile(ItemListFile) is { } list)
        {
            try { types = ItemType.ReadItemTypes(new MemoryStream(list, false)); }
            catch (EndOfStreamException) { types = null; }
        }

        Dictionary<int, string>? sysTexts = null;
        if (source.ReadFile(SystemTextsFile) is { } st)
            sysTexts = SystemTexts.Read(new MemoryStream(st, false));

        int count = Math.Max(names.Length, types?.Length ?? 0);
        var itemList = new List<ItemInfo>(count);
        var itemDict = new Dictionary<int, string>(count);
        for (int i = 0; i < count; i++)
        {
            // ITEMNAME.DAT holds German, English, French (20 bytes each); we use English.
            string n = i < names.Length ? names[i].English ?? "" : "";
            itemList.Add(new ItemInfo(i + 1, n, types != null && i < types.Length ? types[i] : null));
            itemDict[i + 1] = n;
        }

        var spells = new Dictionary<int, string>();
        if (sysTexts != null)
        {
            for (int i = 0; i < 7 * 30; i++)
            {
                if (sysTexts.TryGetValue(FirstSpellTextId + i, out var s) && !string.IsNullOrWhiteSpace(s)
                    && !s.Trim().Equals("DELETED", StringComparison.OrdinalIgnoreCase))
                    spells[i] = s.Trim();
            }
        }

        lock (sync)
        {
            TextCore.SetItemNames(names);
            if (types != null) ItemType.SetItemTypes(types); else ItemType.ClearCache();
            if (sysTexts != null) SystemTexts.SetTexts(sysTexts); else SystemTexts.ClearCache();

            items = itemList;
            itemNames = itemDict;
            HasItemDefinitions = types != null;
            spellNames = spells;
            IsConfigured = true;
            Source = source.Description;
            return new LoadResult(itemList.Count, spells.Count, source.Description);
        }
    }
}
