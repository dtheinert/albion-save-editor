# Albion Save Editor

A Windows tool to view and edit savegames of the 1995 RPG **Albion** (Blue Byte):
party members, stats, skills, conditions, languages, spells, gold, rations, equipment and backpack.

- **Safe by default:** unedited saves are written back byte-for-byte identical; only fields you change are touched.
- **Game limits enforced:** stack sizes (99 for stackable items, 1 otherwise), item charges/enchantments,
  attribute/skill maximums (0–99), current ≤ max, and more. Values already out of range in a save are flagged, not rejected.
- **Item and spell names** are read from your own Albion installation — GOG (`game.gog` CD image) and Steam installs
  are detected automatically. No game files are included.
- **Read-only where editing is risky:** script links and game-computed combat values are shown but locked.

## Download

Grab `AlbionSaveEditor-win-x64.zip` from the [Releases](../../releases) page, unzip, and run `AlbionSaveEditor.exe`.
Windows 10/11 (64-bit), nothing to install.

> The exe is not code-signed, so Windows SmartScreen may warn on first start: **More info → Run anyway**.

**Back up your `SAVES` folder before replacing any save.**

## Usage

1. Start the editor and choose a save (`SAVE.001` …) — or drag it onto the window.
   GOG: `C:\Program Files (x86)\GOG Galaxy\Games\Albion\SAVES` (cloud saves: `…\cloud_saves\SAVES`).
2. Edit, press **Apply changes**.
3. **Download modified save**, save it under the original name into Albion's `SAVES` folder.

## Building from source

Requires the .NET 10 SDK.

```powershell
dotnet run --project src/AlbionSaveEditor          # web version on http://localhost:5080
dotnet test                                       # test suite
powershell -ExecutionPolicy Bypass -File .\publish.ps1   # dist\AlbionSaveEditor.exe + zip
```

Optional real-data tests: set `ALBION_SAVE` (path to a save) and `ALBION_PATH` (Albion install folder).

| Project | Purpose |
|---|---|
| `src/AlbLib.Core` | Port of [AlbLib](https://github.com/IS4Code/AlbLib) (save/XLD/text/item parsing) with fixes |
| `src/AlbionSaveEditor.Server` | ASP.NET Core API, validation, game-data loading (incl. ISO9660 reader) |
| `src/AlbionSaveEditor` | Web host + frontend (`wwwroot`, plain HTML/CSS/JS) |
| `src/AlbionSaveEditor.Desktop` | Windows app (WebView2 window, single-file exe) |
| `tests/AlbionSaveEditor.Tests` | xUnit tests (synthetic saves + optional real data) |

The API and the savegame field layout are documented in [API_CONTRACT.md](API_CONTRACT.md).

## Credits

- [AlbLib](https://github.com/IS4Code/AlbLib) by IS4Code — MIT license; the parsing code is a port of it.
- [UAlbion](https://github.com/csinkers/ualbion) by csinkers and contributors — file-format research
  (character sheet layout, item flags, stack limits). No UAlbion code is included.
- Albion is © Blue Byte. This is an unofficial fan project.

## License

MIT — see [LICENSE](LICENSE). `src/AlbLib.Core` remains under AlbLib's MIT license.
