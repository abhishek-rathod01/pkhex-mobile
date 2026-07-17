# Gen 9 (Scarlet/Violet) Save-Parsing Verification

## Status: VERIFIED against a REAL save file (2026-07-17)

Originally verified library-generated only (see below); a real Pokémon Scarlet save
(`pkmnscarlet_100/main`, 4436579 bytes, Trainer Player, 6-member competitively-built
party — a Switch save dump, not a flat `.sav`) became available and was confirmed both
via console-level `SaveUtil.GetSaveFile` read and through the actual app UI (file
picker → party list → detail screen) on the PkhexMobile_Emulator AVD.

- Party list showed all 6 members correctly (Skeledirge, Clodsire, Squawkabilly,
  Garganacl, Tinkaton, Dondozo — all Lv.100).
- Detail screen for Skeledirge showed: Species Skeledirge, Nature Timid, Ability Blaze,
  Moves Torch Song/Shadow Ball/Snarl/Hex, IVs all 31, EVs 72/129/45/100/61/103 — all
  matching the console-level read exactly.

A real Legends Z-A save (`pkmnlegendsza_100_21/main`, Trainer Player, 6-member party,
mixed CJK/Latin nicknames) was also confirmed at the console level (`SAV9ZA`
recognized correctly) but not separately driven through the UI.

This closes the "no real Gen9 save file was available" gap below, and is a
particularly important result given the documented Write()/re-detection limitation for
*library-generated* Gen9 saves (see below): a **real, loaded** Gen9 save reads (and,
per Phase 3, edits/exports) correctly through the full app — that limitation only ever
affected freshly-authored blank saves, never real ones.

## Summary (original)

**Status: PASS (primary verification).** Library-generated save data was used —
no real Gen9 `.sav` file was available (confirmed via search, see below), and no
synthetic bytes were hand-written anywhere in this task.

## What was tested

- Searched the entire worktree (`C:\Users\abhis\Desktop\pkhex-mobile-gen9`) for
  any `*.sav` files. None were found. All testing below uses a save object
  constructed purely through PKHeX.Core's own `SAV9SV` blank constructor —
  no hand-crafted byte arrays.
- Read `vendor/PKHeX.Core/Saves/SAV9SV.cs` first to confirm the actual
  constructor signatures before writing any code:
  - `public SAV9SV()` — blank save. Builds `AllBlocks` from
    `BlankBlocks9.GetBlankBlocks()`, sets `SaveRevision` from
    `BlankBlocks9.BlankRevision`, then calls `Initialize()` and `ClearBoxes()`.
  - `public SAV9SV(Memory<byte> data)` — loads from raw encrypted bytes via
    `SwishCrypto.Decrypt`.
  - There is also a private `SAV9SV(IReadOnlyList<SCBlock> blocks)` constructor
    used internally by `CloneInternal()`.
- Created `verify/Gen9/Gen9Verify.csproj` (net10.0 console app) with a
  `ProjectReference` to `../../vendor/PKHeX.Core/PKHeX.Core.csproj`, and
  `verify/Gen9/Program.cs` exercising the proven recipe pattern:
  ```csharp
  var sav = new SAV9SV();
  sav.Version = GameVersion.VL;   // required: IsVersionValid() only accepts SL/VL
  sav.OT = "VERIFY";
  var pk = sav.BlankPKM;
  pk.Species = 25; // Pikachu
  pk.CurrentLevel = 10;
  sav.PartyData = [pk];           // list-setter, sets PartyCount correctly
  ```
- Ran with `dotnet run` from `verify/Gen9/`.

## PRIMARY verification (required) — direct read off the live SAV object

Confirmed working, exact console output:

```
Constructed SAV9SV. SaveRevision=2 (-ID)
Trainer: VERIFY
Version: VL (IsVersionValid=True)
PartyCount: 1
  Species=25 Level=10 Nickname=''
PRIMARY VERIFICATION: PASS
```

All of the following were confirmed correct:
- **Trainer name** (`sav.OT`) — round-tripped through `MyStatus9.OT`.
- **Party count** (`sav.PartyCount`) — correctly reflects the single added
  Pokémon via `PartyInfo.PartyCount`, populated using the `PartyData`
  list-setter (not `SetPartySlotAtIndex`, per the known trap).
