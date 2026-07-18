# PkhexMobile — UI Status (Phase 2)

Read-only party list + detail screen, built on top of the per-generation
save-parsing verification in `verify/GenN/PROGRESS-genN.md`. No editing
features in this pass.

## What was built

- **Party list** (`PartyListPage`): after a save is parsed, tapping "View
  Party" shows a scrollable list of `sav.PartyData` - species name,
  nickname, level per slot.
- **Detail screen** (`PokemonDetailPage`): tapping a party entry shows
  species, nickname, level, nature, ability, four moves, and IVs/EVs.
- Both pages are fully generic - no per-generation branching. Every field
  read (`Species`, `Nickname`, `CurrentLevel`, `Nature`, `Ability`,
  `Move1..4`, `IV_*`, `EV_*`) is an abstract member on the base `PKM`
  class, implemented uniformly by every generation's PK1..PK9 subclass.
  Generations that don't have a concept (Gen1/2 have no Nature/Ability)
  return library-defined sentinel values (`Nature.Hardy` / `Ability = -1`)
  rather than throwing, so the UI shows "—" instead of needing a
  generation check.

## Bug found and fixed during verification

`OnViewPartyClicked` originally passed the `SaveFile` object through
`Shell.Current.GoToAsync(route, new Dictionary<string, object> {...})`,
with the destination page receiving it via `[QueryProperty]`. This
crashed on-device with:

```
System.InvalidCastException: Object must implement IConvertible.
  at Microsoft.Maui.Controls.ShellContent.ApplyQueryAttributes
```

Root cause: Shell tries to coerce every query-dictionary value while
resolving the implicit `ShellContent` for a route registered via
`Routing.RegisterRoute` (i.e. a route with no literal `<ShellContent>` in
`AppShell.xaml`) - this happens before the destination page's own
`[QueryProperty]`/`IQueryAttributable` handling ever runs, and fails for
any object that isn't `IConvertible` (which `SaveFile` and `PKM` aren't).
This never showed up in a plain `dotnet build` - it only surfaces at
runtime, on-device, which is exactly why this verification pass mattered.

Fix: added `NavigationState` (`NavigationState.cs`), a small static
hand-off - the sender sets `NavigationState.PendingSave` /
`PendingPokemon` and navigates with a bare route (no dictionary); the
receiving page reads and clears it in `OnAppearing()`. Both
`PartyListPage` and `PokemonDetailPage` were switched from
`[QueryProperty]` to this pattern.

## Per-generation UI verification

Tested on the `PkhexMobile_Emulator` AVD (API 36). Screenshots taken for
list and detail views in all three generations below.

| Gen | Method | Party list | Detail screen | Notes |
|---|---|---|---|---|
| 1 | Real file, via file picker (`SAV1`, box slot populated so `Write()`+`SaveUtil.GetSaveFile()` re-detects it - see `verify/Gen1/PROGRESS-gen1.md`) | ✅ SPARKY/Pikachu Lv.10, Mew Lv.5 | ✅ SPARKY: Species Pikachu, Nature Hardy, Ability —, Moves —, IVs/EVs 0 (fields never set on the test mon) | Species name resolved correctly despite Gen1's internal-vs-National-Dex index quirk |
| 5 | Real file, via file picker (`SAV5BW`, round-trips cleanly out of the box) | ✅ LEAFY/Snivy Lv.12, Oshawott Lv.8 | ✅ LEAFY: Species Snivy, same unset-field pattern as above | First real end-to-end test of the fixed navigation bug |
| 9 | In-memory injection (`SAV9SV` constructed directly, routed through the same `loadedSave → View Party → GoToAsync` path as the real flow) | ✅ SPRIG/Sprigatito Lv.15 | ✅ SPRIG: Species Sprigatito, same unset-field pattern | Gen9 cannot be tested via the real file-picker flow - see below |

### Why Gen9 used in-memory injection instead of a real file

`verify/Gen9/PROGRESS-gen9.md` already documented that a library-generated
`SAV9SV` cannot round-trip through `Write()` + `SaveUtil.GetSaveFile()` -
the SCBlock deserializer throws on never-hydrated (`SCTypeCode.None`)
blocks, and there is no library-only way to produce a fully-typed blank
SV save (no shipped template resource exists). That is a save-parsing
limitation, not a UI limitation, so for this pass a temporary debug
button was added to MainPage that constructed a `SAV9SV` in-memory and
routed it through the exact same code path a real parsed save would use
(`loadedSave` field → `ViewPartyBtn` → `NavigationState` → `PartyListPage`
→ `PokemonDetailPage`). This isolates "does the UI work for Gen9 data"
from "can PKHeX.Core author a detectable Gen9 save file", which are two
different questions. The debug button was removed after taking the
screenshots - it is not part of the committed app.

### Fields shown as unset (Ability —, Moves —, IVs/EVs 0)

The test Pokémon across all three generations only had `Species`,
`CurrentLevel`, and (for two of them) `Nickname` explicitly set - Nature,
Ability, Moves, IVs, and EVs were left at their default/unset values.
The blank "—"/"0" values shown are therefore expected given the test
data, not a rendering bug. The UI code path itself does not distinguish
between "field is genuinely unset" and "field has a real value" - it
just displays whatever PKHeX.Core returns, which is the intended
generic, read-only behavior.

## Real-save inventory sweep (2026-07-18)

