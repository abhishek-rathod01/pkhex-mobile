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

## IV/EV editing + round-trip verification (2026-07-18)

Detail screen: IVs and EVs (all six stats each) are now editable, following
the same pattern as nickname/level - `PokemonDetailPage.xaml.cs` applies
`pk.IV_*`/`pk.EV_*` via PKHeX.Core's own setters, then the existing
`SetPartySlotAtIndex` + `Write()` + `FileSaver` export flow (unchanged).
Input is range-validated generically (IV 0-31, EV 0-252) exactly like the
existing level validation - **not** per-generation, matching the project's
"fully generic, no per-generation branching" design principle already
established for the read-only UI.

`verify/EditRoundTrip/Program.cs` was extended to also set all 6 IVs/EVs
(distinct values per stat, chosen to make any stat mixup visible in the
diff) alongside nickname/level, against the same three real saves as
before:

| Gen | Round-trip (export+reload preserves in-memory value) |
|---|---|
| 1 | ✅ PASS |
| 5 | ✅ PASS |
| 9 | ✅ PASS |

All three gens preserve exactly what the `PKM` object held right before
`Write()` - export/reload serialization itself is not lossy for IVs/EVs on
any tested gen. No stop-and-log condition was hit.

### Genuine finding: Gen1/2 silently normalize IV/EV input, not a round-trip bug

The harness first asserted round-trip success against the *requested* input
values (5, 11, 17, 23, 29, 31 for IVs; 4, 12, 20, 28, 36, 44 for EVs) and
Gen1 "failed" - but the correct comparison is requested-input vs. what
`PKM` actually stores after the setter runs (pre-export), not
requested-input vs. post-reload. Redoing the test against that in-memory
baseline, Gen1 passes cleanly. The real story, confirmed by reading
`vendor/PKHeX.Core/PKM/Shared/GBPKM.cs:185-191`:

- Gen1/2 IVs are 4-bit DVs (real hardware range 0-15, not the 5-bit 0-31
  range Gen3+ uses). `IV_ATK`/`IV_DEF`/`IV_SPA`(`IV_SPC`)/`IV_SPE` setters
  clamp any input above 15 down to 15 (`value > 0xF ? 0xF : value`) -
  silently, no exception, no signal back to the caller.
- `IV_HP` has **no independent storage** - its setter is a no-op
  (`set { }`) - the getter derives it from the low bit of the other four
  DVs (`((IV_ATK&1)<<3)|((IV_DEF&1)<<2)|((IV_SPE&1)<<1)|((IV_SPC&1)<<0)`).
  This is a faithful reproduction of real Gen1/2 game mechanics, not a
  library bug.
- `IV_SPD`/`EV_SPD` setters are also no-ops - Gen1/2 has one shared
  Special stat, so `IV_SPD`/`EV_SPD` always mirror whatever `IV_SPA`/
  `EV_SPA` was last set to.
- This exactly mirrors the already-documented Gen3 quirk in
  `verify/Gen3/PROGRESS-gen3.md` (Nature/Ability/Gender setters are no-ops
  because those are PID-derived, not stored fields) - a class of "the
  setter exists to satisfy the abstract `PKM` contract but the generation
  genuinely doesn't have that as independent state" behavior, not corruption.

**Net effect on the current generic UI**: a user editing a Gen1/2 mon's IVs
today can type e.g. `23` into the SpA field and see it silently become `15`
after saving, with no in-app message explaining why, and editing "HP IV" or
"SpD IV/EV" independently has no effect at all (they always mirror/derive
from other fields). The export itself is correct and lossless for what
PKHeX.Core actually stored - the gap is UI feedback, not data integrity.
Deliberately **not fixed in this pass**: making the IV/EV editor
generation-aware (capping DV fields at 15, disabling/hiding the derived
HP field, merging SpA/SpD into one input for Gen1/2) would introduce the
per-generation branching the UI has intentionally avoided everywhere else.
Flagged in `WAKEUP.md` as a design decision for the user rather than
worked around silently.

### Follow-up checks after initial round-trip pass

Two gaps were identified in review of the initial IV/EV round-trip pass and
closed:

1. **Party stat block staleness.** `pk.CurrentLevel = level` writes EXP, not
   the party save's separately-stored computed stat block
   (`Stat_Level`/`Stat_HPMax`/`Stat_HPCurrent`/`Stat_ATK`/etc.) - it was
   possible the exported save would show the new level in the UI while the
   actual in-game stats stayed frozen at the old level, which would be a
   real, usable-save-breaking defect the original test didn't check because
   it only read back `CurrentLevel`/`IV_*`/`EV_*`. Extended
   `verify/EditRoundTrip/Program.cs` to also dump `Stat_Level`/`Stat_HPMax`/
   `Stat_HPCurrent`/`Stat_ATK` after reload. Confirmed correct on all three
   gens - e.g. Gen9 Scarlet: level edited 100→88, `Stat_Level=88`,
   `Stat_HPMax=367` (not the level-100 value). `SaveFile.SetPartySlotAtIndex`
   → `SetSlotFormatParty` → `SetPartyValues(pk, isParty: true)` recalculates
   the stat block as part of the existing write path - no extra call needed
   from the app.
2. **String encoding edge case.** All prior nickname tests used plain ASCII.
   Added a fourth round-trip case against the real Legends Z-A save
   (`pkmnlegendsza_100_21\main`), which has genuine CJK nicknames, editing a
   party member's nickname to a mixed Japanese/Chinese string
   (`テスト測試`, level 100→77). Round-tripped correctly.

### Caveat: "reload through the file picker" was verified at the library level, not on-device

Task C/D asked to "export an edited save, reload it through the file
picker, confirm ... read back correctly." `verify/EditRoundTrip/Program.cs`
calls the exact same PKHeX.Core APIs the app's `FileSaver`-export and
`FilePicker`-reload paths call
(`SetPartySlotAtIndex`/`Write()`/`SaveUtil.GetSaveFile(byte[])`), so this is
a faithful proxy for the underlying data-correctness question - but the
actual on-device `FileSaver.Default.SaveAsync` write and
`FilePicker.Default.PickAsync` read were **not** exercised end-to-end in
this session (no emulator UI driving was done for the edit flow). This
project has one documented precedent (the Shell-navigation
`InvalidCastException` in the "Bug found and fixed during verification"
section above) of a bug that only surfaced on-device and never showed up in
`dotnet build`. The edit/export/reload *data path* is verified; the
on-device *file I/O plumbing* around it is not, in this pass.

## Gen1/2 IV/EV field caps — design decision resolved (2026-07-18)

The open design question from the previous session ("Needs your decision" in
`WAKEUP.md`) is resolved: **option 3** (generation-aware field caps), not
option 1 (leave as-is) or option 2 (post-save normalization message).