- **Species** — Pikachu (25) round-tripped through `PK9`.
- **Level** — `CurrentLevel = 10` round-tripped correctly (internally stored
  as EXP via `Experience.GetEXP`, and read back correctly through
  `CurrentLevel`'s getter).

One Gen9-specific quirk discovered along the way: `SAV9SV.IsVersionValid()`
requires `Version` to be explicitly `GameVersion.SL` or `GameVersion.VL` — the
blank constructor does not set a valid version by default, so `sav.Version`
had to be set explicitly (done here as `GameVersion.VL`) for `IsVersionValid()`
to return `true`. This is analogous to setting `OT`/trainer info — it is not
optional metadata but a real validity gate PKHeX.Core exposes.

Also note: the blank constructor came up as `SaveRevision=2` ("-ID", i.e.
Indigo Disk / the full DLC2 revision) by default — `BlankBlocks9.BlankRevision`
in this vendored PKHeX.Core snapshot defaults to the latest known revision,
not the vanilla base-game revision. This affects which blocks/keys
(`KBlueberryPoints`, `BlueberryQuestRecord9`, `BlueberryClubRoom9`, etc.) exist
in `AllBlocks`.

## BONUS verification (optional, not required for pass/fail)

Attempted two optional secondary checks, per instructions ("your specific
outcome for the bonus check may well differ [from Gen4], just document what
actually happens"). Neither affects the PRIMARY verification above, which
never calls `Write()` or re-parses anything — it reads data directly off the
live `SAV9SV`/`PK9` objects.

### Bonus 1: `Write()` + `SaveUtil.GetSaveFile()`

Unlike Gen4 (which threw `ArgumentOutOfRangeException` inside checksum code
because the blank constructor didn't allocate the full backing buffer
`Write()` expected), **Gen9's `Write()` completed successfully with no
exception**, producing 4,424,032 bytes (`0x438160`). This makes sense given
SV's architecture: `SAV9SV.SetChecksums()` is a documented no-op (`// None!`),
and `GetFinalData()` simply calls `SwishCrypto.Encrypt(AllBlocks)`, which
computes and appends its own SHA-256-based hash footer as part of encryption.

However, `SaveUtil.GetSaveFile(bytes)` returned `null` — the format was not
re-detected. Diagnosed by calling the public `SwishCrypto.GetIsHashValid(bytes.Span)`
API directly:

```
SwishCrypto.GetIsHashValid(bytes): True
SaveUtil.GetSaveFile returned null -- format not re-detected from raw bytes.
```

So the hash/integrity check **passes** — the encrypted payload is internally
self-consistent. `SaveUtil.IsSizeGen9SV(int length)` gates detection on a
hardcoded allowlist of specific byte-length values/ranges (see
`vendor/PKHeX.Core/Saves/Util/SaveUtil.cs` lines ~22-140), reverse-engineered
from real dumped saves at known game/DLC versions; our blank save's length
(`0x438160`) falls in a gap between the recognized buckets. See Bonus 2 below
for why this length is very unlikely to match a real save's length anyway.

### Bonus 2: direct `SAV9SV(Memory<byte>)` round-trip (bypasses `SaveUtil` entirely)

This is the sharper test: it calls the public decrypt-constructor directly,
skipping `SaveUtil`'s size heuristics altogether. **This throws:**

```
BONUS 2 check threw: ArgumentOutOfRangeException: None (Parameter 'type')
```

Root-caused precisely by reading the relevant source (not guessed):

- `BlankBlocks9.GetBlankBlocks()` → `SCBlockUtil.GetBlankBlockArray()`
  (`vendor/PKHeX.Core/Saves/Encryption/SwishCrypto/SCBlockUtil.cs:18-30`)
  constructs **every never-touched block** as
  `new SCBlock(key, SCTypeCode.None, dummy)` — i.e. a fresh blank save starts
  with thousands of placeholder blocks literally typed `None`, each holding
  arbitrary-length zeroed dummy bytes. Blocks only become "real" typed values
  (`UInt32`, `Object`, `Bool2`, etc.) once something actually calls
  `SetValue`/`SetBlockValue`/`ChangeStoredType` on them. This test only set a
  handful of values (`OT`, `Version`, one party slot), so the vast majority of
  blocks in the resulting save remained `Type = None`.
- `SCBlock.WriteBlock()` (`SCBlock.cs:149-169`) has no guard against
  `Type == None` — it happily writes the type byte (0) and the dummy data for
  every such block.
- `SCBlock.ReadFromOffset()` (`SCBlock.cs:247-301`), used both by
  `SwishCrypto.Decrypt` (called from the public `SAV9SV(Memory<byte>)`
  constructor) and transitively by `SaveUtil`, only special-cases
  `Bool1`/`Bool2`/`Bool3`, `Object`, and `Array`. Every other type code —
  including `None` — falls through to the `default: // Single Value Storage`
  branch, which calls `type.GetTypeSize()`
  (`SCTypeCode.cs:43-61`). `GetTypeSize()` has no case for `None` and
  explicitly throws `ArgumentOutOfRangeException(nameof(type), type.ToString())`,
  which is exactly the exception observed (`"None (Parameter 'type')"`).

**Conclusion:** a library-generated blank `SAV9SV` cannot be round-tripped
through `Write()` → re-parse (via either `SaveUtil` or the direct
constructor), but for a **different and more fundamental reason** than the
`SaveUtil`-size-allowlist gap in Bonus 1: the blank-block model intentionally
leaves most blocks untyped (`None`) until gameplay/editing assigns them real
values, and PKHeX.Core's own binary (de)serializer cannot represent `None` as
a stored type. **This is specific to blank/never-fully-hydrated saves** — a
real save dumped from an actual played game would have every block already
assigned a concrete type by the game itself, so this is not expected to be an
issue for real SV save files, only for `new SAV9SV()` used exactly as-is with
most blocks untouched. It does not affect the PRIMARY verification. No
hand-patched bytes were used to work around this, per the rules; this was
diagnosed by reading source, not by guessing.

## Gen9-specific notes

- **Terastallization**: not specifically exercised in this test (no Tera Type
  was set/read on the `PK9`), since the task's required checks are trainer
  name / party count / species / level. `PK9` does expose a `TeraTypeOriginal`
  field in the vendored source for future, more detailed verification.
- **`SAV9ZA` (Legends Z-A)**: present in `vendor/PKHeX.Core/Saves/SAV9ZA.cs`
  but **was not tested** in this task, per instructions — it's a much
  newer/less mature addition to PKHeX.Core and out of scope here. Only
  `SAV9SV` (Scarlet/Violet) was verified.
- The blank `SAV9SV` constructor defaults to the **latest** known save
  revision (`SaveRevision=2`, Indigo Disk/DLC2) rather than vanilla base game
  (`SaveRevision=0`) or Teal Mask (`SaveRevision=1`). Anyone re-testing this
  in the future should be aware that `new SAV9SV()` does not represent a
  "day one" save layout.

## Recommendation

Library-generated saves are sufficient to confirm PKHeX.Core's Gen9 in-memory
object model (`SAV9SV`/`PK9`) round-trips trainer name, party count, species,
and level correctly. However, **this should be re-verified against a real
Scarlet/Violet save file dumped from actual hardware/emulator once one is
available**, particularly to:
- confirm that a real save (where every block is fully hydrated by actual
  gameplay, so no block is left `Type = None`) round-trips cleanly through
  `Write()` → `new SAV9SV(bytes)` — expected to work based on the root-cause
  analysis above, but not directly observed since no real save file was
  available in this worktree;
- confirm the `Write()` output byte length of a *real* save at a known game
  version falls inside `SaveUtil`'s hardcoded size allowlist; and
- exercise Tera Type and other Gen9-specific fields not covered here.

## Environment

- `dotnet --version`: 10.0.302
- Vendored PKHeX.Core: `vendor/PKHeX.Core/PKHeX.Core.csproj` (net10.0 class
  library, no Android/MAUI/emulator dependency)
- Verify harness: `verify/Gen9/Gen9Verify.csproj`, `verify/Gen9/Program.cs`
- Run via: `cd verify/Gen9 && dotnet run`