A console harness (`verify/Inventory/Program.cs`, references `PKHeX.Core`
directly, no Android/emulator dependency) walked every file under
`C:\Users\abhis\Downloads\sav files pkmn` (256 files, recursive) and ran
`SaveUtil.GetSaveFile(path)` on each one. This re-confirms, from a clean
sweep, the same file set that the per-gen `PROGRESS-genN.md` docs already
recorded as "VERIFIED against a REAL save file" on 2026-07-17 (commits
`6dacc46`, `66f3bb8`) - that on-device app UI verification (file picker →
party list → detail screen) is not repeated here, only the detection layer.

| Detected format | Gen | Files |
|---|---|---|
| `SAV1` | 1 | `POKEMON RED-0.sav` |
| `SAV2` | 2 | `Pokemon - Crystal Version (UE) (V1.1) C!.SAV` |
| `SAV3E` | 3 | `pokeemerald (2).sav` |
| `SAV3RSBox` | 3 | `01-GPXP-pokemon_rs_memory_box.gci` (GC box-storage only, PartyCount=0) |
| `SAV4HGSS` | 4 | `Pokemon Heart Gold Version.sav` |
| `SAV5BW` | 5 | `Pokemon Black Version.sav` |
| `SAV5B2W2` | 5 | `Pokemon Nero 2 Fix.sav`, `Pokemon White 2 Version.sav` |
| `SAV6AO` | 6 | `27.Current`, `main(6)`, `oldgen6&7saves\...Alpha Sapphire\oldASsave\main` |
| `SAV7SM` | 7 | `oldgen6&7saves\...Moon\oldMoonSave\main` |
| `SAV7USUM` | 7 | `main`, `oldgen6&7saves\...Ultra Moon\oldUMsave\main`, plus 68 single-party-slot dumps under `US - Encounters Master\` (one file per captured encounter) |
| `SAV7b` | 7b (LGPE) | `PLGP Master Trainer Counters\Final\savedata.bin` |
| `SAV8SWSH` | 8 | `pokemonsword_100\main` |
| `SAV9SV` | 9 | `pkmnscarlet_100\main` |
| `SAV9ZA` | 9 (Legends Z-A) | `pkmnlegendsza_100_21\main` |

Not recognized (checked individually, both expected):
- `pcdata-legendes Arceus.bin` (345,600 bytes) - not a Legends Arceus main
  save. `SaveUtil.SIZE_G8LA`/`SIZE_G8LA_1` require exactly 1,272,798 or
  1,288,454 bytes; this file is far smaller, and its "pcdata" name matches
  the pattern of a PC-box-only partial dump (same situation as the Gen3
  `SAV3RSBox` GCI file above). No SAV8LA (Legends Arceus proper) file was
  available in this sweep - Gen 8 real-save verification above rests on
  Sword/HeartGold only, not Legends Arceus specifically.
- `Pokemon Mystery Dungeon - Red Rescue Team (USA, Australia).SAV` - Mystery
  Dungeon is a different game family with its own save format; PKHeX.Core
  does not support it. Correctly unrecognized, not a bug.
- All individual per-Pokémon export files (`.pk3`, `.pb7`, etc.) and the
  `Box Data Dump.csv` under `PLGP Master Trainer Counters\` - these are
  single-entity exports, not save containers, so `SaveUtil.GetSaveFile`
  correctly returns null for them.

## Edit round-trip verification (2026-07-18)

`verify/EditRoundTrip/Program.cs` replicates the app's exact edit code path
(`pk.Nickname` / `pk.IsNicknamed` / `pk.CurrentLevel` →
`SaveFile.SetPartySlotAtIndex` → `SaveFile.Write()`, then reloads the
exported bytes through the same `SaveUtil.GetSaveFile(byte[])` call
`MainPage.TryParseSaveFile` uses when a file is picked) against real save
files for Gen1, Gen5, and Gen9:

| Gen | Save | Edit applied | Round-trip |
|---|---|---|---|
| 1 | `POKEMON RED-0.sav` (real) | Nickname MEW→TESTEDIT, Level 100→42 | ✅ PASS |
| 5 | `Pokemon Black Version.sav` (real) | Nickname Snake→TESTEDIT, Level 100→55 | ✅ PASS |
| 9 | `pkmnscarlet_100\main` (real) | Nickname Skeledirge→TESTEDIT, Level 100→88 | ✅ PASS |

All three: nickname and level read back correctly from the re-parsed
exported bytes, and the original file on disk was confirmed byte-for-byte
unchanged after the run (the export path only ever produces an in-memory
`byte[]`, handed to `FileSaver` for a new file in the app — never written
back to the original path).

Note for future harness authors: `SaveUtil.GetSaveFile(byte[])` does not
clone the array it's given - it wraps it as `Memory<byte>`, so subsequent
edits/`Write()` calls mutate that same array in place. The first version of
this harness compared the post-edit array against itself and wrongly
flagged the original file as changed; fixed by cloning the byte array
immediately after `File.ReadAllBytes` before ever passing it to
`SaveUtil.GetSaveFile`. Not an app bug (the app never writes the picked
byte array back to `path`), but worth knowing before writing similar
verification code.

## Known limitations / not covered in this pass

- No real Pokémon save files were used anywhere in this project, for any
  generation - only library-generated or hand-verified-recipe saves.
- Only one sub-version per generation was exercised in the UI (Gen1 RBY,
  Gen5 BW, Gen9 SV); other sub-versions (Crystal, B2W2, Legends Arceus,
  etc.) were checked at the save-parsing level in `verify/GenN/` but not
  separately through this UI.
- Editing is out of scope for this pass by design - everything above is
  read-only display.
