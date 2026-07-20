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
