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
  hyphens). pokesprite does not cover Generation 9 species (Scarlet/Violet/
  Legends Z-A, dex #906+); as of this pass those species had no file and fell
  back to the placeholder glyph - confirmed on-device against a real Gen9
  save (Skeledirge, dex #911). **Superseded 2026-07-22 (commit `200afee`):**
  the #906-1025 gap was filled from PokeAPI/sprites (front battle sprites,
  not pokesprite's box/menu-icon style - a mild, accepted style difference).
  Species coverage is now complete #1-1025, regular and shiny. See "Gen 9
  species sprite gap" further down for the corrected roadmap entry.
- **Held-item icons**: vendored the same way into `Resources/Images/items/`
  as `item_{id:D4}.png`, but **keyed by PKHeX's own item ID space** (0-2683,
  read from `vendor/PKHeX.Core/Resources/text/items/text_Items_en.txt`,
  where item ID = line number − 1), matched to pokesprite's files by
  normalized English name - pokesprite's own internal item numbering does
  **not** correspond to PKHeX item IDs, so a numeric-ID-to-numeric-ID mapping
  would have silently produced wrong icons for most items. 933 of 2683 items
  matched (~35%) as of this pass; unmatched IDs (mostly TM/TR items, stored
  in pokesprite by type/number rather than a readable name, plus assorted
  items with no pokesprite icon at all) fall back to the placeholder.
  **Superseded**: `db049cc` vendored 43 more name-matched icons (933→976),
  then `a7aacb4` added 173 type-colored TM/TR icons derived from each TM's
  taught move (+173, not name-matching). Current total: 1149 of 2684 item
  IDs have an icon (~43%).
- **Fallback strategy**: rather than maintaining a hardcoded valid-ID list
  that would go stale as sprite coverage changes, every sprite slot layers a
  static `sprite_placeholder.svg` (a neutral Poké Ball outline glyph,
  matching the design's `SpriteSlot` fallback spec) *behind* an `Image` bound
  to the computed filename. A MAUI `Image` that fails to resolve its `Source`
  simply renders nothing, so the placeholder shows through underneath with
  no broken-image glyph and no crash - confirmed on-device for both a
  missing species (Gen9 Skeledirge, since fixed - see above) and the
  remaining ~57% of unmatched item IDs (still current).
- `SpriteHelper.cs` (new) - `SpeciesSpriteFile(species, shiny)` /
  `ItemSpriteFile(itemId)`, the filename-computation half of the above.
  `PartyEntryDisplay` (used by both `PartyListPage` and `BoxListPage`'s
  shared row template) grew computed properties (`SpriteFile`, `IsShiny`,
  `ItemSpriteFile`, `HasItem`/`HasNoItem`, `ItemName`/`ItemFirstWord`) so the
  existing `record` continues to flow straight into data-bound
  `CollectionView` templates without new per-page glue.
  `PokemonDetailPage.RefreshHero`/`PopulateHeldItem` do the same for the
  single-item detail-screen hero and Main-card held-item row (a **new
  read-only display** at the time - held item was previously not shown
  anywhere in the app at all. **Superseded**: a later session made held
  item genuinely editable via `pk.ApplyHeldItem` - see "Navigation wiring +
  consolidated on-device pass" further down for the on-device verification).
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
| 9 | `gen9_real.sav` (Skeledirge, dex #911) | Detail hero shows Poké Ball placeholder (dex #911 > pokesprite's #905 ceiling - expected, not a bug **at the time**; the #906-1025 gap was later filled by commit `200afee`, so this species now renders a real sprite) → changed Species Skeledirge→Quaxwell via the restyled Picker dialog → Save Changes → real FileSaver dialog → saved (status line correctly appended the species-changed legality note) → button dirty→clean | ✅ pass |
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

> **All four items below were resolved in later sessions** (box move/swap,
> Form/Nature/Ability editing, the Gen 9 sprite gap, and further item-icon
> coverage - see "Sprites and held-item icons" above and the "Box/party move
> + swap" and "Form + Nature + Ability editing" sections further down for
> the corrected, current state). Left in place as a dated snapshot rather
> than rewritten, since the sections that superseded each item are already
> in this file.

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
| Drag-and-drop | Attempted via `adb shell input swipe` (two attempts, 1.2s and 2.5s duration) *and* `adb shell input draganddrop` (two more attempts, 1s and 3s hold - this command exists on this AVD's API level and is purpose-built to simulate a hold-then-move gesture, unlike `swipe`) | Not triggered by any of the four attempts - ADB's synthetic touch input doesn't replicate whatever native gesture-recognition MAUI's Android `DragGestureRecognizer` renderer actually listens for on this emulator (an environment/automation limitation flagged before attempting, and confirmed empirically across two different `adb input` subcommands) - no crash on any attempt. **Library/code-level only for drag specifically**; every other interaction above (including the harder "primary path" per-task-instructions, tap-to-select) was verified with real on-device touch input. |
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

## Form + Nature + Ability editing (2026-07-22)

Extended `PokemonDetailPage`'s editor to cover **Form**, **Ability**, and
**Nature** - previously read-only (`StatsList`, now removed entirely; the
`StatRow` record it used is gone from `PartyEntryDisplay.cs`). Same shape
as the species/move pattern: a `Picker` per field, applied via PKHeX.Core's
own setter, behind the existing legality warning (text widened to name all
five now-editable fields). The task's real substance wasn't wiring three
pickers - it was determining, **empirically per generation**, whether each
field's setter actually does anything, since "PID-derived on early gens"
folklore turned out to be only half right.

### What's actually editable per generation (confirmed via `verify/FormNatureAbilityEdit/Program.cs` against real saves, not assumed)

| Field | Gen1/2 | Gen3 | Gen4 | Gen5+ |
|---|---|---|---|---|
| Form | no-op (sentinel 0, except Unown's IV/PID side-effect - not exposed) | no-op | **real**, round-trips, recomputes stats | real |
| Ability | no concept (sentinel -1) | real to *display*, setter no-op | **real**, round-trips | real |
| Nature | no concept (sentinel Hardy) | real to *display* (PID%25), setter no-op | no-op (contrary to "PID-derived Gen3-5" folklore, Gen4's Nature setter specifically is *still* a no-op even though Ability/Form aren't) | **real**, round-trips (confirmed independently stored as of Gen5, not just Gen6+ as PROGRESS.md's own task brief assumed) |

Non-editable cases are **disabled, not hidden** - `FormPicker`/`AbilityPicker`/
`NaturePicker` still show the correct current value (or an explicit
"N/A"/no-concept title for Gen1/2), matching the existing Gen1/2 IV-field-
disabling precedent, so the UI never silently accepts an edit that has no
effect. Each picker's caption label explains *why* it's disabled inline
(e.g. "Nature (PID-derived before Gen 5 - not directly editable, see
PROGRESS.md)").

Two things deliberately **not** wired up despite being technically
possible:
- **Gen3/4's `SetPIDNature` workaround.** PKHeX.Core can force a Nature by
  rerolling the PID until it matches, but this also **changes the mon's
  shininess** as a side effect - too surprising to hide behind a plain
  Nature picker, so Gen3/4 Nature stays a genuine no-op rather than a
  working-but-shiny-breaking edit.
- **Gen1-3's Form setter for Unown.** Only ever does anything via IV/PID
  randomization (Unown's letter is DV-derived), not a plain stored byte -
  same "too surprising for a plain picker" call as above.

### Genuine finding: Gen8+'s Mint mechanic needed explicit syncing, not just `pk.Nature = x`

`PKM.LoadStats`/`ResetPartyStats` reads **`StatAlignment`** (the "Mint"
byte, Gen8+ only) for the stat-boost calculation, not `Nature` directly -
first pass of the harness set only `Nature` and the Gen9 stat-block
assertion failed outright (stats didn't move at all after a Nature edit).
Fixed: `OnSaveChangesClicked` also sets `pk.StatAlignment = newNature`
whenever `pk.Format >= 8`, so a user picking "Adamant" gets both the
displayed Nature *and* the stat block reflecting Adamant, not a
legal-but-confusing original-Nature/Mint mismatch. This is the same class
of "setter exists but something else needs to move too" bug already found
twice before (party stat staleness, species-before-level ordering) - caught
here by the harness asserting the *stat value*, not just the field getter,
exactly the discipline the task's brief asked for.

### Stats-affected gating

`statsAffected` (drives the `ResetPartyStats()` call) now also includes
`formChanged` and `natureChanged` - both feed `PersonalInfo.GetFormEntry`/
the stat-boost calculation respectively, confirmed via the harness's
before/after `Stat_ATK`/`Stat_DEF`/`Stat_SPA` dumps. `abilityChanged` is
deliberately **excluded** - Ability affects battle mechanics, not the
stored stat block, so including it would trigger a pointless (if harmless)
recompute.

### Verification

`verify/FormNatureAbilityEdit/Program.cs` (real saves, real
`Write()`/`SaveUtil.GetSaveFile` round-trip, original file confirmed
untouched on disk afterward, same discipline as `EditRoundTrip`/
`SpeciesMoveEdit`): Gen1 (no-op confirmation), Gen5 (Nature+Ability
in-place edit, and a separate Form edit via a species switch to Giratina
Altered/Origin to get a real form-level base-stat difference to detect),
Gen9 (same two cases, Skeledirge -> Zygarde 50%/Complete for the form
case) - all pass, stat-block deltas match the expected `PersonalInfo`
tables exactly. Bonus (not required by the task, done because the real
save files were already on hand): Gen3 (Emerald, confirms the no-op table
row) and Gen4 (HeartGold, confirms the "Nature no-op but Ability/Form
real" split specifically, which is the one row of the table most likely to
be assumed wrong by pattern-matching against Gen3).

On-device (`PkhexMobile_Emulator`, real touch input, screenshots in
`verify/OnDeviceFormNatureAbility/screenshots/`, 74 frames): Gen9
(`gen9_real.sav`) - Ability and Nature edited via the restyled Pickers,
saved through the real FileSaver dialog, legality badge reflects the
change; a separate pass changed Species to Squawkabilly and picked a
different Form (color variant), saved, confirmed. Gen5 (`gen5_real.sav`)
- Nature edited via the Picker, saved through the real FileSaver dialog,
confirmed saved. Gen3 was loaded and its detail screen viewed to confirm
the disabled-picker treatment renders correctly (greyed out, current
value still shown) - no edit attempted there since Gen3 has nothing
editable in this task per the table above.

### Explicitly out of scope / deferred

- No legality re-validation beyond the existing read-only badge (unchanged
  cadence - refreshes on load/save, same as species/move edits).
- Gen6/7 were not separately round-trip-tested (no real Gen6/7 save file
  in the project's inventory with these specific fields probed) - the
  Gen5/Gen9 pass brackets them and the underlying PKHeX.Core storage
  format for Nature/Ability/Form is unchanged across Gen5-9 (all
  independently-stored `Data[]` fields from Gen5/Gen4 onward respectively,
  per the source reads in `PokemonDetailPage.xaml.cs`'s own comments) -
  a reasonable inference, not a tested fact for Gen6/7 specifically.

## Move type indicators on the detail screen (2026-07-22)

Each of the four move rows now leads with a solid, type-coloured chip
(the design bundle's `TypeBadge` in its "solid" variant, at the fixed width
`DetailScreen.jsx`'s own `MoveRow` uses so the four rows align in a column).
Display only - nothing here reads or writes the underlying `PKM` beyond
`MoveInfo.GetType`.

**Provenance note:** the code for this landed as part of commit `3b75c12`
("WIP: rescue in-flight agent work interrupted by a session usage-limit
cutoff"). The agent that wrote it was cut off *mid on-device verification*
and never wrote this section. This pass reviewed the code, added the one
assertion it was missing (below), and completed the interrupted on-device
verification - it did not rewrite the feature.

### Three correctness traps, all handled and all pinned by the harness

1. **Context-aware typing, not a hardcoded context.** The type comes from
   `MoveInfo.GetType(move, pk.Context)`. Several moves changed type between
   generations, so a Gen1 Pokémon's Gust/Bite/Karate Chop/Sand Attack must
   read *Normal* (their Gen1 typing) and pre-Gen6 Charm/Moonlight/Sweet Kiss
   likewise. A hardcoded modern context would silently mislabel every one of
   them.
2. **The empty slot is detected from the move ID, never the type byte.**
   `MoveInfo.GetType(0, ctx)` returns `0` in *every* context - indistinguishable
   from a genuine Normal-type move - so branching on the type byte would paint
   a cleared slot with a bogus "NORMAL" chip. Move ID `0` gets a neutral
   placeholder chip (`—` on `SurfaceSunken`) rather than being hidden, so
   clearing a move doesn't shift the row's layout.
3. **PKHeX's type-byte order is not the design bundle's type order.**
   `GameInfo.Strings.Types` starts Normal/Fighting/Flying/Poison; the bundle's
   `TypeBadge.jsx` list starts Normal/Fire/Water/Electric. Indexing one by the
   other would mis-colour almost every chip (Fighting would render as Fire).
   The app's `TypeColorKeys` array is therefore written in PKHeX's own order,
   with hardcoded English keys so the colour lookup is independent of UI
   language (a localized `Types[9]` of "Feu" would never resolve `TypeFire`).

### The assertion the interrupted agent's harness was missing

`verify/MoveTypes/Program.cs` sections 3-5 prove the type *name* is right, but
the chip's *colour* comes from the app's own `TypeColorKeys[type]` array and
nothing asserted the two agree. If they ever diverged, a chip would read
"FIRE" while painting Steel's colour and every existing check would still
pass. Added as section 6: `TypeColorKeys[i] == Types[i]` name-for-name for
i=0..17, plus a check that the single unpalletted byte is `Types[18]`
"Stellar". Kept as a literal copy of the app array because that array lives in
a `net10.0-android` MAUI assembly this `net10.0` harness cannot reference - so
section 6 asserts the *contract* (PKHeX's type-byte ordering), not the copy.

All 6 sections pass, including the pre-existing finding worth restating:
across every `EntityContext`'s full move-type table the maximum type byte is
17 (Fairy), so `Types[18]` "Stellar" - the Gen9 Terastal type, which has no
design token - is **unreachable by any move in any generation**. The app's
`Normal` fallback for out-of-palette bytes is therefore dead code by
construction, not a latent wrong-colour bug.

### Dropdown rows carry the type too

MAUI renders `Picker` dropdown rows as plain strings and cannot host a styled
view, so the chip can only cover the *collapsed* state - while the open
dropdown (choosing among ~900 moves) is exactly where the type matters most.
The type is appended to the item text as a `"(Type)"` suffix there instead.
Purely cosmetic: `MoveIdFor` resolves a selection through `moveIds` by index,
never by parsing the text.

### On-device verification (`PkhexMobile_Emulator`, real touch input)

Screenshots in `verify/OnDeviceDetailExpansion/screenshots/`. This is the half
the interrupted agent never reached.

- **Gen1** (`gen1_real.sav`, party Blastoise): chips render ICE / WATER /
  NORMAL / GROUND against the correct palette colours. Then the actual point
  of trap 1 - changed move 3 to **Bite** via the Picker and the chip rendered
  **NORMAL**, not Dark. Bite is Normal-type only in Gen1; a hardcoded modern
  context would have shown DARK here. The dropdown itself also listed
  "Bite (Normal)" and "Karate Chop (Normal)", same Gen1 typing.
- **Gen1 empty slot**: cleared move 4 to `(None)` - the chip became the
  neutral grey `—` placeholder with the row layout unchanged, confirming trap
  2 on real input rather than only in the harness.
- **Gen9** (`gen9_real.sav`, party Skeledirge): FIRE / GHOST / DARK / GHOST,
  again matching the palette, on a save whose party+boxes span all 18 type
  bytes per the harness.
- Save button dirty/clean tracking still behaves correctly around the chip
  refresh: disabled on a fresh load, enabled the instant a move Picker
  changed. (This is why the chip refresh and `MarkDirty` are combined in one
  `OnMoveSelectionChanged` handler rather than stacked as two subscriptions -
  the chip must repaint during `LoadPokemon`'s programmatic population, while
  `MarkDirty` must stay suppressed by `isLoading` for exactly that window.)

## EV/IV caps taken from the library, and the 510 budget surfaced (2026-07-22)

`CAPABILITY-AUDIT.md` §4.1 (gap #4) flagged the app's hardcoded stat bounds
as a genuine correctness defect *and* a de-branching win. Both halves shipped,
but **not the way the audit's own recommendation phrased it** - see the
contradiction below, which the harness settles empirically.

### The de-branch and the defect it fixed

`PokemonDetailPage.LoadPokemonCore` previously computed:

```
isGen12 = p.Generation is 1 or 2;
ivMax   = isGen12 ? 15 : 31;
evMax   = isGen12 ? 65535 : 252;
```

now simply `ivMax = p.MaxIV; evMax = p.MaxEV;` - both abstract on `PKM.cs:298-299`
and overridden per format. Verified per generation in
`verify/DetailFieldEdits/Program.cs` (part A1) against real saves:

| Format | `MaxIV` | `MaxEV` | old hardcoded `evMax` | |
|---|---|---|---|---|
| Gen1/2 (`GBPKM.cs:18-19`) | 15 | 65535 | 65535 | ✅ unchanged |
| Gen3 (`G3PKM.cs:23-24`) | 31 | **255** | 252 | ❌ was wrong |
| Gen4 (`G4PKM.cs:24-25`) | 31 | **255** | 252 | ❌ was wrong |
| Gen5 (`PK5.cs:306-307`) | 31 | **255** | 252 | ❌ was wrong |
| Gen6+ (`G6PKM`/`G8PKM`/`PK9`) | 31 | 252 | 252 | ✅ unchanged |

The Gen3/4/5 rows are a real data-loss bug, not a cosmetic cap: `LoadPokemon`
populates each field via `Math.Clamp(p.EV_X, 0, evMax)`, so a save that
genuinely held 253-255 was **displayed clamped to 252 and then written back at
the clamped value** - the same failure shape as the already-fixed Gen1/2
`byte.TryParse("65535")` bug, an over-narrow cap destroying real data.
`verify/DetailFieldEdits` part A2 confirms 255 round-trips through
`Write()`/`GetSaveFile` on real Gen3, Gen4 and Gen5 saves *and* that it
actually reaches the stat block (+57/+57/+63 Atk), not merely that the getter
echoes it back.

### Where the audit's own recommendation was wrong, and why the app didn't follow it

§4.1 recommends replacing the ternaries with "`pk.MaxIV` / `pk.GetMaximumEV(i)`"
and says this fixes both the Gen3-5 under-cap *and* adds 510-budget
enforcement. Those two goals are in direct conflict, because
`CommonEdits.cs:329-337` reads:

```
if (pk.Format < 3) return EffortValues.Max12;
return Math.Clamp(510 - (EVTotal - thisEV), 0, EffortValues.Max252);
```

`GetMaximumEV` **clamps to 252 regardless of format**. Adopting it as the field
cap would therefore have re-imposed exactly the 252 ceiling this change exists
to remove. Confirmed empirically rather than by reading alone - part A5, on a
real Gen3 save with all six EVs zeroed (i.e. maximum possible headroom):
`GetMaximumEV(ATK) = 252` while `pk.MaxEV = 255`.

So the two concerns were split:

- **Field cap → `pk.MaxEV`/`pk.MaxIV`** (the format's true storage limit). Live
  clamp, load-time backstop and save-time validation all now quote it.
- **510 budget → an advisory caption, not a clamp.** A new `EvTotalLabel`
  shows "EV total: n / 510" live from what is currently typed (not from
  `pk.EVTotal`, which is last-saved state and would lag every keystroke), and
  recolours to `StatusWarnFg` with an "over budget" note past 510.

Making the budget advisory rather than enforced is a deliberate consistency
call, not laziness: this app's stated stance is that **edits are applied
exactly as chosen and legality is reported, never enforced** - that is what the
permanent warning banner and the read-only `LegalityAnalysis` badge are for,
and the species/move/nature editors already build illegal mons freely. An
editor that refuses an over-budget keystroke would be the one place enforcing
a legality rule. The caption is hidden entirely on Gen1/2, which have no 510
concept (the library short-circuits to `Max12` for `Format < 3`); hiding it
there also sidesteps a double-count, since Gen1/2's single shared "Special"
stat-exp value is surfaced as two mirrored SpA/SpD fields.

### What deliberately did NOT change

`isGen12` survives - it no longer carries any numeric meaning, only the Gen1/2
**structural** facts: HP IV derived from the other four DVs' low bits, and
SpA/SpD sharing one "Special" value. §4.1's own scope limit is right that these
have no generic library handle (`ISeparateIVs`, the obvious candidate, is
implemented only by `CK3`/`XK3`, not `GBPKM`), so that logic stays hand-rolled
on purpose. Part A4 of the harness pins it: on real Gen1 *and* Gen2 saves, HP
DV still derives from the other four (`IV_HP=11` for the probe values), SpD
still moves with SpA, and the library still clamps an over-range DV to 15.
Part A3 separately re-confirms 16-bit stat exp (65535 on Gen1, 54321 on Gen2)
still round-trips with the stat block tracking it - the behaviour the
"Gen1/2 EV save-blocking bug fixed" session established.

The range captions are now interpolated from `ivMax`/`evMax` rather than
written as literals, so a caption can no longer drift from the cap actually
enforced (the old literal "EVs (0-252 each)" was itself wrong on Gen3/4/5).

## Navigation wiring + consolidated on-device pass (2026-07-22)

The previous session ended with `TrainerInfoPage` and `PokemonTransferPage`
fully coded but unreachable from the running app - deliberately left
disconnected so four concurrent agents wouldn't collide on `MainPage.xaml`/
`AppShell.xaml.cs`. This pass did the deferred integration (sequentially, no
subagents, per this project's own §8 cost lessons) and then ran the
consolidated on-device verification pass the handoff called for, since this
project has a documented history of bugs that only surface on-device.

### Navigation wiring

- `AppShell.xaml.cs` registers `TrainerInfoPage` and `PokemonTransferPage`
  as routes.
- `MainPage` gains a "View Trainer" button, visible whenever a save is
  loaded (not gated on `PartyCount`/`HasBox` like the other two buttons,
  since `TrainerInfoPage` itself already handles the "this save has no
  trainer block" case via `TrainerFieldSupport`).
- `PokemonDetailPage` gains an "Import / Export" button that forwards the
  current `pk`/`parentSave`/`partyIndex` through the exact same
  `NavigationState` hand-off the page itself was reached with - including a
  `null` `parentSave` for a box-opened mon, which `PokemonTransferPage.
  ConfigureImportAvailability` already handles by hiding the import cards
  and leaving export available. No new `NavigationState` fields needed.

### On-device verification: three flows, all real device I/O, no bugs found

Driven on `PkhexMobile_Emulator`, real `FileSaver`/`FilePicker`, real touch
input, real save files (`gen5_real.sav`, plus a real `.pk3` pulled from
`172 - PICHU - 0377DA486937.pk3` in the project's save inventory).
Screenshots in `verify/OnDeviceNavAudit/screenshots/`.

1. **Trainer screen (newly reachable, never verified before this pass).**
   Loaded `gen5_real.sav`, tapped View Trainer - real OT/TID/SID/Money/
   PlayTime/Gender/Language/Pokédex data rendered. Edited OT name; the
   7-character `MaxLength` for SAV5BW live-truncated the typed text before
   it could exceed the cap (confirms the "can't be entered in the first
   place" clamp pattern holds here too, not just on the IV/EV fields it was
   built for). Saved through the real FileSaver dialog, pulled the exported
   file, re-parsed it with a throwaway PKHeX.Core harness (deleted after
   use, per this project's established convention): OT read back exactly as
   truncated, every other field byte-for-byte unchanged.
2. **`.pk` / Showdown transfer (brand new page, never on-device verified -
   the previous session wrote it but it was unreachable).** From a party
   Pokémon's new Import/Export button: exported a `.pk5` (Serperior),
   pulled it and re-parsed it with PKHeX.Core - matched the on-screen
   Showdown text exactly (species, nature, ability, IVs, EVs, moves).
   Imported the real `.pk3` (a Gen3 Pichu, OT "Erick R", pulled from this
   project's own save inventory) through the real file picker - **the app
   correctly converted PK3→PK5** and reported "IMPORTED — CONVERTED".
   Applied a hand-typed Showdown set (Pikachu, Level 25, Adamant, Thunderbolt)
   via "Apply set" - "SET APPLIED". Saved through the real FileSaver dialog;
   the exported save, re-parsed, showed the imported Pichu in the target
   party slot with the exact species/level/OT/PID/IVs from the source file.
   This is the first real cross-generation entity conversion (3→5) this
   project has exercised through the actual on-device file-picker path
   rather than a library harness.
3. **Box↔party move/swap (re-verified; the tap-to-select path already had
   on-device coverage from the 2026-07-21 session, see "Box/party move +
   swap" above).** Enabled Move mode, selected a party Pokémon, tapped an
   empty box slot - "Moved.", party count updated 6/6→5/6, box occupancy
   updated 1→2 Pokémon, both grids reflected the change immediately.
4. **Form/Nature/Ability editing (re-verified; had on-device coverage from
   the 2026-07-22 Form+Nature+Ability session, see above).** Changed a
   Gen5 Volcarona's Nature (Gentle→Jolly) via the restyled Picker, saved,
   pulled the file - Nature persisted and the stat block was recalculated
   for the new nature (confirms `natureChanged` still correctly drives
   `ResetPartyStats()`, unregressed by this session's changes).

### Bonus finding: held item editing had already shipped, undocumented and unverified

While auditing `PokemonDetailPage.xaml.cs` for stale-doc purposes (see
below), found that held-item editing - listed in the previous session's
`WAKEUP.md` as "not started" alongside ball and friendship - is actually
**fully implemented**: `PopulateHeldItem` builds a per-format item picker
(`GameInfo.Strings.GetItemStrings(p.Context)`, correctly keyed to each
generation's own item ID space), disabled with an explanatory caption on
Gen1 (held item is the catch-rate byte there, a hard no-op), and
`OnSaveChangesClicked` applies it via `pk.ApplyHeldItem` (not a raw
`HeldItem =`, so item-specific side effects run). This had never been
on-device verified. Done here: changed a Gen5 Volcarona's held item to
Master Ball via the picker, saved through the real FileSaver dialog, pulled
the file and re-parsed it - `HeldItem=1` (Master Ball in Gen5's item space)
persisted correctly. Ball and friendship remain genuinely unimplemented -
only the held-item claim in the prior handoff was wrong.

### Documentation corrections (the previous session's WAKEUP.md item 3)

- **Gen 9 species sprites**: corrected the "Sprites and held-item icons"
  section and the roadmap entry - the #906-1025 gap this file previously
  described as open was filled by commit `200afee` (front battle sprites
  from PokeAPI, not pokesprite's box-icon style - an accepted style
  mismatch). Species coverage is now #1-1025 complete, regular and shiny.
- **Item icon coverage**: corrected the "933 of 2683 (~35%)" figure - two
  later commits (`db049cc`, +43; `a7aacb4`, +173 TM/TR icons via
  move-type mapping) brought current coverage to 1149 of 2684 IDs (~43%),
  counted directly from `Resources/Images/items/` rather than re-quoted
  from memory.
- **"Not started / deferred" list**: held-item editing removed (see
  "Bonus finding" above); ball and friendship confirmed still absent by
  grepping `PokemonDetailPage.xaml.cs` for any trace of them (none found).

## Pokedex browse/detail screens + reference data assembly (2026-07-23)

Two parallel tracks: Track A (Haiku subagent, data assembly only) and Track B
(this session, main thread, Pokedex UI). Both landed on `master`; Track A's
commit unintentionally landed directly on `master` rather than its isolated
worktree branch (a tooling gap, not a content problem - see "Worktree
isolation gap" below), but its diff was clean and additive-only, so no
conflict resulted with Track B's concurrent edits.

### Track A: Pokedex-manifest.json + item-info.md (commit `f23d225`)

- `Pokedex-manifest.json` (322K): all 1025 species (#1-1025), PokeAPI-sourced
  name/generation/types/abilities, plus sprite paths pointing at the
  already-vendored `Resources/Images/species/spr_NNNN.png` files (no
  re-fetching - species sprite coverage was already complete per this file's
  "UI reskin" section above).
- `item-info.md` (160K): 1482 PokeAPI items matched to PKHeX item IDs by
  normalized name, each with effect text and sprite path. 236 new icons
  vendored into `Resources/Images/items/item_NNNN.png` (PKHeX ID space,
  matching the existing convention - not PokeAPI's numbering), bringing item
  icon coverage from 1149/2684 to 1385/2684.
- Neither manifest is currently wired into the shipped UI - Track B built the
  Pokedex screens directly against PKHeX.Core's own data (below) rather than
  these PokeAPI-sourced files, since PKHeX.Core already had everything needed
  and is the more authoritative/faster source for an app already built on it.
  The manifests remain available on disk for a future feature that
  specifically needs PokeAPI's framing (e.g. flavor text, in-game location
  data) that PKHeX.Core doesn't carry.

### Track B: PokedexListPage + PokedexDetailPage (commit `954d49a`)

Reference-only Pokedex, reachable from a new "Pokedex" button on `MainPage`
(no save file required, unlike every other nav button there) - `PokedexService.cs`
is the data layer, sourcing everything from PKHeX.Core directly:

- **Species/types/abilities/base stats**: `PersonalTable.SV.GetFormEntry` -
  chosen because SV's table covers the full National Dex `#1-1025` regardless
  of catchability in Scarlet/Violet specifically (format conversion needs
  stat data for every species), so it's one complete source instead of
  per-generation branching.
- **Forms (Mega Evolution, Gigantamax, regional forms)**: `FormConverter.GetFormList`
  is context-gated to match real game rules - Mega Evolution doesn't exist in
  Scarlet/Violet, Gigantamax only exists in Sword/Shield, so no single
  `EntityContext` returns the union of every form a species has ever had.
  Queried across one context per era (`Gen6`/`Gen7`/`Gen7b`/`Gen8`/`Gen8a`/
  `Gen8b`/`Gen9`/`Gen9a`) and de-duplicated by name. Confirmed on-device:
  Charizard's Forms card lists Mega X/Mega Y/Gmax; a single-context query
  (originally just `Gen9`) showed none of them, since Gen9's context has
  `IsMegaContext == false` and isn't `>= 8` in the branches that gate Gmax -
  this was caught and fixed during on-device verification, not assumed correct
  from a build pass.
- **Evolution chain**: `EvolutionTree.Evolves9` (Scarlet/Violet's tree, the
  most current), root found via `EvolutionNetwork.GetBaseSpeciesForm`, then
  walked forward via `IEvolutionForward.GetEvolutions`/`GetForward` to capture
  every branch (this is what makes Eevee's 8-way branch and per-form
  branches like regional evolution lines work without special-casing them).
  Each edge's `EvolutionMethod` is rendered into a human-readable caption by a
  hand-written `EvolutionType` switch in `PokedexService.DescribeMethod` (no
  such string-formatter exists in PKHeX.Core to reuse). Item-based methods
  (`UseItem`/`TradeHeldItem`/etc.) treat `EvolutionMethod.Argument` as a
  PKHeX item ID and cross-reference it via the existing `SpriteHelper
  .ItemSpriteFile`/`PkmDisplayHelper.GetItemName` - confirmed correct
  on-device against Eevee's full 8-evolution chain (Leaf/Ice/Thunder/Water/
  Fire Stone icons and names all matched real game data, alongside the
  friendship/affection-based branches which correctly show no item icon).
- **Navigation**: species ID passed as a plain query-string int
  (`PokedexDetailPage?speciesId=6`), not the `NavigationState` static
  hand-off the rest of the app uses for `SaveFile`/`PKM` objects - safe here
  because CLAUDE.md's Shell `InvalidCastException` trap only applies to
  non-`IConvertible` types; `ushort`/`int` are `IConvertible` and survive
  Shell's query-dictionary coercion.

Verified on-device (`PkhexMobile_Emulator`, no loaded save required): list
screen shows "1025 of 1025 species", search-by-name and search-by-dex-number
both filter correctly (`Eevee` -> `1 of 1025 species` -> #0133), generation
filter picker populates Gen 1-9; Charizard's detail screen matches real
Bulbapedia data exactly (Fire/Flying, Blaze/Solar Power hidden, base stats
78/84/78/109/85/100 = 534, Charmander -> Charmeleon Lv.16 -> Charizard Lv.36).

### Worktree isolation gap (process note, not a code bug)

Track A was spawned with `isolation: "worktree"` specifically so its
data-fetch-and-commit cycle couldn't collide with Track B's concurrent edits
on `master` (per `CLAUDE.md` §8's shared-file collision warning). The
worktree was created correctly (`git worktree list` showed it, tracking
branch `worktree-agent-*`), but the agent's actual `git commit` ran against
whichever branch the *main* repo directory had checked out (`master`) rather
than the worktree's own branch - the worktree branch never advanced past its
creation point. Confirmed via `git reflog show <worktree-branch>` (one entry:
"Created from origin/master") versus `git reflog show master` (Track A's
commit appears as master's very next entry). No harm resulted only because
Track A's diff was clean and additive (manifests + new icon files, zero
overlap with Track B's files) - a future session relying on worktree
isolation for a track whose diff *does* overlap with concurrent work should
verify with `git log <worktree-branch>` that commits actually landed there
before assuming isolation held.

The empty/stale worktree and its unused branch (zero unique commits, a
strict ancestor of `master`) were removed after verification
(`git worktree remove` + manual directory cleanup, since its `.git` pointer
file referenced a path from the agent's own execution environment that
didn't resolve on this host - `git worktree prune` handled the stale
administrative state).

## 3D model viewer, branch `3d-models-experimental` (2026-07-23)

`Model3DViewerPage`, reachable from the Pokedex detail screen's new "View in
3D" button (`PokedexDetailPage.OnView3DClicked`). Renders a bundled `.glb`
via the vendored `model-viewer` web component inside a `HybridWebView`;
falls back to the existing 2D sprite when no model is bundled for that
species. Committed `068c14b` on `3d-models-experimental`, **not merged to
master** - this branch is intentionally experimental per the original task
scope and stays separate until real models exist and it's been reviewed.

### Why HybridWebView, not a plain WebView

A plain `WebView` pointed at a `file://` URL into `FileSystem.CacheDirectory`
fails on-device with `net::ERR_ACCESS_DENIED` - Android's WebView renderer
runs in a sandboxed process that cannot read the app's own private storage
via `file://`, even though the main app process can read/write there freely.
`HybridWebView`'s virtual host (`HybridRoot="model3d"`, serving the
`MauiAsset`-bundled `Resources/Raw/model3d/**` files over an internal
virtual origin `https://0.0.0.1/`) sidesteps this entirely.

### Six attempts to find a working per-species parameterization

The interesting part of this feature wasn't rendering a `.glb` (that worked
first try, confirmed with a hardcoded `<model-viewer src>` and a public-domain
test duck model) - it was passing *which* model to load into a page that's
otherwise identical for every species. In order:

1. **JS↔C# bridge, `EvaluateJavaScriptAsync` from `WebViewInitialized`** -
   threw `InvalidOperationException: PlatformView cannot be null here`; that
   event fires before the handler's native view is assigned.
2. **Same call, delayed** - past the crash, but the DOM wasn't ready yet:
   `Uncaught TypeError: Cannot read properties of null (reading
   'setAttribute')` (`document.getElementById('mv')` was null).
3. **JS-side `window.HybridWebView.sendRawMessage('ready')` handshake**,
   gating the C# call on `RawMessageReceived` - the "ready" message never
   arrived. A direct C#→JS probe (`typeof window.HybridWebView`) round-tripped
   as empty with no exception and no console error. The bridge itself does
   not work reliably on this MAUI/WebView version, independent of DOM timing.
   A separate hardcoded-`src` control test on the same build proved the
   render pipeline (HybridWebView virtual host → model-viewer → WebGL) works
   correctly - isolating the fault to the bridge specifically.
4. **`DefaultFile = "index.html?glb=models/{id}.glb"`**, read via
   `location.search` - `net::ERR_INVALID_RESPONSE at https://0.0.0.1/`.
5. **`DefaultFile = "index.html#glb=models/{id}.glb"`**, read via
   `location.hash`, on the theory that URL fragments are never sent as part
   of any resource request (true, per the URL spec) - produced the
   **identical** `net::ERR_INVALID_RESPONSE`. This was the surprising result:
   it proved `HybridWebView.DefaultFile` is matched against the bundled
   asset set as a **literal filename string**, not parsed as a URL at all -
   neither query nor fragment syntax reaches a resolver that would strip it,
   because there is no such parsing step.
6. **`DefaultFile` swapped to a second real, distinct, literal filename**
   (`s6.html`, a file identical to the working hardcoded-src control but
   bundled under a different name) - confirmed on-device: renders correctly,
   and re-navigates correctly on a second `DefaultFile` assignment. This is
   the mechanism the shipped code uses: one real `model_{speciesId}.html`
   file per bundled model (see `Resources/Raw/model3d/README.md` for the
   generation convention), gated by `FileSystem.OpenAppPackageFileAsync`
   existence-checking that exact file before navigating.

No `.glb` files are bundled as of this commit (the test duck and its
throwaway test page were deleted before committing) - every species
currently shows the 2D-sprite fallback. Verified on-device that this fallback
path is clean (no Chromium error page) for a species with no bundled model,
which is every species right now - this matters because the query-string/
fragment attempts, if left wired to a nonexistent per-species page, would
have surfaced exactly the same `net::ERR_INVALID_RESPONSE` Chrome error page
to a real user the moment any model was added, rather than the intended
fallback.

### Not done in this pass

- **Track C-2 (fetching real `.glb` models from `github.com/Pokemon-3D-api/
  assets`) was not run.** Given no models exist yet, generating per-species
  `model_{id}.html` wrapper pages ahead of time was judged premature
  (would-be dead code pointing at nonexistent files) - the convention and
  generation template are documented in the README instead, to be produced
  alongside each real model as it's added.
- Not merged to `master`.
- Alternate forms (Mega/regional/Gmax) all resolve to the base species' model
  slot - no separate per-form model lookup yet.

## Gender editing, manual PP/PP-Ups editing, and a read-only "Computed" card (2026-07-23, `master`)

Closes four of `CAPABILITY-GAPS.md`'s Tier A items (#3, #4, #5, #6/#7 combined)
plus the Tier A hardening item (#8). All on `PokemonDetailPage`.

- **Gender editing.** `pk.Gender` (Male/Female/Genderless picker). **SPLIT**,
  identical shape to Ball/Friendship: Gen1/2 derive it from `IV_ATK` vs. the
  species' gender-ratio threshold (`GBPKM.cs:106-119`, hard no-op setter);
  Gen3 derives it from PID vs. the same threshold (`G3PKM.cs:37`, also a hard
  no-op); Gen4+ is real independent storage (`PK4.cs:170`, `Data[0x40]`).
  Disabled-but-show-the-truth on Gen1-3, same as every other SPLIT field on
  this page. Unconstrained by the species' actual gender ratio - applied
  exactly as chosen, matching this app's Nature/Ability/Form stance; a
  mismatch is the read-only legality badge's job to report, not a picker
  guard's job to prevent.
- **Manual PP / PP-Ups editing.** `pk.MoveN_PP`/`MoveN_PPUps`, uniform across
  every generation (no SPLIT needed - `PKM.cs:133-140` are plain abstract
  members with no per-format no-op anywhere). Previously the app only ever
  called `SetMoves`, which auto-maxes PP; this exposes direct control. A
  small PP/PP-Ups row was added under each of the 4 move pickers. Selecting a
  *different* move in a slot auto-resets that slot's PP to the new move's max
  and PP-Ups to 0 (mirrors real in-game "learn a new move" behavior) - the
  user can still override immediately after. Live-clamped to 0-255 (PP) and
  0-3 (PP-Ups) - 0-255 because every generation stores PP as a single byte
  (e.g. `PK9.cs:340` `Data[0x7A] = (byte)value`), so that's a real structural
  ceiling, not an arbitrary UI choice; 0-3 because PP Up's real max stack
  effect is 3 in every generation. Deliberately **not** clamped to the
  selected move's own max-at-current-PP-Ups: unlike IV/EV, PP has no
  legality-reporting mechanism in this app to fall back on, and an unusually
  high PP is harmless (nothing derives from it), so it's treated like the
  EV-over-budget case - allowed, not blocked.
- **Read-only "Computed" card**, three gap-list items folded into one card
  since they share one read cadence (load + after save, never per-keystroke):
  - **Battle stats** via `PKM.GetStats(PersonalInfo)` - computes fresh from
    the PKM's own *current* IV/EV/level/nature fields. Deliberately not
    `pk.Stat_HPMax` etc. (the stored party stat block): that's last-saved
    state and would lag every unsaved edit, and per CLAUDE.md's own trap
    table, a box mon's stored block can be stale/zeroed anyway. `GetStats`
    sidesteps both problems - same call for a party mon and a box mon.
  - **Species type chip(s)** from `PersonalTable.SV`, keyed by species/form -
    reuses the exact technique the already-shipped Pokedex feature uses, and
    for a real reason found while building this: `pk.PersonalInfo.Type1`/
    `Type2` is a **per-save-format raw byte**, not safe to use directly.
    `PersonalInfo1.Type1` (Gen1) is `Data[0x06]`, the game's own internal
    type-ID table, which has unused gaps (an "unused Bird type" slot) and
    does **not** match PKHeX's modern 0-17 `MoveType` order - confirmed by a
    real `ArgumentOutOfRangeException` crash in `verify/GenderPPEdit` on the
    first run, indexing `GameInfo.Strings.Types[t1]` with a raw Gen1 byte.
    Fixed by routing through `PersonalTable.SV` instead, same source/tradeoff
    the Pokedex feature already made (shows the *current-games* type, e.g.
    Fairy for Clefairy, not necessarily the mon's origin-generation type).
  - **Hidden Power type**, `HiddenPower.GetType(IVs, context)`, **Gen3+ only**.
    The raw 0-15 return value needs `+1` to map onto the standard type-byte
    order (it skips Normal, which Hidden Power can never be) - confirmed via
    `ShowdownSet.cs:775`'s own `1 + HiddenPowerType // skip Normal` comment,
    the only place in the vendored library that actually uses this
    conversion. Gen1/2's GB-era path (`HiddenPower.GetTypeGB`) is a
    *different* raw encoding that this same `+1` offset is never verified
    against anywhere in the library (`ShowdownSet` never exercises it, since
    the Showdown format doesn't cover Gen1/2) - rather than assert something
    unverified, Hidden Power is simply not shown before Gen3. Gen1 doesn't
    have the Hidden Power move at all regardless (introduced Gen2).
- **Locked-slot guard in `PokemonSlotMover.MoveOrSwap`** (`CAPABILITY-GAPS.md`
  §1.2 / Part 2 #8, a hardening item, not a feature). Any box endpoint
  (source or destination) is checked against `SaveFile.IsBoxSlotOverwriteProtected`
  before any write happens, throwing `InvalidOperationException` instead of
  silently overwriting or vacating a game-reserved slot (battle team, GO
  transporter, etc.) - `BoxManagement`'s bulk sort/clear ops already guarded
  the equivalent bulk case; this per-slot path had none until now. `BoxListPage
  .PerformMove` already wraps every `MoveOrSwap` call in a generic
  `catch (Exception ex)` that reports `ex.Message` and continues, so this
  needed no UI changes to surface cleanly.

**Verification:** `verify/GenderPPEdit` (new) - Gender editable/no-op split
and PP/PP-Ups round-trip against 5 real saves (Gen1/2/3/5/9), plus a
plausibility check on the three Computed-card values per generation (this is
where the Type1 crash above was actually caught, before it ever reached the
app). `verify/BoxPartyMove` gained a locked-slot case: none of this project's
real save inventory happens to have an actually-locked box slot in its
as-downloaded state (confirmed by scanning all three), so the test
synthetically locks Team 0 via `SAV9SV`'s own public `TeamIndexes` API (a
real, supported save-editing surface, not a hack - `TeamSlots[0]` must first
be pointed at a real linear box index, since `GetBoxSlotFlags` resolves a
team via `TeamSlots.IndexOf(index)` and a lock flag alone with no team slots
assigned resolves to nothing, confirmed empirically on the first attempt)
to genuinely exercise `IsBoxSlotOverwriteProtected` returning true, rather
than only asserting the guard code exists. On-device (`gen9_real.sav`,
Skeledirge): Gender changed Male->Female, Move1 PP changed 16->10, both
round-tripped through the real `FileSaver` write path and were confirmed in
the exported file; Computed card showed Fire/Ghost typing, battle stats
(367/196/247/281/201/212), and "Hidden Power: Dark", with no layout issues
scrolling through the new UI.

## Pokedex "Where to Find" + Mega trigger items + Dex Entries + shiny toggle (2026-07-23, `master`)

Direct user request mid-session: "make the pokedex extremely detailed" - where to catch a
species, in which games, at what locations, plus what Mega Evolution needs. Four additions to
`PokedexDetailPage`, all reference-only (no loaded save required, same as the rest of the
Pokedex feature).

### Where to Find (species -> games/locations/methods)

`PokedexService.GetEncounterLocations` - built on `EncounterMovesetGenerator.GenerateEncounters`,
a **public** PKHeX.Core API (the same one backing PKHeX's own "Encounter Database" tool) - no
network dependency, no hand-inverted per-generation area tables. Proven in a throwaway-then-kept
harness (`verify/EncounterLocationData`) before any UI was written, per this project's own
methodology, against 5 species chosen for risk coverage: Charizard (Megas, multi-gen history),
Eevee (branching evolutions), Mewtwo (legendary, static-only), Piplup (starter, gift-only),
Pikachu (highest-volume wild encounter species - a stress test).

Two real layers of duplication were found and fixed while building the harness, both empirical,
neither obvious in advance:
- `GameUtil.GetVersionsWithinRange` special-cases "HOME era" contexts (Gen8+): it returns each
  such format's full ID range unrestricted rather than limiting to that context's own generation,
  so scanning Gen8/Gen8a/Gen8b/Gen9 separately re-discovers the same encounters ~4x over. Fixed:
  scan the whole HOME-connected block once, via Gen9 alone.
- Independent of that, an early-generation Egg/Static encounter is still a legitimate explanation
  for a Pokemon currently sitting in a *later* generation's format (bred in Ruby, transferred
  forward) - so the same fact gets rediscovered once per later generation-context scanned even
  after the first fix. Fixed: dedupe **globally** across every context scanned, not per-context.

A third, purely-a-bug (not a duplication) issue: location names must be resolved using the
**encounter's own** generation, not the scanning context's - resolving a Gen3 Ruby/Sapphire egg's
location ID with generation=9 (the context being scanned) produced a nonsense modern Paldea
location name for an old game. Caught in the harness before it ever reached production code.

Categorized into Wild / Static-Gift / Egg / Trade / Raid / Mystery-Gift-Event (via `IEncounterEgg`,
`MysteryGift`, and `enc.Name` pattern matching - PKHeX.Core doesn't expose this categorization on
the returned object itself, though it uses an equivalent internal `EncounterTypeGroup` for its own
dispatch). Mystery Gift events are individually real but far too numerous/granular to list
one-by-one (Pikachu alone has ~48 distinct past distribution events across 13 games) - collapsed
to one summary line. Each other category is capped at 12 rows with a "+N more" note for on-screen
readability (Pikachu has 161 wild-encounter rows alone across 9 generations of games) -
`EncounterCategoryDisplay` in `PokedexDetailDisplay.cs`.

**Encounter rate/probability is explicitly not shown, and this is stated in the UI itself** -
PKHeX.Core's encounter data has no such field at all (the Gen9 slot struct, for example, is fully
accounted for by species/form/gender/level-range/time/weather - no room left for a rate byte).
This is legality-grade "is this possible" data, not a game-guide "how common" database; the
caption under the card's title says so rather than omitting the limitation silently.

**Real on-device bug found and fixed**: the first version of this feature called
`GetEncounterLocations` synchronously from `LoadSpecies` (the same method that populates every
other card on this page). For Charizard this caused an actual **ANR** ("PkhexMobile isn't
responding") that was still unresponsive after 45+ seconds of tapping "Wait." Every other lookup
on this page (base stats, abilities, forms, evolution chain) is an instant pre-computed table
read; encounter scanning is not - it walks up to 9 `EntityContext`s and, for a heavily-encountered
species, produces 1000+ raw records before deduplication. Fixed by moving the call to
`Task.Run` (`LoadEncounterLocationsAsync`), with a loading-state label shown until it resolves and
a stale-species guard (captures `speciesId` before the `await`, skips repainting if the user
navigated away mid-scan). Confirmed on-device afterward: Charizard and Pikachu (the two most
expensive cases tested) both load their "Where to Find" card without any ANR, each producing the
expected row counts and "+N more" captions.

### Mega Evolution trigger items

`PokedexService.GetMegaTriggerItem` - `ItemStorage9ZA.GetExpectedMegaStoneOrPrimalOrb(species,
form)`, a complete real PKHeX.Core table (not hand-built), confirmed against Charizard X/Y,
Mewtwo X/Y, and Groudon/Kyogre Primal Reversion in `verify/EncounterLocationData`. Wired into the
existing Forms card's `FormEntryDisplay.Note` (previously a generic "Mega Evolution (battle-only
form)" caption) - now reads "Mega Evolution - requires holding Charizardite X, used in battle"
when the lookup resolves, falling back to the old generic caption if it doesn't (form-index
alignment between `GetFormNames`' de-duplicated union list and the real form byte
`ItemStorage9ZA` expects isn't guaranteed for every species, only confirmed correct for the common
Gen6-origin Mega case - see the code comment on `FormEntryDisplay.MegaItemId`).

### Dex Entries (per-game flavor text)

Unlike everything else in the Pokedex feature, this is **not** sourced from PKHeX.Core - a
legality/save-editing library has no reason to carry hand-written game flavor text. Fetched ahead
of time from PokeAPI by a Haiku subagent (dispatched in an isolated worktree per this project's
collision-avoidance convention) covering all 1025 species across 34 game versions, bundled as
`Resources/Raw/dexentries/flavortext.json` (~2.6MB MauiAsset, picked up automatically by the
existing recursive `Resources\Raw\**` glob - no `.csproj` change needed). Loaded lazily/async and
cached (`PokedexFlavorTextService`) so it never blocks page render.

The prior session's item-data subagent had made a real mistake worth avoiding here (pulled item
descriptions in French by not filtering the `language` field) - this agent's brief explicitly
called that out, and its output was independently spot-checked (not just trusted) before merging:
confirmed English-only, 1025/1025 species present, whitespace-normalized, no French markers found
in a text scan. **Sibling versions with verbatim-identical flavor text are grouped** (e.g. "Black,
Black 2, White, White 2" as one entry) rather than repeating the same paragraph 2-4 times, since
PokeAPI's data frequently has this redundancy) - grouped by exact text match, order preserved from
first occurrence.

Agent's commit landed correctly on its own worktree branch this time (`git log` confirmed before
merging) - contrast with the earlier Track A incident this session where a worktree agent's commit
landed on `master` instead. Pushed to `origin/pokedex-flavortext-data` for durability, then merged
into `master` (clean merge, no conflicts), worktree removed and pruned.

### Shiny toggle

Sprite assets were already 100% complete for all 1025 species (both regular and shiny) before this
session even started - confirmed by direct file count, no fetch needed. Just a `Button` on the
hero card flipping `SpriteHelper.SpeciesSpriteFile(id, shiny: true/false)`, mirroring the
shiny-star treatment already shipped on `PokemonDetailPage` - here it's a user choice (reference
browsing has no real mon whose `IsShiny` to read), not a computed fact from a loaded save.

### Not done in this pass

- Encounter rate/probability - not available in PKHeX.Core at all, explicitly noted in the UI
  rather than network-fetched (PokeAPI's own encounter-rate data is spotty and worst exactly where
  it matters most, modern SV/newer games) or estimated.
- Per-form encounter/Mega data for alternate forms beyond the base species - `Form` is hardcoded
  to 0 for this whole page, same constraint the rest of the Pokedex detail screen already has.

## Characteristic string, `PokemonDetailPage`'s Computed card (2026-07-23, `master`)

Closes `CAPABILITY-GAPS.md` Tier B's "Characteristic string" item - the "It's alert to sounds!"-
style flavor line real games show alongside a Pokemon's stats, one line added to the existing
Computed card (below Hidden Power). Both pieces already exist in PKHeX.Core, nothing hand-built:
`EntityCharacteristic.GetCharacteristic(ec, ivs)` computes the 0-29 index (the same real algorithm
the games use - highest IV among the six, tie broken by `EncryptionConstant % 6` so two mons with
identical IVs don't always show the same line), and `GameInfo.Strings.characteristics` is
PKHeX.Core's own matching text table (`GameStrings.cs`'s `"character"` localization key) - unlike
Dex Entries flavor text, which genuinely isn't in the library at all and had to come from PokeAPI.

**Gen3+ only** - `EncryptionConstant` is a hard `0` on Gen1/2 (`GBPKM.cs:124`, sealed no-op
setter), and Gen1/2 never had a Characteristic mechanic in the real games either, so this is a
structural gate, not a display preference. Verified in `verify/CharacteristicDisplay` that Gen3-5
(`G3PKM.cs:34` aliases `EncryptionConstant` to `PID`, a real working value) still produce a
meaningful, per-mon-varying result despite EC not being independently stored until Gen6 - the gap
list's original "Gen3+" scoping was correct, this isn't a Gen6+-only feature. The harness also
cross-checked that the stat index the algorithm picked is actually tied-for-highest among that
mon's own IVs, for all 5 tested generations (1/2/3/5/9) - not just that a string came back.

Verified on-device against `gen9_real.sav`'s Dondozo: "It's alert to sounds!" rendered correctly
under Hidden Power in the Computed card, no layout issues.

## Pokerus editing, `PokemonDetailPage`'s Main card (2026-07-23, `master`)

Closes `CAPABILITY-GAPS.md` Tier B's Pokerus item - `pk.PokerusStrain`/`pk.PokerusDays`, one
packed byte (upper nibble strain, lower nibble days), identical layout confirmed across
`PK2.cs:83-84`/`PK3.cs:129-130`/`PK9.cs:152-153` in `verify/PokerusEdit`. **Gen1 is a hard no-op**
(`PK1.cs:149-150`, `get => 0; set { }` - RBY has no Pokerus mechanic at all), disabled there with
the reason inline, same precedent as Ball/Friendship/Gender above it on the same card.

**Deliberately not further SPLIT beyond the single Gen1 gate**, despite `Editing/Pokerus.cs`'s own
`IsObtainable` reporting the true in-game mechanic as absent from PB7 (Let's Go), PK9 (Scarlet/
Violet), and PA9 (Legends Z-A) - none of those games' wild encounters/breeding ever produce a
nonzero value, and HOME doesn't transfer it in. The storage bytes are nonetheless real and
independently read/written even on PK9 (confirmed in the harness) - this is a plausibility fact
for the read-only `LegalityAnalysis` badge to flag, not a second storage no-op, so it's left
editable and applied-as-is everywhere Gen2+, consistent with how Nature/Gender/Ability already
work on this page (apply exactly as chosen; legality is reported, not enforced).

Structural hardware range (0-15 each, one nibble) is the live-clamp ceiling, same defensive-clamp
precedent as IV/EV/PP - not the smaller "really obtainable" 0-8 strain subset, which is a
legality-engine concern, not a UI guard.

Verified library-level (`verify/PokerusEdit`, Gen1/2/3/5/9, including round-trip through
`Write()`+reload and a same-generation byte-layout cross-check) and on-device against
`gen9_real.sav`'s Skeledirge: Strain 0->3, Days 0->2, both round-tripped through the real
`FileSaver` write path and confirmed in the exported file.

## Markings editing, new "Markings" card on `PokemonDetailPage` (2026-07-23, `master`)

Closes `CAPABILITY-GAPS.md` Tier B's Markings item - the 6 small shapes (Circle/Triangle/Square/
Heart/Star/Diamond) shown under a Pokemon in-game, used purely for player sorting (no
battle/legality effect). `IAppliedMarkings<bool>`/`IAppliedMarkings<MarkingColor>` from `PKM.cs`,
verified in `verify/MarkingsEdit` against the exact per-generation SPLIT this feature needs:
**Gen1/2 implement neither interface at all** (no marking concept exists, confirmed via
`pk is IAppliedMarkings<T>` returning false, not merely an unstored no-op); **Gen3** implements
`IAppliedMarkings<bool>` with `MarkingCount=4` (Circle/Triangle/Square/Heart only -
`G3PKM.cs:42`); **Gen4-6** implement it with `MarkingCount=6` (+ Star/Diamond - `G4PKM.cs:172`);
**Gen7+** implement `IAppliedMarkings<MarkingColor>` (`None`/`Blue`/`Pink`) with `MarkingCount=6`
(`PK7.cs:427`). Index order (0-5 = Circle/Triangle/Square/Heart/Star/Diamond) is identical
wherever a marking exists.

UI: 6 tappable circular chips. Gen3-6 tap toggles on/off; Gen7+ tap cycles
None -> Blue -> Pink -> None. Chips beyond a generation's real `MarkingCount` are dimmed and
disabled rather than hidden (same precedent as every other per-generation field on this page);
Gen1/2 disables the whole card with an inline explanation, since no marking slots exist there at
all - genuinely nothing to show, not a suppressed no-op.

**Real on-device bug found and fixed, caught only by re-testing after the fact rather than
trusting the first screenshot**: the Heart chip rendered as a filled red circle on a completely
fresh, untouched Gen9 save where the Heart marking's real underlying value was `None` - looking
identical to a genuinely-marked chip. Root cause: **Unicode U+2665 ("BLACK HEART SUIT") has a
colored-emoji presentation on Android that some system fonts render in a fixed red, ignoring the
`Label.TextColor` the app actually set** - unlike Circle/Triangle/Square/Star/Diamond (●▲■★◆),
none of which have an emoji presentation and so were never affected. Confirmed via a from-scratch
re-test (fresh app process, fresh save load, fresh page instance, before any taps) that this
reproduced identically, ruling out stale in-memory state as the cause, and via a standalone
harness confirming the underlying `MarkingColor` really was `None` in both the original file and
a save exported before the fix - i.e. **this was purely a rendering bug, not a data-loss bug**;
every write/round-trip was correct throughout. Fixed by appending U+FE0E (the "text presentation
selector") after the heart glyph, forcing the same plain monochrome rendering the other five
shapes already had. Re-verified on-device: all 6 chips now correctly render as neutral gray when
unmarked.

Also verified the interactive tap-to-cycle behavior on-device (Circle: None -> Blue -> Pink) and
a full save round-trip (`gen9_real.sav`'s Skeledirge, Circle set to Pink, confirmed in the
exported file via a standalone check that also reconfirmed Heart correctly stayed `None`
throughout - proving the fix didn't regress the write path).

**Also fixed in this pass**: two `CS0419` "ambiguous cref" compiler warnings this session had
introduced earlier (in `PokedexService.cs`'s and `PokemonSlotMover.cs`'s XML doc comments,
referencing overloaded PKHeX.Core methods without disambiguating parameter types) - this
project's own bar is 0 warnings, and these had crept in past the pre-existing 7. Fixed by adding
explicit parameter-type lists to the `<see cref>` tags; confirmed the build is back to exactly
the same 7 pre-existing `CS8622` warnings as before this session.

## Combined-field regression test (`verify/CombinedFieldSave`)

Every field `PokemonDetailPage` added this session (Gender, PP/PP-Ups, Pokerus, Markings) had only
been verified **in isolation** (`verify/GenderPPEdit`, `verify/PokerusEdit`, `verify/MarkingsEdit`),
same for the pre-existing stack (`verify/FormNatureAbilityEdit`, `verify/BallFriendshipEdit`). All
of them funnel through the same `OnSaveChangesClicked` -> `SetPartySlotAtIndex(EntityImportSettings
.None)` -> `Write()` path, and this project has one documented precedent (the `CurrentHandler`
"as if traded" bug) of a regression that only shows up when multiple writes land in the same save
call - so isolated-per-field testing was a real gap, not just extra caution.

Built a harness that stacks Gender + Pokerus Strain/Days + Markings (all slots) + PP/PP-Ups +
an IV change (to force `ResetPartyStats`) onto **one mon in one save operation**, replicating the
app's exact field-application order, across Gen1/2/3/5/9. Asserts: every field round-trips through
`Write()`+reload, `Stat_ATK` actually moved off the new IV (proving `ResetPartyStats` engaged
correctly alongside everything else, not stale), `CurrentHandler` is untouched (the
`EntityImportSettings.None` guard still holds with every new field stacked in), `LegalityAnalysis`
recomputes without throwing on a mon carrying every new field at once, and the original file on
disk is byte-for-byte unchanged. All 5 generations pass. No cross-feature interaction bug found -
this was a clean confirmation pass, not a bug hunt that turned something up. Hardcodes local save
paths (like `BallFriendshipEdit` and friends) - excluded from CI.
