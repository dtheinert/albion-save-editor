# Albion Save Editor — API Contract

Shared contract between backend (ASP.NET Core, .NET 10) and frontend (static HTML/JS in `wwwroot`).
Base URL: same origin (backend serves the frontend). All JSON is camelCase.

## Layout
```
AlbionSaveEditor/
  API_CONTRACT.md
  AlbionSaveEditor.sln
  src/AlbionSaveEditor/            <- ASP.NET Core minimal API (backend owns everything except wwwroot)
     wwwroot/                      <- frontend owns this (index.html, app.js, styles.css)
  tests/AlbionSaveEditor.Tests/    <- backend tests
```
Library source: [IS4Code/AlbLib](https://github.com/IS4Code/AlbLib) (.NET Fx 4.5), ported to `src/AlbLib.Core`.
Upstream is not edited; the port carries `PORT:` comments for every change.

## Endpoints

### `POST /api/saves`  (multipart/form-data, field `file`)
Uploads a save (e.g. `SAVE.001`). Server parses and keeps it in memory.
Response `200`:
```json
{
  "sessionId": "string",
  "fileName": "SAVE.001",
  "header": { "name": "My save", "version": "1.38", "days": 3, "hours": 14, "minutes": 5,
              "mapId": 300, "partyX": 42, "partyY": 17 },
  "characters": [ { "index": 0, "name": "Tom", "race": "Terran", "class": "Pilot", "level": 3 } ]
}
```
`400` `{ "error": "message" }` when the file can't be parsed.

### `GET /api/saves/{sessionId}`
Same shape as the upload response (reflects current edits).

### `PUT /api/saves/{sessionId}/header`
Body: the `header` object (all fields). Returns the updated header. `version` is read-only (ignored).

### `GET /api/saves/{sessionId}/characters/{index}`
Full character:
```json
{
  "index": 0,
  "name": "Tom",
  "type": "Party",
  "gender": "Male", "race": "Terran", "class": "Pilot",
  "magic": ["None"],                 // flags enum -> array of names
  "language": ["Terran"],           // flags enum -> array of names
  "level": 3, "actionPoints": 2,
  "appearance": 0, "face": 0, "inventoryPicture": 0,
  "trainingPoints": 10, "gold": 12.5, "rations": 20,
  "conditions": [],                  // flags enum -> array of names
  "experience": 1234,
  "lifePoints":  { "current": 30, "max": 30 },
  "spellPoints": { "current": 0,  "max": 0 },
  "attributes": {                    // each { current, max }
    "strength": {...}, "intelligence": {...}, "dexterity": {...}, "speed": {...},
    "stamina": {...}, "luck": {...}, "magicResistance": {...}, "magicTalent": {...}
  },
  "skills": { "closeCombat": {...}, "rangedCombat": {...}, "criticalChance": {...}, "lockpicking": {...} },
  "spells": [ { "school": 0, "id": 1, "name": "Heal", "known": true, "strength": 0 } ],
  "equipment": { "neck": ItemStack, "head": ItemStack, "tail": ItemStack, "rightHand": ItemStack,
                 "chest": ItemStack, "leftHand": ItemStack, "rightFinger": ItemStack,
                 "feet": ItemStack, "leftFinger": ItemStack },
  "backpack": [ ItemStack x24 ],     // slot order, index 0..23
  "other": { "fieldName": 123 }      // any additional editable scalar the library exposes
}
```
`ItemStack`:
```json
{ "itemId": 0, "itemName": "", "count": 0, "charges": 0, "enchantment": 0, "flags": [] }
```
`itemId: 0` / `count: 0` = empty slot. `itemName` is best-effort (empty if game data isn't available).
Backend may add fields it discovers in the library; frontend must render unknown scalar fields generically.
Exact property names above are binding; if the library lacks one, omit it rather than rename.

### `PUT /api/saves/{sessionId}/characters/{index}`
Body: full character object (same shape). Returns the updated character, or `400 { "error", "field" }`
on validation failure (e.g. value out of byte/short range).

### `GET /api/saves/{sessionId}/download`
Returns the rebuilt save file as `application/octet-stream` with the original filename.
Unedited saves must be byte-identical to the upload.

### `GET /api/meta`
Enum metadata for dropdowns / checkbox groups:
```json
{
  "enums": {
    "gender":    { "flags": false, "values": [ { "name": "Male", "value": 0 } ] },
    "race": {...}, "class": {...}, "type": {...},
    "magic":     { "flags": true,  "values": [...] },
    "language":  { "flags": true,  "values": [...] },
    "conditions":{ "flags": true,  "values": [...] },
    "itemFlags": { "flags": true,  "values": [...] }
  },
  "limits": { "level": { "min": 0, "max": 255 }, "...": {} },
  "items": [ { "id": 1, "name": "Sword" } ]     // empty array if game data not configured
}
```

### `POST /api/gamedata`  (optional) body `{ "path": "C:\\...\\ALBION" }`
Points the server at an Albion install so item/spell names resolve. Returns `{ "ok": true, "itemCount": n }`.

---

## Backend notes

### Run
```
cd albion-save-editor
dotnet run --project src/AlbionSaveEditor        # http://localhost:5080
dotnet test                                      # uses Microsoft.Testing.Platform (see global.json)
```
Game data (item/spell names) is loaded at startup from `ALBION_PATH` (or config `Albion:GamePath`);
if unset, these install folders are probed and the one used is logged:
`C:\Program Files (x86)\GOG Galaxy\Games\Albion`, `C:\GOG Games\Albion`,
`C:\Program Files (x86)\GOG.com\Albion`, `C:\Program Files (x86)\Steam\steamapps\common\Albion`,
`C:\Program Files\Steam\steamapps\common\Albion` (disable with `Albion:AutoDetect=false`).
The port can be overridden with `--urls`. Tests: set `ALBION_SAVE` (a real save) and/or `ALBION_PATH`
(an Albion install) to enable the real-data tests; otherwise they are skipped.
The server never writes to disk: uploads are kept in memory (max 64 sessions, oldest dropped) and
saves are only returned via `/download`. Game data files are opened read-only.

### Desktop app (Windows, shareable single exe)
- `src/AlbionSaveEditor.Server` (class library): all API/hosting code (`EditorHost.Build(args, options)`),
  the frontend embedded from `src/AlbionSaveEditor/wwwroot` (ManifestEmbeddedFileProvider). The web
  project is now a 3-line `Program.cs` calling `EditorHost.Build(args)`; it still serves wwwroot **from disk**
  when `wwwroot/index.html` exists in its content root (frontend edits need no rebuild).
- `src/AlbionSaveEditor.Desktop` (net10.0-windows WinExe): hosts the server on 127.0.0.1 with a random free
  port, shows the UI in a WebView2 window ("Albion Save Editor", 1400x900, icon `app.ico`). "Download
  modified save" opens a Save As dialog with the original file name. Without the WebView2 runtime the default
  browser is opened and a small "Editor running - close this to quit" window keeps the server alive.
  Closing the window stops the server. Log: `%LOCALAPPDATA%\AlbionSaveEditor\editor.log`; WebView2 data:
  `%LOCALAPPDATA%\AlbionSaveEditor\WebView2`. Options: `--port N`, `--self-test SAVE OUTDIR` (automated
  upload + blob download through WebView2, exit code 0 = file saved and byte-identical).
- Build: `powershell -ExecutionPolicy Bypass -File .\publish.ps1` -> `dist\AlbionSaveEditor.exe`
  (self-contained single file, ~61 MB) and `dist\AlbionSaveEditor-win-x64.zip` (exe + README.txt +
  LICENSE-AlbLib.txt; README source: `packaging/README.txt`). `-ArtifactsPath <dir>` if bin\ is locked.
- `GET /api/about` -> `{ name, version, credits: [{ name, author, url, license, usage, licenseText }], notice }`
  (AlbLib with its MIT license text, UAlbion as format research).

### Projects
- `src/AlbLib.Core` – partial port of AlbLib (MIT, LICENSE included) to net10.0: `SaveGame`, `XLD`,
  `Texts` (core), `Items`, `Caching`, path helpers. Changes are marked `PORT:`. Notably:
  `NPC/Character.ToRawData()` implemented (patches known fields into the original record, unknown
  bytes preserved), `NPCLayout` offsets class, text encoding = code page 850 (lossless for all 256 bytes;
  upstream used ASCII), XLD signature search fixed, `Spell.GetName` reads SYSTEXTS directly.
- `src/AlbionSaveEditor.Server` – `EditorHost.cs` (endpoints), `Domain/SaveFile.cs` (parse/rebuild),
  `Api/*Mapper.cs`, `Api/Limits.cs`, `Services/*`. `src/AlbionSaveEditor` – web host (`dotnet run`, port 5080).

### Save rebuild
`download` = header + original bytes between header and the first XLD + rebuilt character XLD +
original bytes after that XLD (other XLDs/game state verbatim). If the header was never PUT, the
original header bytes are copied. Changing the header name shifts everything after the header.
Subfiles of the character XLD shorter than 940 bytes (empty slots) are not listed and are kept verbatim.

### Deviations / clarifications
- `characters[].index` / `{index}` is the **subfile index** inside the character XLD; empty slots are
  skipped, so indexes can have gaps (e.g. 0, 2, 3).
- Enum names come from the library, e.g. gender `Man`/`Woman`/`Creature`, type
  `Character`/`NPC`/`Monster` (not `Male`/`Party`). Values the library does not name are returned as
  decimal strings (e.g. `"race": "12"`); numeric strings are also accepted on PUT.
- Flags: only named bits are listed. Bits not covered by any enum member are hidden and always preserved
  on PUT. A zero value is `["None"]` when the enum has a `None` member (magic, language, itemFlags),
  otherwise `[]` (conditions).
- `name` is the **first** 16-byte name slot (offset 274; AlbLib calls it "German"). Real saves only fill
  this slot; slots 2/3 (always empty in real saves) are `other.nameSlot2` / `other.nameSlot3`.
  New names may be at most 15 characters and must be encodable in code page 850. Unchanged names are
  never re-validated or rewritten.
- `empty: true` (in `characters[]` and in the full character; output only, ignored on PUT) marks unused
  records: all three name slots blank and level 0 (e.g. records 7, 10, 21, 40, 57 of a real save).
- `ItemStack.enchantment` maps to the library's `NumRecharged` (byte 2 of the 6-byte stack;
  UAlbion calls it enchantment). `itemName` is `""` unless game data is configured.
- `spells`: always 210 entries (7 schools x 30, `id` 0..29). The known-spell masks are **1-based**:
  `known` = bit `id + 1` of the school's 32-bit mask, `strength` = `SpellStrengths[school, id]`,
  `name` = SYSTEXTS entry `203 + school*30 + id` (`""` if unknown, blank or "DELETED").
  Verified on a real save (Sira: mask 0x260 -> View of life, Frost splinter, Light healing; Harriet's
  mask would hit a DELETED spell with a 0-based mapping). Mask bits 0 and 31 are preserved.
  Entries may be omitted on PUT; `school`/`id` identify the spell.
- `other`: `nameSlot2`, `nameSlot3` (editable) and the read-only `eventSetId`, `wordSetId`, `baseDefense`,
  `bonusDefense`, `baseAttack`, `bonusAttack`, `levelsPerActionPoint`, `lifePointsPerLevel`,
  `spellPointsPerLevel`, `trainingPointsPerLevel` (see "Character field limits & corrections").
  Unknown keys in `other` -> 400.
- PUT bodies may be partial: missing/null properties keep their current value. Validation is atomic
  (nothing changes on 400). Error `field` uses dotted paths with `[n]` for arrays, e.g.
  `attributes.strength.current`, `backpack[3].count`, `spells[5].strength`, `other.age`.
  Malformed JSON / wrong types -> 400 with `field` from the JSON path when available.
- Unknown session or character -> `404 { error }`.
- Header: `name` max 255 characters (our cap; the length prefix is int16). `version` is formatted as
  `major.minor` with two minor digits (byte 138 -> `"1.38"`) and ignored on PUT.
- `/api/meta`: `limits` are keyed by leaf name (`level`, `gold`, `attributes`, `skills`, `lifePoints`,
  `spells.strength`, `itemId`, `count`, `charges`, `enchantment`, `age`, `days`, ...); `gold` has
  `step: 0.1`; name limits are `{ maxLength }` (`name`, `nameSlot2`, `nameSlot3`, `header.name`).
  Extra properties `gameDataConfigured: bool`, `gameDataSource: string|null`. `items` holds English names.
- `/api/gamedata` returns `{ ok, itemCount, spellCount, source }`. `path` may be:
  a game folder with `XLDLIBS/ITEMNAME.DAT` (or the `XLDLIBS` folder itself); a folder containing a CD
  image (`game.gog` preferred, else `*.gog`/`*.iso`/`*.bin`) - the GOG release ships the data only inside
  `game.gog`; or the image file itself. Images are read with a built-in read-only ISO9660 reader
  (2048-byte sectors, or raw 2352-byte Mode 1 (+16) / Mode 2 Form 1 (+24), detected via the PVD at
  sector 16); files are taken from `/ALBION/XLDLIBS/` (`ITEMNAME.DAT`, `ITEMLIST.DAT`,
  `ENGLISH/SYSTEXTS`). ITEMNAME.DAT = 60 bytes per item: German, English, French (20 bytes each);
  English is used. Everything is loaded into memory. Errors: `400 { ok: false, error, field: "path" }`
  with a message listing what was tried.
- `itemId` must be 0..32767 when changed; unchanged values (even negative ones) are accepted.

### Item limits (implementation of the "Item limits" section below)
- `AlbLib.Core` `ItemType`: upstream's `Count1`/`Count2` bytes (offsets 27/28) replaced by
  `ItemTypeFlags Flags` (uint16; `PlotItem` 0x02, `Stackable` 0x04, `Cursed` 0x400; other bits kept raw)
  and `IsStackable`. Item defs are loaded from `XLDLIBS/ITEMLIST.DAT` (folder or CD image).
  Verified on the GOG data: 225 stackable items; 291 stimdrink stackable (maxCount 99),
  378 overall not stackable (maxCount 1).
- `meta.items[]`: `{id, name, stackable, maxCount, maxCharges, maxEnchantments, plotItem, cursed, slot, class}`.
  `maxEnchantments` = ITEMLIST offset 25, `maxCharges` = offset 26. `slot`/`class` use AlbLib's enum names
  (`ItemSlot`: `Inventory` for "no slot" (the contract example says `None`), `Neck`, `Head`, ...,
  `RightHandOrTail`; `ItemClass`: `Armor`, `Drink`, `CloseRangeWeapon`, ...); numeric strings for
  unnamed values. If ITEMLIST.DAT is missing (names only), entries have `maxCount: 255`,
  `slot`/`class: null` and no per-item validation is done. `meta.limits.stackMax = 99` (a plain number).
- PUT validation (only with item definitions loaded, only for stacks that differ from the current server
  state). Within a changed stack each check runs only if its own field or the `itemId` changed, so
  e.g. editing the count of a stack whose stored charges already exceed the max does not fail on charges:
  `itemId` must be a known id (`field: ...itemId`); `count` 1..maxCount (`...count`);
  `charges` <= maxCharges (`...charges`); `enchantment` <= maxEnchantments (`...enchantment`).
- A changed stack with `itemId: 0` is normalised to an all-zero slot (no error, regardless of game data).
- Untouched stacks are never validated or rewritten (save stays byte-identical).

### Character record offsets (940 bytes, little endian; `src/AlbLib.Core/SaveGame/NPCLayout.cs`)
| Offset | Size | Field | | Offset | Size | Field |
|---|---|---|---|---|---|---|
| 0 | 1 | type | | 106 | 4 (+4) | age (cur, max) |
| 1 | 1 | gender | | 122 | 4 | closeCombat (cur, max) |
| 2 | 1 | race | | 130 | 4 | rangedCombat |
| 3 | 1 | class | | 138 | 4 | criticalChance |
| 4 | 1 | magic (flags) | | 146 | 4 | lockpicking |
| 5 | 1 | level | | 202 | 4 | lifePoints (cur, max) |
| 8 | 1 | language (flags) | | 208 | 4 | spellPoints (cur, max) |
| 9/10/11 | 1 each | appearance / face / inventoryPicture | | 212 | 2 | SP boost (preserved, not exposed) |
| 15 | 1 | morale | | 214/216/218/220 | 2 each | baseDefense u16 / bonusDefense s16 / baseAttack u16 / bonusAttack s16 |
| | | | | 226/228/230/234 | 2 each | levelsPerActionPoint / lifePointsPerLevel / spellPointsPerLevel / trainingPointsPerLevel |
| 17 | 1 | actionPoints | | 238 | 4 | experience |
| 18/20 | 2 each | eventSetId / wordSetId (u16) | | 242 | 7x4 | known-spell masks |
| 22 | 2 | trainingPoints | | 274 | 3x16 | name slots (only the first is used by the game) |
| 24 | 2 | gold (u16 tenths) | | 322 | 7x30x2 | spell strengths |
| 26 | 2 | rations | | 742 | 9x6 | equipment |
| 30 | 2 | conditions (flags) | | 796 | 24x6 | backpack |
| 42,50,..,98 | 4 each, stride 8 | strength, intelligence, dexterity, speed, stamina, luck, magicResistance, magicTalent | | | | |

ItemStack (6 bytes): count, charges, enchantment(NumRecharged), flags, itemId (int16).
Header: int16 nameLength, int16 unknown, name, int32 unknown, byte version, 7 unknown bytes,
int16 days, hours, minutes, mapId, partyX, partyY.
Offsets come from the read order in AlbLib's `NPC(BinaryReader)` and were checked against a real GOG
save (`SAVE.001`, character XLD at 39181, 58 subfiles): Tom level 5 (offset 5), LP 12/20, SP 0/0,
experience 400 at 238 (239 would give 1 / 1610612736 - upstream's commented writer was wrong),
gold 0 / Mellthas 12.2 / Khunag 24.0, rations 27, age 28, strength 42/70; magic flags match the classes
(Sira DjiKas, Mellthas Druidic, Harriet EnlightenedOne, Khunag OquloKamulos); appearance/face/
inventoryPicture = PlayableCharacter ids 1..10; equipment/backpack item ids resolve (378 overall,
74 boots, 329 gold ring, 291 stimdrink, 73 shoes, 306 cup). Load -> download and load -> GET/PUT all ->
download are byte-identical on that save. Header of that save: name "albion1", version 1.38, day 1,
02:38, map 300 at (133, 50).

### Character field limits & corrections (implementation of the section below)
- `AlbLib.Core` `NPC`/`NPCLayout` (`PORT:` comments): `DialogueOptions`/`ResponseOptions` -> `EventSetId`/
  `WordSetId` (u16); `Morale` (offset 15, was inside an unknown block); `Age` is now a `CharacterAttribute`
  (106 current, 108 max); upstream's `BaseProtection/Protection/BaseDamage/Damage` (212..218, shifted by 2)
  replaced by `BaseDefense` 214, `BonusDefense` 216, `BaseAttack` 218, `BonusAttack` 220 plus growth fields
  226/228/230/234; 212 (SP boost) and the gaps 222..225, 232, 236 are kept as unknown bytes; gold is read
  and written as **unsigned** u16 tenths.
- Character JSON: top-level `age: {current, max}` and `morale`; `other` as listed above.
- Validation applies only to values that differ from the server state (untouched out-of-range values from
  a save are accepted and written back unchanged). Pairs: `max` must be in its range when changed;
  `0 <= current <= max` is checked when either side changed - the error `field` is `...current` if current
  changed, otherwise `...max`. Read-only fields: changing one -> `400 {error: "'eventSetId' is read-only",
  field: "other.eventSetId"}`. Header: hours 0..23, minutes 0..59, days/mapId/partyX/partyY 0..32767.
  Spell strengths keep the int16 range; item stacks as in "Item limits".
- `meta.limits` adds path keys `attributes.max|current`, `skills.max|current`, `age.max|current`,
  `lifePoints.max|current`, `spellPoints.max|current` (the leaf/container keys `attributes`, `skills`,
  `age`, `lifePoints`, `spellPoints` carry the same ranges); `gold` {0, 6553.5, step 0.1}; `morale`
  {0, 100}; `experience` {0, 2147483647}; `rations`/`trainingPoints` {0, 32767}; header keys as above.
  `meta.readOnly` lists the 10 read-only keys; `meta.rules = [{type: "currentLeMax"}]`.
- Verified on the real save: Tom strength 42/70, age 28/80, close combat 35/80, eventSetId 981; Rainer
  bonusAttack -5; Harriet wordSetId 319; Siobhan baseDefense 2, bonusDefense 9, baseAttack 4,
  bonusAttack -10; morale 0 for all; Tom growth 10/4/0/3. Round trips stay byte-identical.

## Item limits (added 2026-10-08)
Source: ITEMLIST.DAT (40 bytes/item). Item flags = uint16 at offset 27 (verified on the GOG data:
bit 0x04 = Stackable on all 225 stackable items; matches UAlbion `ItemFlags`). Per-slot stack cap 99
(UAlbion `ItemSlot.MaxItemCount = 99`; not in the data files, hard-coded in the game).
Charges / recharges: item def offsets 23 Charges, 24 EnchantmentCount, 25 MaxEnchantmentCount, 26 MaxCharges.

`GET /api/meta` -> each `items[]` entry becomes:
```json
{ "id": 291, "name": "stimdrink", "stackable": true, "maxCount": 99,
  "maxCharges": 0, "maxEnchantments": 0, "plotItem": false, "cursed": false,
  "slot": "None", "class": "Drink" }
```
`maxCount` = 99 if stackable else 1. Also `meta.limits.stackMax = 99`.

`PUT .../characters/{index}` validates each ItemStack **whose content changed vs the current server state**
(untouched stacks from the save are never rejected), only when game data is loaded:
- itemId must be 0 or a known item id
- itemId != 0 -> 1 <= count <= maxCount; itemId == 0 -> count must be 0 (normalise charges/enchantment/flags to 0)
- charges <= maxCharges, enchantment <= maxEnchantments
Errors: `400 { "error": "...", "field": "backpack[3].count" }` (same path style as before).
Without game data: only the byte-range checks apply (count 0..255), as today.

## Character field limits & corrections (added 2026-10-08, approved by user)
Evidence: 18 real records (user's SAVE.001 + XLDLIBS/INITIAL/PRTCHAR*.XLD) and UAlbion's CharacterSheet layout.
Every attribute/skill `max` in the data is <= 99 (age max <= 100); no negatives anywhere.

### Field corrections (offsets in the 940-byte record)
| Old JSON key | Offset | New JSON key | Editable | Notes |
|---|---|---|---|---|
| other.dialogueOptions | 18 u16 | other.eventSetId | **read-only** | script/event set link (UAlbion EventSetId) |
| other.responseOptions | 20 u16 | other.wordSetId | **read-only** | word set link (UAlbion WordSetId) |
| other.baseProtection | 212 | (removed) | — | 212 is actually SpellPoints "boost"; keep bytes untouched |
| other.protection/baseDamage/damage | 214/216/218 | (removed) | — | were shifted by 2 |
| — | 214 u16 | other.baseDefense | **read-only** | |
| — | 216 s16 | other.bonusDefense | **read-only** | signed, game-computed from equipment |
| — | 218 u16 | other.baseAttack | **read-only** | |
| — | 220 s16 | other.bonusAttack | **read-only** | signed |
| — | 226/228/230/234 u16 | other.levelsPerActionPoint / lifePointsPerLevel / spellPointsPerLevel / trainingPointsPerLevel | **read-only** | growth per level (info) |
| other.age (scalar) | 106 | `age: {current,max}` (top-level pair) | yes | Age is a current/max/boost/backup block like attributes |
| — | 15 u8 | `morale` (top-level) | yes, 0..100 | UAlbion: Morale [0..100] |
Attribute/skill blocks are 8 bytes: current, max, boost, backup (u16). Boost/backup are not exposed and always preserved.
LP (202) and SP (208) are 6 bytes: current, max, boost.

### Limits (enforced by backend on PUT for **changed** values only; mirrored in UI)
| Field | Range | Rule |
|---|---|---|
| attributes.*.max, skills.*.max | 0..99 | |
| age.max | 0..100 | |
| attributes/skills/age .current | 0..max | checked if current or max changed |
| lifePoints/spellPoints .max | 0..32767 | |
| lifePoints/spellPoints .current | 0..max | |
| gold | 0..6553.5 step 0.1 | stored as **unsigned** u16 tenths (fix signed read) |
| rations, trainingPoints | 0..32767 | |
| experience | 0..2147483647 | |
| level, actionPoints, appearance, face, inventoryPicture | 0..255 | level cap unknown |
| morale | 0..100 | |
| header.hours 0..23, header.minutes 0..59, header.days 0..32767, mapId/partyX/partyY 0..32767 | | |
Read-only fields: a PUT that *changes* one -> `400 {error:"'eventSetId' is read-only", field}`; unchanged values are accepted.
Untouched values that are already out of range in a save are never rejected.

### Meta additions
`meta.limits` keys for the above, using path keys where ambiguity exists (e.g. `"attributes.max"`, `"skills.max"`,
`"age.max"`, `"lifePoints.max"`), plus `meta.readOnly: ["eventSetId","wordSetId","baseDefense","bonusDefense",
"baseAttack","bonusAttack","levelsPerActionPoint","lifePointsPerLevel","spellPointsPerLevel","trainingPointsPerLevel"]`
and `meta.rules: [{ "type": "currentLeMax" }]` (current <= max for every {current,max} pair).