This matches PKHeX desktop's verified approach (`PKHeX.WinForms/Util/
WinFormsUtil.cs` uses `NumericUpDown` controls with `Maximum` set to 15 for
Gen1/2 IV controls, plus a `SetValueClamped` backstop for programmatic sets)
rather than the "accept anything, then silently normalize on save" behavior
the app had before this pass.

`PokemonDetailPage.xaml[.cs]` changes:

- **IV field cap**: `ivMax` is now computed per-Pokémon from `pk.Generation`
  (15 for Gen1/2, 31 for Gen3+) instead of a hardcoded 31. A `TextChanged`
  handler on the four independently-stored IV fields (Atk/Def/SpA/Spe)
  clamps any typed value above `ivMax` back down live, as the user types -
  out-of-range values can't be entered in the first place, not just rejected
  at save time. The save-time `TryParseStat` check was also switched from a
  hardcoded 31 to the same `ivMax`, so it stays correct as a second-layer
  backstop even though the live clamp should make it unreachable in normal
  use.
- **HP IV (derived, no storage)**: `IvHpEntry.IsEnabled = false` for Gen1/2;
  its text is recomputed live from the other four DVs' low bits
  (`((atk&1)<<3)|((def&1)<<2)|((spe&1)<<1)|(spa&1)`, matching
  `GBPKM.cs:185`) whenever any of them changes, so it always shows what will
  actually be saved instead of accepting input that has no effect.
- **SpA/SpD linkage**: `IvSpdEntry` and `EvSpdEntry` are also disabled for
  Gen1/2 and mirror `IvSpaEntry`/`EvSpaEntry` live - Gen1/2 has one shared
  "Special" DV/stat-exp value (`GBPKM.cs:182-183,190-191`), so the two
  fields can no longer be edited independently and silently diverge from
  what gets written.
- **Defensive clamp backstop (d)**: `LoadPokemon` now populates every IV
  entry via `Math.Clamp(p.IV_X, 0, ivMax)` rather than a raw `.ToString()`,
  so even a PKM object that somehow held an out-of-range value (corrupted
  data, future format) can't get echoed into a field whose cap it violates.
  The section label (`IvRangeLabel`) also switches text for Gen1/2 to spell
  out the derived/linked fields.
- **Out-of-range load safety (e)**: `verify/Gen12IvCap/Program.cs` confirms
  two things can't crash the app: (1) forcing an over-range IV through the
  real PKM setters (`pk.IV_ATK = 999` etc.) against real Gen1 (`POKEMON
  RED-0.sav`) and Gen2 (`Pokemon - Crystal Version (UE) (V1.1) C!.SAV`)
  saves - the library's own setter clamp (`GBPKM.cs:186-189`,
  `value > 0xF ? 0xF : value`) already catches this before the app-level
  clamp would ever see it, and `SetPartySlotAtIndex`/`Write()`/reload all
  survive; (2) the app's own `Math.Clamp` backstop against adversarial
  synthetic input (`int.MaxValue`, `int.MinValue`, etc.) directly, since a
  real Gen1/2 DV is bit-packed into a 4-bit nibble and so **cannot**
  structurally exceed 15 in genuine save data - true "out of range" input
  can only originate from a bug or a future format, which is exactly what
  the synthetic-input half of the harness checks against. Both halves pass
  for all six probed values on both real save files.

### Follow-up gaps closed after advisor review

1. **Gen3+ HP/SpD IV fields weren't live-clamped.** The initial pass only
   wired the live `ivMax` clamp to the four independently-stored IV fields
   (Atk/Def/SpA/Spe), since HP/SpD are derived/linked-and-disabled for
   Gen1/2. But for Gen3+, HP and SpD *are* independently editable, and typing
   an out-of-range value into them was only caught at save time, not live -
   missing Item 1's stated goal ("out-of-range values can never be entered
   in the first place") for 2 of 6 fields. Fixed by wiring `IvHpEntry`/
   `IvSpdEntry` to the same clamp handler as the other four (harmless no-op
   for Gen1/2, where they're disabled anyway).
2. **Genuine pre-existing bug found, not fixed (out of scope for Item 1):**
   Gen1/2 "EVs" are actually 16-bit Stat Exp (hardware range 0-65535), not
   the modern 0-252 EV system - confirmed against the real Gen1 save
   (`POKEMON RED-0.sav`, MEW at level 100 has `EV_HP=EV_ATK=...=65535`,
   maxed stat exp). The EV entry fields still parse into a `byte` and
   validate against 252 for every generation, unchanged from before this
   pass. Effect, not just cosmetic: `byte.TryParse("65535")` **fails
   outright** (doesn't fit in a byte at all), so `OnSaveChangesClicked`
   reports "EVs must be numbers between 0 and 252" and **blocks saving any
   edit at all** - including a nickname-only change - on a Gen1/2 mon that
   already has real stat exp. This is a pre-existing bug, not something
   Item 1 introduced (the old hardcoded-252 validation had the exact same
   `byte`-parse failure); it was only surfaced now because Item 2's
   on-device verification is the first pass to actually drive
   `OnSaveChangesClicked` against a real Gen1/2 save with real stat exp
   values loaded into the UI. Fixing it properly (widening the EV field
   type past `byte` and making the cap generation-aware, 0-65535 for
   Gen1/2 vs 0-252 for Gen3+) is its own scope, not Item 1's "IV caps
   only" - logged here so it isn't silently rediscovered. Item 2's
   full save round-trip is therefore run against a Gen3+ save instead of
   Gen1/2; the Gen1/2 pass in Item 2 is display/cap-only (no Save Changes
   click).

## On-device edit flow verification (2026-07-18)

Item 2 asked for the full edit flow (load real save → view party → edit
nickname/level/IV/EV → save → reload → confirm) to be driven at least once
through the actual on-device `FileSaver`/`FilePicker` UI, not the
library-level proxy `verify/EditRoundTrip/Program.cs` used - closing the
caveat that document had carried since it was written. Done on the
`PkhexMobile_Emulator` AVD (API 36); screenshots in
`verify/OnDeviceEdit/screenshots/`.

**Environment note for future sessions:** installing the Debug APK directly
via `adb install` (bypassing the .NET Android build's own deploy step)
crashes on launch with `SIGABRT` / *"No assemblies found in
'.../files/.__override__/x86_64' ... Assuming this is part of Fast
Deployment. Exiting..."* - Debug builds default to Fast Deployment, which
expects the tooling to push assemblies to that override directory
separately from the APK; a bare `adb install` never does that. Fix: deploy
with `dotnet build PkhexMobile/PkhexMobile.csproj -f net10.0-android
-t:Run`, which both builds and correctly installs/launches. Cost about 40
minutes of this session chasing silent launch failures (`am start` printing
no error yet the process never appearing in `adb shell ps`) before finding
the crash in `adb logcat -b crash`. Also note plain `dotnet build` (no
`-t:Run`) does *not* reliably refresh the already-installed `-Signed.apk`
on disk even when source changed and it reports "Build succeeded" - always
check the APK's file timestamp against the source files' before trusting
`adb install -r` of a pre-existing APK path, or just use `-t:Run`/`-t:Rebuild`.

**Full round-trip (Gen5, `gen5_real.sav` = `Pokemon Black Version.sav`,
pushed to `/sdcard/Download/`):** loaded via the real file picker → View
Party → tapped "Snake" (Serperior) → edited Nickname `Snake→TESTEDIT2`,
Level `100→55`, IV SpA `23→31`, EV HP `100→50` through the on-screen
keyboard → Save Changes → the real `FileSaver` document-picker dialog
appeared → **caught and corrected an autocomplete hazard**: the save
dialog's filename field pre-filled/autocompleted to `gen5_real.sav`
(the original file's name) partway through the flow; saving with that name
would have overwritten the real save. Cleared it and typed a distinct name
(`gen5_edited_item2.sav`) before confirming - the app itself never
attempted to touch the original path, this was a picker-UI autocomplete
risk caught before it became a mistake. Exported to
`/storage/emulated/0/Download/gen5_edited_item2.sav` ("Saved to: ..."
shown in-app). Reloaded that file through the real file picker from Home
→ View Party → tapped the party entry (now showing nickname **TESTEDIT2**
in the list itself, confirming the export) → detail screen showed
Nickname `TESTEDIT2`, Level `55`, IV SpA `31`, EV HP `50` - all edits
round-tripped correctly through the real on-device file I/O, not just the
library-level proxy.

**Gen1/2 cap enforcement (`gen1_real.sav` = `POKEMON RED-0.sav`, MEW):**
confirmed visually and structurally, without clicking Save (see the
Gen1/2 EV bug logged above - saving this mon would hit the byte-parse
block). The IV label read *"IVs / DVs (0-15 each; HP derived, SpD linked
to SpA)"*; `adb shell uiautomator dump` confirmed the HP and SpD **IV**
entries and the SpD **EV** entry all have `enabled="false"` in the live
view hierarchy (not just visually greyed - genuinely non-interactive).
Typing `99` into the Atk IV field live-clamped to `15` as each keystroke
landed, with no separate submit step needed - confirming Item 1(a)/(d)'s
"can't be entered in the first place" claim holds in the running app, not
only in code review. The EV row showing `65535` for every stat visually
corroborates the Gen1/2 EV finding logged above.

## PC box viewing, read-only (2026-07-18)

Extends the existing party-list pattern to PC boxes: `BoxListPage`
(`BoxListPage.xaml[.cs]`) adds a box-name `Picker` above a `CollectionView`
that reuses the same `PartyEntryDisplay` record and item template as
`PartyListPage`. `MainPage` grows a "View Boxes" button next to "View
Party", shown only when `sav.HasBox` (not every save type has PC storage).
Tapping a box entry opens the existing `PokemonDetailPage`.

- **Box names**: read via `IBoxDetailNameRead.GetBoxName(box)` when the
  save implements it, falling back to `BoxDetailNameExtensions
  .GetDefaultBoxName(box)` ("Box N") otherwise - same pattern
  `SlotCache.cs` already uses internally in PKHeX.Core, not invented here.
- **Empty slots are filtered**: unlike `PartyData` (always fully populated
  for however many mons are in the party), a box is `BoxSlotCount` slots
  (20 for Gen1/2, 30 for Gen3+) mostly empty - `GetBoxSlotAtIndex(box,
  slot)` is called for every slot and any result with `Species == 0`
  (PKHeX.Core's standard empty-slot sentinel, confirmed via existing usage
  in `SaveFile.cs`/`SAV4.cs`) is skipped rather than rendered as a blank row.
- **Read-only guarantee - navigation-level, not a UI toggle**: box entries
  navigate to `PokemonDetailPage` with `NavigationState.PendingPokemonSave`
  explicitly set to **null** (rather than the real `SaveFile`, as
  `PartyListPage` does). `PokemonDetailPage.LoadPokemon` already computes
  `SaveChangesBtn.IsVisible = parentSave is not null`, so the Save Changes
  button is hidden entirely for box entries with no page-specific code
  needed - the existing party detail page's own null-check *is* the
  read-only guarantee, not a new one bolted on. This also sidesteps a real
  hazard: `OnSaveChangesClicked` writes via `SaveFile.SetPartySlotAtIndex`,
  which is party-slot-indexed (max 6 slots) - had a box mon's save been
  routed through that path with a real `SaveFile` and a box-slot index
  (0-29), it would have written into the wrong party slot or thrown/
  corrupted data, since box storage and party storage are separate regions
  entirely (`GetBoxSlotOffset` vs `GetPartyOffset`, unrelated address
  spaces). Nickname/Level/IV/EV fields are still populated and interactively
  focus/type-able (they're just `Entry` controls, no per-page read-only
  flag exists or is needed) - the `null` parent save is the only guard, and
  it's sufficient because nothing downstream of "no Save button" can
  persist a change.

Verified against two real saves with populated boxes, chosen via a quick
inventory harness (`verify/BoxInventory/Program.cs`) that counts non-empty
slots per box across all real saves in the folder before committing to
which ones to drive through the emulator:

| Gen | Save | Result |
|---|---|---|
| 1 | `POKEMON RED-0.sav` (`gen1_real.sav`) | 12 boxes, default "Box N" names, 235 occupied slots total. Box 1: 20/20 shown correctly. Switched to a different box via the Picker (all 12 box names listed) - different Pokémon shown. Tapped a box entry - **no Save Changes button appeared**, fields showed real Nickname/Level/IV(0-15 range, Gen1)/EV/Nature/Ability/Moves. |
| 9 | `pkmnscarlet_100\main` (`gen9_real.sav`) | 32 boxes, real "Box N" names from the save itself (not the default fallback), 618 occupied slots total. Box 1: 30/30 shown correctly, matching the harness's count exactly. Tapped a box entry (Floragato) - real IVs (0-31 range, Gen9), Nature Impish, Ability Overgrow, real moves shown, **no Save Changes button**. |

Screenshots in `verify/OnDeviceBoxes/screenshots/`.

Not done (explicitly out of scope for this pass, per instructions): no
box→party moves, no slot swaps, no box editing. Species/move editing and
legality checking are also explicitly deferred to a future session.

## Known limitations / not covered in this pass

- No real Pokémon save files were used anywhere in this project, for any
  generation - only library-generated or hand-verified-recipe saves.
- Only one sub-version per generation was exercised in the UI (Gen1 RBY,
  Gen5 BW, Gen9 SV); other sub-versions (Crystal, B2W2, Legends Arceus,
  etc.) were checked at the save-parsing level in `verify/GenN/` but not
  separately through this UI.
- Editing is out of scope for this pass by design - everything above is
  read-only display.

## Gen1/2 EV save-blocking bug fixed (2026-07-20)

Fixed the bug logged in the previous session's "Known bug, not fixed" section
(`WAKEUP.md`): EV fields parsed into a `byte` and validated against a
hardcoded 252 cap for every generation, so `byte.TryParse("65535")` failed
outright and `OnSaveChangesClicked` blocked saving *any* edit - even a
nickname-only change - on a Gen1/2 mon that already had real stat exp loaded.

Mirrors exactly what the earlier IV cap fix (`df48f97`/`f4d88e8`) did, just
for EVs:

- `PokemonDetailPage.xaml.cs` gains an `evMax` field, computed the same way
  as `ivMax` (`isGen12 ? 65535 : 252`), since Gen1/2 "EVs" are real 16-bit
  Stat Exp, not the modern 0-252 EV system.
- All six EV `Entry` fields are live-clamped to `evMax` as the user types
  (`OnEvIndependentEntryTextChanged`, wired the same way the IV fields
  already were), not just checked at save time.
- `LoadPokemon` populates EV fields via `Math.Clamp(p.EV_X, 0, evMax)`
  (defensive backstop, same reasoning as the existing IV backstop) and the
  EV section label (`EvRangeLabel`, newly named in the XAML) switches text
  for Gen1/2 to spell out the SpD-linked-to-SpA relationship, mirroring
  `IvRangeLabel`.
- `TryParseStat` and `ClampEntryToMax` widened from `byte`/`byte.TryParse`
  to `int`/`int.TryParse` to support the 0-65535 range (`EV_*`/`IV_*` are
  `int` on the abstract `PKM` class already, so no cast is needed at the
  assignment sites). `TryParseStat` also gained an explicit `parsed < 0`
  rejection, since widening to `int` opened up negative values that
  `byte.TryParse` could never produce.
- Save-time validation (`OnSaveChangesClicked`) now checks EVs against
  `evMax` instead of a hardcoded `252`, with the error message updated to
  match (`"EVs must be numbers between 0 and {evMax}."`).

### Verification against the real Gen1 save with maxed stat exp

Driven on-device (`PkhexMobile_Emulator` AVD) against the real
`POKEMON RED-0.sav` (MEW, level 100, all six EVs at 65535 - the exact mon
that exposed this bug during Item 2 of the previous session):

1. **Detail screen shows the fix live**: EV section label reads *"EVs / Stat
   Exp (0-65535 each; SpD linked to SpA)"*, all six EV fields show `65535`
   (SpD greyed out, mirroring SpA), matching the values logged previously.
2. **Nickname-only edit now saves successfully**: changed only the nickname
   (MEW → EVFIXOK), left all six EVs untouched at 65535, tapped Save
   Changes - the real `FileSaver` document-picker dialog appeared
   immediately (previously this exact scenario was blocked before ever
   reaching that dialog, with "EVs must be numbers between 0 and 252").
   Exported successfully to `gen1_ev_fix_saved.sav`.
   - **Caught the same filename-autocomplete hazard documented in the
     previous session's Item 2**: the save dialog's filename field
     autocompleted to an unrelated existing file (`gen3_real.sav`) partway
     through. Caught before confirming, corrected to a distinct name, and
     verified the corrected text via `uiautomator dump` before tapping
     SAVE.
3. **EV value edit round-trips correctly on export/reload**: reloaded the
   just-saved file through the real file picker (confirmed Trainer ASH,
   Party 6, first entry "EVFIXOK" - i.e. actually the file just written,
   not a stale one), confirmed all six EVs read back as `65535`
   (proving the untouched EVs survived the earlier save uncorrupted), then
   edited EV Atk from `65535` to `12345`, saved again, and read the
   exported bytes back via a throwaway PKHeX.Core harness
   (`SaveUtil.GetSaveFile` on the pulled file): `Nickname=EVFIXOK
   Species=151 Level=100`, `EV: HP=65535 ATK=12345 DEF=65535 SPA=65535
   SPD=65535 SPE=65535` - exactly the edited value, with every other field
   preserved.

Screenshots in `verify/OnDeviceEvFix/screenshots/`. The one-off read-back
harness used for step 3 was deleted after use (not part of the committed
app); PKHeX.Core's own `SaveUtil.GetSaveFile`/party-read APIs were the only
thing it called, so this is a faithful proxy for "reload through the file
picker," matching the same caveat already documented for the IV/EV
round-trip work earlier in this file.

## Species + move editing, behind a legality warning (2026-07-20)

Extended the detail-page editor from nickname/level/IV/EV to also cover
**species** and the **four moves**, each via a `Picker` (dropdown) in
`PokemonDetailPage.xaml[.cs]`. Species and moves are no longer in the
read-only `StatsList` (Nature/Ability remain there - they're PID-derived on
several gens, so exposing them as free edits would mislead).

- **Species picker**: populated 1..`pk.MaxSpeciesID` (format-specific
  structural bound - a Gen9 'mon is never offered in a Gen1 file; this is a
  format constraint, not a legality judgement). `pk.Species = id` on save;
  the format setter does the derived-field work (e.g. Gen1 internal-index +
  stored types, confirmed via `PK1.SetSpeciesValues`).
- **Move pickers**: index 0 = "(None)" (clears a slot), 1..`pk.MaxMoveID`.
  Applied via **`pk.SetMoves(newMoves)`**, not raw `Move1..4`, so current PP
  is recomputed for the new moves (`SetMaximumPPCurrent`). Setting the IDs
  alone would leave stale PP from the previous moves - the move-PP analog of
  the level/stat-recalc class of bug.
- **Legality warning**: a persistent orange label under the species/move
  pickers, always visible: *"⚠ Species and move edits are applied exactly as
  chosen. PKHeX.Core does not auto-fix legality, so the result may be flagged
  as illegal by other tools. This is expected."* Plus, on a successful save
  where species/moves actually changed, the status line appends *"(Species/
  move edits are applied as-is; other tools may flag this as illegal.)"*. No
  auto-validation or auto-correction is performed anywhere (out of scope by
  request).

### Two real bugs found and fixed while verifying (not pre-planned)

The verification harness (`verify/SpeciesMoveEdit/Program.cs`) was written to
actively hunt for the edge-case classes earlier sessions were bitten by
(stale derived state, gen-specific coupling), and it surfaced two genuine
defects - **both also affected the pre-existing level/IV/EV edit path**, not
just the new species/move code:

1. **Party stat block never recomputed on edit.** `SaveFile.SetPartyValues`
   only calls `ResetPartyStats()` when `!pk.PartyStatsPresent` (i.e.
   `Stat_HPMax == 0`). Every real party mon already has stats present, so the
   stat block (HP/Atk/.../`Stat_Level`) was **never** recalculated on save -
   for species changes (a Charizard would keep Mew's HP) *and* for the older
   level/IV/EV edits. The previous session's PROGRESS note claiming
   `SetPartySlotAtIndex` recalculates the stat block was **wrong**; the
   harness proves it (species changed, HP unchanged). **Fix:**
   `PokemonDetailPage.OnSaveChangesClicked` now calls `pk.ResetPartyStats()`
   before `SetPartySlotAtIndex`, gated on `statsAffected` (species/level/IV/EV
   changed) so a nickname-only edit doesn't heal/clear status as a side
   effect. `ResetPartyStats`→`LoadStats` is generation-aware (Gen1/2 DVs +
   stat exp, PA8/PB7 overrides). Verified: Gen1 Mew(399 HP)→Charizard L50 now
   reads 182; Gen9 Skeledirge→Garchomp L50 reads 192.
2. **Level misread when species AND level both change (EXP/growth-rate
   ordering).** `CurrentLevel` is stored as EXP, and EXP↔level depends on the
   species' growth-rate group. The original code set `CurrentLevel` *before*
   `Species`, so a level set under the old growth rate was then reinterpreted
   under the new species' rate - e.g. a level-50 Skeledirge became a level-45
   Garchomp on reload. **Fix:** set `pk.Species` **before** `pk.CurrentLevel`.
   Verified: after the reorder, `Stat_Level == 50` for all three gens.

### Round-trip verification (`verify/SpeciesMoveEdit/Program.cs`)

Replicates the app's exact path (`pk.Species` → `pk.SetMoves` →
`ResetPartyStats` → `SetPartySlotAtIndex` → `Write()` →
`SaveUtil.GetSaveFile(byte[])`) against the real Gen1/Gen5/Gen9 saves, each
changing species + 4 moves (Gen5 also clears two slots to "(None)") + level:

| Gen | Save | Edit | Result |
|---|---|---|---|
| 1 | `POKEMON RED-0.sav` | Mew→Charizard, 4 moves, L50 | ✅ species/moves/PP/stat block + Gen1 stored types all round-trip |
| 5 | `Pokemon Black Version.sav` | Serperior→Pikachu, 2 moves + 2 cleared, L50 | ✅ cleared slots read back PP=0 |
| 9 | `pkmnscarlet_100\main` | Skeledirge→Garchomp, 4 moves, L50 | ✅ all round-trip |

Plus a **form-staleness probe**: the app doesn't edit Form, but if a loaded
mon has a non-zero Form and species changes, the stale Form persists (we
deliberately don't auto-correct - that's legality). Confirmed this cannot
crash: `PersonalInfo.HasForm` falls back to the base (form-0) entry for an
out-of-range form, so `ResetPartyStats`/`Write()`/reload all survive and
compute base-form stats (synthetic Garchomp Form=20 → HP 192 = base form).
All checks pass; originals confirmed byte-for-byte untouched on disk.

### On-device verification (`PkhexMobile_Emulator`, real FileSaver/FilePicker)

Drove the full flow against the real `gen9_real.sav`: loaded → View Party →
Skeledirge → changed **Species Skeledirge→Quaquaval**, **Move 1 Torch
Song→Aqua Step**, **Level 100→50** via the on-screen pickers/keyboard → Save
Changes → the real FileSaver dialog appeared, status showed the species/move
note → saved to a distinct name (`gen9_specmove_test.sav`, cleared the
pre-filled name per the documented autocomplete hazard) → reloaded that file
through the real file picker → party list shows slot 1 as species
**Quaquaval Lv.50** (nickname "Skeledirge" retained) → detail shows Species
Quaquaval, Move 1 Aqua Step, Level 50. All edits round-tripped through real
on-device file I/O. Screenshots in `verify/OnDeviceSpeciesMove/screenshots/`.

### Explicitly out of scope this pass (unchanged from prior sessions)

No legality validation or auto-correction (by request). Form is not
user-editable and not auto-reset on species change (safe - falls back to base
form). Nature/Ability remain read-only. Box (PC) mons stay read-only via the
existing null-`parentSave` guard (no Save button); the pickers render but
can't persist there, matching the existing nickname/IV/EV behavior.

## UI reskin: design system, sprites, held items, app icon (2026-07-21)

Full visual reskin of every screen (MainPage, PartyListPage, BoxListPage,
PokemonDetailPage) against a separately-authored design-handoff bundle at
`PkhexMobile Design System/design_handoff_pkhexmobile/` (tokens as CSS custom
properties + component/screen references as HTML/React prototypes - reference
only, not copied into the app). Explicitly **reskin + button wiring**, no
data-logic changes: every field, edit path, validation rule, and save/export
flow documented in the sections above is untouched and re-verified working
(see "On-device regression verification" below).

### Scope decision: direct-edit form kept, not the mockup's read-only-inspector paradigm

The design bundle's `DetailScreen.jsx` reference is a **read-only** inspector
(`DataRow`s) with reserved pencil-icon "Edit" affordances and a bottom
"Edit Pokémon" button, plus a `LegalityBadge`/Verify-button engine - all
explicitly marked "not yet designed" in the bundle's own README (no wired
logic behind any of it). The app's actual edit surface is the opposite:
direct inline `Entry`/`Picker` editing with no separate edit-mode. Adopting
the mockup's paradigm would have meant *removing* working, already-verified
editing controls to match an unwired prototype - contrary to "preserve all
existing functionality exactly." Resolution: kept the direct-edit form,
applied the design system's visual language (cards, type scale, color/
spacing/radius/shadow tokens, motion) to it, and did **not** build the
mockup's read-only inspector, `LegalityBadge`/Verify engine, Add-Pokémon/
Add-move affordances, or per-row legality dot - all of those are inert
non-functionality that the task's "confirm actual bound behavior, don't just
restyle" instruction rules out shipping. The one genuinely new *behavior*
(not just visual) added: dirty/clean Save button tracking (below).

### Design tokens → MAUI resources

- `Resources/Styles/Colors.xaml` - full color token port (neutrals, brand
  red, semantic/status, shiny, stat accents, 18-type palette) as MAUI
  `Color`/`SolidColorBrush` resources.
- `Resources/Styles/Tokens.xaml` (new file) - spacing scale, radii, type
  scale, line heights, motion durations, and a `Shadow` elevation ramp
  (`Border.Shadow` resources) - the non-color tokens that don't belong in
  `Colors.xaml`.
- `Resources/Styles/Styles.xaml` - rewritten: text-role styles
  (`ScreenTitleStyle`, `SectionTitleStyle`, `RowTitleStyle`, `HeroTitleStyle`,
  `BodyStyle`, `LabelCaptionStyle`, `MonoValueStyle`, etc.) mapped to the
  design's semantic type roles; `Button` variants (`PrimaryButtonStyle`/
  `SecondaryButtonStyle`/`GhostButtonStyle`/`DangerButtonStyle`) matching
  `components/controls/Button.jsx`'s 4-variant spec including the `Disabled`
  VisualState (`Opacity=0.5`); a `CardBorderStyle`/`RowBorderStyle` for the
  card/row surface language; `Entry`/`Picker`/`Switch`/`Page`/`Shell` base
  styles. Design system documents a single **light** palette only - no dark
  theme tokens were provided, so no `AppThemeBinding` dark branch exists;
  this is a scope observation, not an oversight.
- **Fonts**: the design specifies Space Grotesk (display), Manrope (body),
  JetBrains Mono (numeric/mono) from Google Fonts, at several named weights.
  Google Fonts' current `google/fonts` GitHub repo only ships **variable**
  fonts for these three families (no static per-weight files) - used
  `fonttools`' `varLib.instancer` to generate 12 static-weight `.ttf`
  instances (SpaceGrotesk Medium/SemiBold/Bold; Manrope Regular/Medium/
  SemiBold/Bold/ExtraBold; JetBrainsMono Regular/Medium/SemiBold/Bold) under
  `Resources/Fonts/`, each registered with its own alias in
  `MauiProgram.ConfigureFonts`. The stock `OpenSans-Regular/Semibold.ttf`
  template fonts were removed (unused once every style switched to the new
  families).
- **Motion**: `PressScaleBehavior.cs` (new) - a `Behavior<Button>` wired via
  `BaseButtonStyle.Behaviors` that plays the design's press feedback
  (`scale(0.97)` over 120ms, `Easing.CubicOut`) on every button via
  `Button.Pressed`/`Released`, matching `--dur-fast`/no-bounce motion spec.

### Save button: dirty/clean tracking (the one new *behavior*, not just style)

`PokemonDetailPage` previously left `SaveChangesBtn` always enabled. Per
design-notes.md's Save button spec (disabled clean → enabled on first edit →
disabled again after a successful save), added `isDirty`/`isLoading` fields:
every editable control (`NicknameEntry`/`LevelEntry`.`TextChanged`,
`SpeciesPicker`/`Move1-4Picker`.`SelectedIndexChanged`, plus the existing
IV/EV `TextChanged` handlers) now calls `MarkDirty()`, which is a no-op while
`isLoading` is true (guards against `LoadPokemon`'s own programmatic field
population being misread as a user edit). `OnSaveChangesClicked`'s success
branch resets `isDirty = false`. Verified on-device across Gen1/5/9 (see
below): button loads visibly faded/disabled on a fresh load, turns
full-opacity/enabled the instant any field changes (including a same-value
IV re-type, matching the design's "track edits, not value-diffs" spec), and
fades back to disabled immediately after a successful save.

### Sprites and held-item icons

- **Species sprites**: regular + shiny icons for National Dex #1-905,
  vendored from `msikma/pokesprite` into `Resources/Images/species/` as
  `spr_{id:D4}.png` / `spr_{id:D4}_s.png` (MAUI/Android image-resource names
  must be lowercase, start with a letter, and contain only `[a-z0-9_]` - no
  hyphens). pokesprite does not yet cover Generation 9 species (Scarlet/
  Violet/Legends Z-A, dex #906+); those have no file and fall back to the
  placeholder glyph (see below) - confirmed on-device against a real Gen9
  save (Skeledirge, dex #911).
- **Held-item icons**: vendored the same way into `Resources/Images/items/`
  as `item_{id:D4}.png`, but **keyed by PKHeX's own item ID space** (0-2683,
  read from `vendor/PKHeX.Core/Resources/text/items/text_Items_en.txt`,
  where item ID = line number − 1), matched to pokesprite's files by
  normalized English name - pokesprite's own internal item numbering does
  **not** correspond to PKHeX item IDs, so a numeric-ID-to-numeric-ID mapping
  would have silently produced wrong icons for most items. 933 of 2683 items
  matched (~35%); unmatched IDs (mostly TM/TR items, stored in pokesprite by
  type/number rather than a readable name, plus assorted items with no
  pokesprite icon at all) fall back to the placeholder.
- **Fallback strategy**: rather than maintaining a hardcoded valid-ID list
  that would go stale as sprite coverage changes, every sprite slot layers a
  static `sprite_placeholder.svg` (a neutral Poké Ball outline glyph,
  matching the design's `SpriteSlot` fallback spec) *behind* an `Image` bound
  to the computed filename. A MAUI `Image` that fails to resolve its `Source`
  simply renders nothing, so the placeholder shows through underneath with
  no broken-image glyph and no crash - confirmed on-device for both a
  missing species (Gen9 Skeledirge) and (implicitly) any of the ~65% of
  unmatched item IDs.
- `SpriteHelper.cs` (new) - `SpeciesSpriteFile(species, shiny)` /
  `ItemSpriteFile(itemId)`, the filename-computation half of the above.
  `PartyEntryDisplay` (used by both `PartyListPage` and `BoxListPage`'s
  shared row template) grew computed properties (`SpriteFile`, `IsShiny`,
  `ItemSpriteFile`, `HasItem`/`HasNoItem`, `ItemName`/`ItemFirstWord`) so the
  existing `record` continues to flow straight into data-bound
  `CollectionView` templates without new per-page glue.
  `PokemonDetailPage.RefreshHero`/`PopulateHeldItem` do the same for the
  single-item detail-screen hero and Main-card held-item row (a **new
  read-only display** - held item was previously not shown anywhere in the
  app at all; it is still not user-editable, matching the existing
  Nature/Ability read-only precedent).
- `PkhexMobile.csproj`'s `MauiImage` glob was `Resources\Images\*`
  (non-recursive) - would have silently skipped both new subdirectories.
  Changed to `Resources\Images\**\*`, plus `Resize="False"` overrides for
  the sprite/item PNGs so Resizetizer's default multi-density upscaling
  doesn't blur the pixel art.

### App icon and splash

Original Poké Ball SVG (not copied from any source) referencing the general
silhouette of the PKHeX desktop icon, at `Resources/AppIcon/appicon.svg`
(full icon, used standalone on non-adaptive-icon platforms) and
`appiconfg.svg` (foreground-only layer, inset for Android's adaptive-icon
safe zone, composited over the `Color="#F6F8FB"` background set on the
`MauiIcon` element in the `.csproj`). Same glyph reused for
`Resources/Splash/splash.svg` and as an in-app brand mark
(`Resources/Images/pokeball_mark.svg`, shown on `MainPage`). Android's
`Platforms/Android/Resources/values/colors.xml` (`colorPrimary`/
`colorPrimaryDark`/`colorAccent`) updated to the new brand red so
system-level chrome (status bar tint, native `Picker`/`Entry` focus
underline) matches instead of the stock MAUI purple.

### MainPage: template chrome removed

Dropped the stock "Hello, World! / Welcome to .NET MAUI" content, the
submarine `dotnet_bot.png` image, and the dead `CounterBtn`/`OnCounterClicked`
click-counter (unrelated template scaffolding, not app functionality) -
replaced with a branded header (Poké Ball mark + title) and the existing
Pick-a-file / View Party / View Boxes flow restyled into cards, functionally
unchanged.

### On-device regression verification

Driven on `PkhexMobile_Emulator` (real file picker/`FileSaver`, not a
library-level proxy), screenshots in `verify/UIReskin/screenshots/`:

| Gen | Save | What was exercised | Result |
|---|---|---|---|
| 5 | `gen5_real.sav` | Load → party list (sprites render) → detail (Main/Moves/Stats cards, legality banner) → nickname edit (Save button clean→dirty) → Save Changes → real FileSaver dialog → saved → button dirty→clean again → reloaded party list shows edited nickname | ✅ all pass |
| 1 | `gen1_real.sav` (MEW, maxed stat exp) | Party list sprites (Gen1 dex range fully covered) → detail shows Gen1/2-specific labels ("IVs / DVs (0-15 each; HP derived, SpD linked to SpA)", "EVs / Stat Exp (0-65535 each...)"), HP/SpD IV fields and SpD EV field visibly disabled and mirroring SpA, Ability shows "—", Nature shows "Hardy" (sentinel) | ✅ matches pre-reskin documented behavior exactly, no regressions |
| 1 | (same) | Typed "99" into an editable IV field | ✅ live-clamped to 15 as digits landed, confirming the "can't be entered in the first place" clamp survived the restyle |
| 9 | `gen9_real.sav` (Skeledirge, dex #911) | Detail hero shows Poké Ball placeholder (dex #911 > pokesprite's #905 ceiling - expected, not a bug) → changed Species Skeledirge→Quaxwell via the restyled Picker dialog → Save Changes → real FileSaver dialog → saved (status line correctly appended the species-changed legality note) → button dirty→clean | ✅ pass |
| 9 | (same) | Pulled the exported file, read back via a throwaway PKHeX.Core harness (deleted after use, per existing project convention - see `OnDeviceEvFix` above) | `Species=913 (Quaxwell)`, `Level=100`, **`Stat_Level=100`** (not the species-before-level EXP-reinterpretation bug fixed in the previous session), `Stat_HPMax=299` (recomputed for the new species, not stale) - confirms the two bugs the previous session fixed are still fixed after this session's restyle |

No regressions found in species/move editing, IV/EV editing (both the
Gen3+ 0-31/0-252 and the Gen1/2 0-15/0-65535 paths), or the save/export
round trip. The dirty/clean Save button - the one new behavior this session
added - works as specified on every generation tested.

## Read-only legality badge, wired to PKHeX.Core's LegalityAnalysis (2026-07-21)

Added a read-only legality result banner to `PokemonDetailPage`, filling in
the `LegalityBadge` concept the design bundle left unwired (see the previous
session's "Scope decision" note - this was explicitly deferred out of the
reskin pass, then requested as its own task). Uses PKHeX.Core's own
`LegalityAnalysis(pk)` directly - no reimplementation of any legality logic,
and no auto-fix anywhere (matches the existing species/move-edit warning's
"applied as-is" stance).

- **Styles**: `LegalityBanner{Pass,Fail}Style` / `LegalityBadge{Pass,Fail}Style`
  / `LegalityBadgeLabel{Pass,Fail}Style` / `LegalityMessage{Pass,Fail}Style`
  added to `Styles.xaml`, reusing the existing `StatusPassBg/Fg` and
  `StatusFailBg/Fg` color tokens (defined in Task 1's `Colors.xaml` port but
  unused until now).
- **`PokemonDetailPage.RefreshLegality(PKM p)`** constructs a fresh
  `LegalityAnalysis`, swaps the banner/badge/message `Style`s based on
  `la.Valid` (pass=green/"LEGAL", fail=red/"ILLEGAL"), and sets the message
  text to `la.Report()` - PKHeX.Core's own human-readable report string
  (`"Legal!"` when valid; the same itemized invalid-check list PKHeX desktop
  shows, when not). Called from `LoadPokemonCore` (every load) and again
  after a successful save (species/move/stat edits change the result) -
  **not** live per-keystroke, matching the existing hero/title refresh
  cadence and keeping `LegalityAnalysis` (non-trivial cost - full encounter
  matching) off the hot path of every field edit.

### On-device verification (the task's specific ask: confirm it flags issues *introduced by* the species/move editor)

Driven on `PkhexMobile_Emulator`, screenshots in `verify/UIReskin/screenshots/`
(32-38):

1. **Gen9, `gen9_real.sav`, Skeledirge (untouched)**: banner loads green
   "LEGAL" / "Legal!" - confirms no false positives on a legitimately-obtained
   real save mon.
2. **Same mon, edited Species Skeledirge→Quaxwell via the restyled species
   Picker, saved**: banner turned red "ILLEGAL" immediately after the save
   completed, with a detailed itemized report - `Invalid Move 1-4: Invalid
   Move` (Torch Song/Shadow Ball/Snarl/Hex aren't Quaxwell's moves),
   `Unable to match an encounter from origin game`, `Ability is not valid
   for species/form`, plus a dozen `Unexpected Technical Record Learned
   flag` lines. Exactly the kind of issue-flagging the task asked to
   confirm - a species/move edit's illegality is caught and explained, not
   silently accepted.
3. **Before the save**, the banner correctly stayed on the stale "LEGAL"
   result while the Species picker showed the new selection - confirms the
   snapshot-on-load/save cadence (not live-per-keystroke) behaves as
   designed, not as a bug.
4. **Gen1, `gen1_real.sav`, MEW**: banner loads red "ILLEGAL" / "Invalid:
   Unable to match an encounter from origin game" - correct (this is a
   hand-crafted test-save Mew, not a legitimately-obtained one) and, more
   importantly, confirms `LegalityAnalysis` runs without crashing on Gen1's
   substantially different parse path (VC-transfer treatment, GameBoy parse
   format) - not just the Gen9 path exercised in steps 1-3.

No regressions to any Task 1 functionality; this task only added the banner
and its refresh calls.

## Design-system consistency pass: box view + edit-form details (2026-07-21)

Audited the box view and detail-screen edit forms against the party-list/
detail-screen visual language established in the earlier reskin session,
looking specifically for places the two diverged rather than adding new
surface area (per instruction: match the established language, preserve all
existing functionality exactly, no box editing). On-device audit (real
`gen9_real.sav`, 30-slot box) found two concrete gaps, both fixed:

1. **`BoxListPage`'s row template was missing the held-item indicator.**
   `PartyListPage`'s row shows `Lv {level}` plus a second line - item name's
   first word or muted "No item" - directly under it; `BoxListPage`'s row
   only showed the level, despite both templates binding the same
   `PartyEntryDisplay` (which already exposes `ItemFirstWord`/`HasItem`/
   `HasNoItem` from the Task 1 reskin). Copied the exact same second-line
   markup into `BoxListPage`'s `DataTemplate` so a box mon's held item is
   visible in the list, not just after opening the detail screen.
2. **`BoxListPage`'s box-selector `Picker` had no label caption above it.**
   Every field in `PokemonDetailPage`'s edit form has an uppercase
   `LabelCaptionStyle` label above it ("NICKNAME", "LEVEL", "SPECIES", ...);
   the box-view's `Picker` was the one remaining input in the app without
   one. Added a "BOX" caption and changed the Picker's placeholder text to
   "Select box" (was "Box", which read oddly once a redundant caption sat
   right above it).
3. **The read-only Details (Nature/Ability) list didn't match the design's
   `DataRow` component spec.** `DataRow.jsx` specifies right-aligned
   semibold values and a `1px solid var(--border-subtle)` bottom divider
   between rows (`divider=true` by default). The existing `StatsList`
   `DataTemplate` had left-aligned plain-weight values and no divider at
   all. Rebuilt the template to match: label left (tertiary caption),
   value right-aligned (`ManropeSemiBold`), `BoxView` divider
   (`BorderSubtle`, 1px) under each row. This is a `CollectionView`
   `DataTemplate`, so the last row also gets a divider (the design shows
   `divider={false}` on a section's final row) rather than engineering an
   "is-last-item" binding for what is currently always exactly two rows
   (Nature, Ability) - a deliberate, low-value tradeoff, not an oversight.

No `.cs` files were touched for this task - every change is XAML template
markup, reusing bindings/properties (`ItemFirstWord`, `HasItem`,
`HasNoItem`, `BorderSubtle`, `LabelCaptionStyle`, `MonoMutedStyle`) that
already existed from Tasks 1 and 2. Re-verified on-device (screenshots
43-44 in `verify/UIReskin/screenshots/`) against `gen9_real.sav`'s 30-slot
box: item indicators now show correctly per-row, box caption renders,
Nature/Ability rows show right-aligned values with dividers. Also
re-confirmed (during the same pass, screenshots 41-42) that the box→detail
navigation path - sprite, held-item row, legality badge, hidden Save
button - already worked correctly end-to-end from Tasks 1/2, with no
regressions from this pass's template edits.

## Roadmap / not yet started (as of 2026-07-21, post-legality-badge)

- **Box (PC) editing.** Still read-only via the structural null-`parentSave`
  guard; no box→party moves or slot swaps.
- **Form/Nature/Ability editing.** Still not user-editable (Nature/Ability
  are PID-derived on several gens; Form has no per-species form list yet).
- **Gen9 species sprite gap.** pokesprite (the vendored sprite source) has
  no Scarlet/Violet/Legends Z-A icons (dex #906+) as of this vendoring pass;
  those species show the placeholder glyph until a source with Gen9
  coverage is vendored.
- **Item icon gap.** ~65% of PKHeX item IDs (mostly TM/TR and minor items)
  have no matching pokesprite icon and show the placeholder.

## Box/party move + swap, drag-and-drop + tap-to-select (2026-07-21)

Enables moving Pokemon between box slots and party slots, in both
directions, plus box-to-box moves - the first item on the previous
session's roadmap. Two interaction methods, both wired into `BoxListPage`
(chosen over `PartyListPage` as the host, since a move inherently needs
box context - see "Scope decision" below): drag-and-drop and
tap-to-select-then-tap-destination, gated behind a new "Move mode" switch
so the pre-existing tap-to-view-detail browse behavior stays exactly as it
was when the switch is off.

### The write-path core: `PokemonSlotMover.cs`, proven before any UI touched it

Per an advisor consult before writing UI code, the data-integrity-sensitive
part (`PkhexMobile/PokemonSlotMover.cs`, a new file) was built and proven
against real saves with `verify/BoxPartyMove/Program.cs` *first*, so the
part with real corruption risk was already green before the grid UI
existed. `SlotLocation` (also new) is a small tagged union (`IsParty` +
either a party index or a box/slot pair) - deliberately a single type so
neither the mover nor the UI can accidentally confuse a box index for a
party index or vice versa (the exact hazard the pre-existing box read-only
guard in `BoxListPage`/`PokemonDetailPage` was already written to avoid -
see "PC box viewing, read-only" above).

`PokemonSlotMover.MoveOrSwap(sav, from, to)` guards, all deliberate and
tested:

1. **Both endpoints are read into locals before any write.** A validation
   failure (bad index, invalid empty-party-target) throws before anything
   is mutated - no half-applied edit.
2. **Party's no-gaps invariant is enforced, not assumed from the UI.** The
   only valid *empty* party target is exactly index `PartyCount` (the
   slot immediately after the current party) - checked inside the mover
   itself. A move that vacates a party slot (moving a mon out to a box)
   closes the gap via the existing `SaveFile.DeletePartySlot`, which
   shifts everyone after that slot down by one - verified against a
   *middle* slot removal (not just the trailing slot) to actually prove
   the shift-down, not just a lucky no-op case.
3. **Destination is written before source is cleared.** For a plain move
   into an empty slot, if anything were to throw between the two steps
   (very unlikely - these are synchronous in-memory byte writes with no
   I/O), the worst case is a duplicate, never a silent loss. A **swap**
   (destination occupied) has no such risk at all either way - both slots
   stay occupied throughout, so there's never a transient empty/hole
   state on either side, regardless of the party/box combination.
4. **`ResetPartyStats()` is called explicitly and unconditionally on the
   box-origin side of any move/swap that lands in a party slot** - never
   left to `SaveFile.SetPartyValues`'s own `!PartyStatsPresent` auto-gate.
   This mirrors the exact fix already applied in
   `PokemonDetailPage.OnSaveChangesClicked` for the same class of bug
   (see "Species + move editing" above), and the task's own working
   theory turned out to be gen-dependent: **empirically confirmed via
   the harness's `Stat_HPMax` dump** that a fresh `GetBoxSlotAtIndex` read
   has `Stat_HPMax == 0` for Gen1 and Gen5 (matching the "box format
   doesn't carry stat-block bytes" theory) but **`Stat_HPMax == 12`,
   `PartyStatsPresent == true` for Gen9** (Scarlet's Sprigatito, box slot
   0) - i.e. the theory does *not* hold for every generation, and relying
   on the library's automatic gate instead of an explicit unconditional
   call would have silently reintroduced the stale-stat-block bug for
   Gen9 box→party moves specifically. Only fires box→party
   (`!origin.IsParty && destination.IsParty`); a party→party reorder or
   box→box move never calls it, since doing so unconditionally would
   wrongly full-heal and clear status on a mon that already has valid
   live battle state.
5. **`EntityImportSettings.None` on every write**, matching the precedent
   already set by `DeletePartySlot`'s own internal shifting calls - a
   same-save relocation shouldn't re-trigger "as if traded" handler
   conditioning, Pokedex updates, or record-acquired bookkeeping.

### `verify/BoxPartyMove/Program.cs`

Links `PokemonSlotMover.cs` directly via `<Compile Include>` (the actual
production file, not a re-implementation that could drift), against real
Gen1 (`POKEMON RED-0.sav`), Gen5 (`Pokemon Black Version.sav`), and Gen9
(`pkmnscarlet_100\main`) saves. Covers every case the task called out:
same-slot no-op (party and box), party→box move (vacating a *middle*
slot, proving shift-down), box→party move (into the append slot), swaps
specifically with destination occupied (party↔box and box↔box), and
"last party member out" (see below). Field-for-field preservation is
checked via an anonymous-type snapshot (species, PID, nickname, OT name,
TID16, level, all 4 moves, all 6 IVs, all 6 EVs) compared before/after
each operation - not just "a mon exists at the destination."

Two design choices worth flagging for future harness authors:

- **Box↔box swap and box→party move deliberately don't search the save
  for a pre-existing second occupied box slot.** The real saves vary
  wildly in box fullness (Gen1's `POKEMON RED-0.sav` has 235/240 box
  slots occupied; Gen5's `Pokemon Black Version.sav` has almost nothing
  stored outside the party) - a test that depends on "find another
  occupied slot lying around" is a property of the specific save file,
  not of `PokemonSlotMover`. Both tests self-seed a known, distinct
  second slot via a move the harness itself performs first (e.g. moving
  a party member into an empty box slot), so they're reliable regardless
  of how full a given save's boxes are.
- **"Last party member out" is deliberately allowed, not blocked** -
  `PokemonSlotMover` has no PartyCount>0 guard, matching PKHeX.Core
  itself (no such guard exists in `SaveFile`) and this project's own
  save inventory, which already treats `PartyCount==0` as a legitimate
  parsed state (`SAV3RSBox`, a GC box-storage-only file). The harness
  drains the party to 0 and confirms `Write()` + `SaveUtil.GetSaveFile`
  reload still succeeds. Gen1's real save doesn't have enough spare empty
  box slots to fully drain a 6-member party (only ~5 empty slots existed
  even before the harness's own earlier moves ate into them) - this is
  correctly reported as a save-capacity note, not a failure, with the
  *partially*-drained state's round-trip still verified instead.

All three gens' full case list: `=== ALL CASES PASS ===`. Originals
confirmed byte-for-byte untouched on disk after each run.

### The grid UI: `BoxListPage`, `SlotCellDisplay`

`BoxListPage` (previously a simple filtered `CollectionView` list, see
"PC box viewing, read-only" above) is rewritten around two
`GridItemsLayout` `CollectionView`s sharing one new display type,
`SlotCellDisplay` (`Location`, `Source` PKM-or-null, `IsSelected`) -
rebuilt and reassigned to `ItemsSource` after every change (matches the
existing list-rebuild pattern already used by `LoadParty`/the old
`LoadBox`, not a bound view-model with `INotifyPropertyChanged`):

- **Party grid** (`Span="6"`) is always visible at the top of the page -
  both ends of a party↔box move need to be on screen simultaneously for
  drag-and-drop to be possible at all. Shows every populated slot plus
  *exactly one* trailing empty cell when `PartyCount < 6` (never more) -
  the single valid "append" target `PokemonSlotMover` accepts; rendering
  further empty cells would offer drop targets the mover correctly
  rejects as gap-creating, a confusing dead end rather than a real
  option.
- **Box grid** (`Span="5"`, scrollable) below a "BOX" picker card, same
  as before - every slot rendered, empty or not, since boxes already
  tolerate holes.
- **Move mode switch.** Off (default): tapping a populated cell opens
  `PokemonDetailPage`, exactly as before - box cells still navigate with
  `PendingPokemonSave` left null (read-only guard, unchanged), party
  cells now navigate read-write (`PendingPokemonSave = currentSave`,
  `PendingPokemonIndex = cell.Location.Slot`), matching
  `PartyListPage`'s existing behavior - this is a small **added**
  capability (editing a party mon was previously only reachable via
  `PartyListPage`, not `BoxListPage`), not a regression, and doesn't
  weaken the read-only guarantee for box entries at all. On: tap
  repurposed for tap-to-select-then-tap-destination (see below); drag is
  always active regardless of the switch, since it's a distinct gesture
  from a plain tap and doesn't collide with browse-mode taps.
- **Tap-to-select fallback.** First tap on a populated cell selects it
  (highlighted via a `DataTrigger` on `IsSelected`, new `SlotCellStyle` in
  `Styles.xaml`) and shows "Selected. Tap a destination slot." Second tap
  on a *different* cell calls `PokemonSlotMover.MoveOrSwap` and refreshes
  both grids; second tap on the *same* cell cancels the selection
  ("Selection cleared.") - verified on-device, both paths. Selection is a
  `SlotLocation?` field, not tied to what's currently rendered, so
  switching boxes via the picker mid-selection is deliberately allowed -
  this is exactly how a box↔box move *between two different boxes* works
  via the fallback: select in box A, switch the picker to box B, tap a
  destination there. Verified on-device: selected a Gen9 box A slot,
  switched the picker through Box 2 (open, tap "Box 2" in the dialog)
  then again to Box 9 (a mis-tap landed there instead of the intended
  Cancel button, but the pending selection survived it and completed the
  move against Box 9 without any corruption or crash when the eventual
  destination tap landed - a useful accidental stress test of exactly
  this state-independent-of-rendering design).
- **Drag-and-drop.** `DragGestureRecognizer`/`DropGestureRecognizer` on
  every cell (`CanDrag="True"` on populated cells only -
  `e.Cancel = true` for empty ones in `DragStarting`); `Drop` calls the
  same `PerformMove` the tap fallback uses, so there is exactly one code
  path that ever calls `PokemonSlotMover`. Compiles and runs without
  crashing on-device.
- **Explicit "Export Save" action, added mid-task after an advisor
  review flagged the gap:** a move/swap only mutates the in-memory
  `SaveFile` - unlike `PokemonDetailPage`'s edit flow, there's no
  "Save Changes" button naturally in the way on this page. Added a
  dirty/clean-tracked "Export Save" button (disabled until a move
  actually succeeds, mirroring the existing Save-button pattern from the
  reskin session) that calls `currentSave.Write()` +
  `FileSaver.Default.SaveAsync`, identical in shape to
  `PokemonDetailPage.OnSaveChangesClicked`'s export flow.

### Two real bugs caught during on-device testing, both fixed before the pass was considered done

1. **The gap above** (no export path existed at all from the grid UI) -
   caught by re-reading the advisor's pre-UI-work notes rather than by
   accidentally discovering it on-device; fixed by adding the Export Save
   button described above, then verified end-to-end (see below).
2. **ADB coordinate-scaling mistakes, not app bugs** - repeatedly tapped
   using un-scaled "displayed" screenshot coordinates instead of the
   1.2× raw-device coordinates the environment requires (see WAKEUP.md's
   existing UI-automation note) or stale bounds after the page's layout
   shifted once the Export Save button/status text changed height. Every
   apparent "the tap did nothing" turned out to be exactly this, not the
   app failing to respond - confirmed each time via `uiautomator dump`
   before concluding a real defect. Logged here as a caution for
   whoever automates this page next, not as an app-side finding.

### On-device verification (`PkhexMobile_Emulator`, real FileSaver, real touch input)

Driven against the real `gen9_real.sav` (screenshots in
`verify/OnDeviceBoxPartyMove/screenshots/`):

| Interaction | What was exercised | Result |
|---|---|---|
| Tap-to-select | Party slot↔box slot swap (destination occupied) | ✅ status "Moved.", PartyCount unchanged (6/6), both grids updated in place |
| Tap-to-select | Box↔box swap within one box (destination occupied) | ✅ both slots exchanged contents |
| Tap-to-select | Cross-box move: select in Box 1, switch picker (through an unintended extra box due to a mis-tap), complete on a different box | ✅ selection survived every picker change; move completed correctly with no corruption |
| Tap-to-select | Same-slot tap cancels a pending selection | ✅ "Selection cleared.", highlight removed |
| Move mode OFF | Tap a party cell opens the detail screen (pre-existing behavior) | ✅ correct mon (post-swap Sprigatito, Lv1) shown, Legal badge, all fields correct |
| Drag-and-drop | Attempted via `adb shell input swipe` (two attempts, 1.2s and 2.5s duration) | Not triggered - ADB's synthetic swipe doesn't replicate the native long-press-drag gesture MAUI's Android renderer listens for (an environment/automation limitation flagged before attempting, and confirmed empirically) - no crash either time. **Library/code-level only for drag specifically**; every other interaction above (including the harder "primary path" per-task-instructions, tap-to-select) was verified with real on-device touch input. |
| Export | "Export Save" button disabled→enabled after a move, real FileSaver document-picker dialog, saved to a distinct filename | ✅ "Saved to: /storage/emulated/0/Documents/boxparty_...sav" |
| Export round-trip | Pulled the exported file, read back via a throwaway PKHeX.Core harness (deleted after use, per this project's established convention) | `PartyCount=6`, `Party[0]: Species=906 (Sprigatito) Level=1 Stat_HPMax=12`, `Box0[0]: Species=911 (Skeledirge) Level=100` - exactly the swapped values, confirming the on-device move round-tripped through the real export |
| Gen1 regression | `gen1_real.sav` (Trainer ASH) - box/party grid renders correctly (sprites, 6/6 party, 20/20 box 1), one tap-to-select swap performed | ✅ no crash, status "Moved." |

### Scope decision: hosted on `BoxListPage`, `PartyListPage` untouched

`PartyListPage` (no box context, simple list) is left exactly as it was -
a move fundamentally needs both a party view and a box view on screen
(or reachable) at once, and `BoxListPage` already had box context plus,
after this pass, a party section added to it. Building the move UI as a
new page-with-both-grids inside `BoxListPage` rather than modifying
`PartyListPage` at all keeps the pure-browse party list's existing,
already-verified behavior completely unchanged and lowers regression
risk - `PartyListPage.xaml[.cs]` has a zero-line diff for this task.

### Explicitly out of scope / deferred

- Drag-and-drop between two *different* boxes (would require both boxes
  visible simultaneously - not attempted; the tap-to-select fallback
  already covers this case, see above).
- No legality re-validation triggered by a move specifically (the
  existing legality badge only refreshes on `PokemonDetailPage`
  load/save, unchanged - a moved mon's legality can be seen by opening
  its detail screen with Move mode off).
- Gen1 was smoke-tested (one swap, on-device, no crash) rather than
  given the full multi-case sweep Gen9 got on-device - the *library*
  harness already proves Gen1's write-path correctness exhaustively
  (same test suite, same file, see above); the on-device pass exists to
  catch UI-specific issues, and none were found.
