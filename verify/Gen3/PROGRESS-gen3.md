# Gen 3 (Ruby/Sapphire/Emerald/FireRed/LeafGreen) Save-Parsing Verification

## Status: VERIFIED against a REAL save file (2026-07-17, updated)

A real Pokémon Emerald trainer save (`pokeemerald (2).sav`, 131072 bytes, Trainer
Abhishk, 5-member party) was provided, closing the gap noted below. Confirmed both via
console-level `SaveUtil.GetSaveFile` read (`SAV3E`, Version E) and through the actual
app UI (file picker → party list → detail screen) on the PkhexMobile_Emulator AVD.

- Party list showed all 5 members correctly: MEWTWO Lv.100, DEOXYS Lv.100, SALAMENCE
  Lv.100, MEW Lv.30, TELLYTEBBY/Abra Lv.9 (a custom nickname distinct from species,
  confirming the nickname-vs-species-name display logic works for Gen3 too).
- Detail screen for MEW showed: Species Mew, Nature Mild, Ability Synchronize, Moves
  Fly/Surf/Waterfall/Dive, IVs 17/12/23/26/15/3, EVs all 0 — all matching the
  console-level read exactly.

This closes the last remaining "no real save file" gap in the project — all 9
generations (1 through 9, including 7b/LGPE) are now confirmed against genuine save
files, not just library-generated ones.

### Earlier finding (superseded): GCI box file only

Before the Emerald save was provided, the only Gen3-adjacent real file available was
`01-GPXP-pokemon_rs_memory_box.gci` (483392 bytes, GameCube memory card export) —
recognized as `SAV3RSBox` (Ruby/Sapphire GameCube box-storage format), but a
**PC-box-only storage container** with no active party (`PartyCount: 0`). It confirmed
the GCI format parses but didn't exercise trainer/party/species/level reads. That gap
is now closed by the real Emerald save above.

## Record correction

Prior session history had suggested Gen 3 support was "already working." That is
**incorrect**. A review of session history shows the only generation actually
exercised in an earlier pass was Gen 1 (RBY), via a hand-synthesized save file,
before the "no hand-written bytes" rule existed. **Gen 3 had never been verified
until this session.** This document is the first real verification of Gen 3
save-parsing in PKHeX.Core for this project.

## Test methodology

- **No real `.sav` file was used.** The worktree (`C:\Users\abhis\Desktop\pkhex-mobile-gen3`)
  was searched for any `*.sav` files before starting; none exist in the repo.
- Per the project rule, **no synthetic save bytes were hand-written**. Instead, a
  save file was constructed purely through PKHeX.Core's own public API
  (`new SAV3E()` blank constructor + `BlankPKM`), exactly the pattern already
  proven for Gen 4 (`SAV4DP`).
- Verification reads state directly off the live in-memory `SaveFile`/`PKM`
  objects — no serialization round-trip is required for the primary check.

## Variant tested

PKHeX.Core has multiple Gen 3 save containers, all subclasses of the abstract
`SAV3` base (`vendor/PKHeX.Core/Saves/SAV3.cs`):

- `SAV3E` — Emerald (**this is the variant tested**)
- `SAV3RS` — Ruby/Sapphire
- `SAV3FRLG` — FireRed/LeafGreen
- `SAV3Colosseum` / `SAV3XD` — GameCube spinoffs (Colosseum/XD), not standard handheld saves

Only **Emerald (`SAV3E`)** was exercised in this pass, to keep scope bounded, per
instructions. `SAV3RS` and `SAV3FRLG` were read (constructors confirmed to exist
with the same `(bool japanese = false)` / `(Memory<byte> data)` shape as `SAV3E`,
inherited from the shared `SAV3` base) but were **not separately instantiated or
tested**. Given they share the same base class and construction pattern, they are
expected to behave the same way, but this is an assumption, not a verified fact —
future work should explicitly test `SAV3RS` and `SAV3FRLG` too, since Ruby/Sapphire
and FireRed/LeafGreen use different save block layouts internally
(`SaveBlock3RS`/`SaveBlock3FRLG` vs `SaveBlock3SmallE`/`SaveBlock3LargeE`) even
though the public constructor surface looks identical.

## Constructor confirmed

From `vendor/PKHeX.Core/Saves/SAV3E.cs`:

```csharp
public SAV3E(Memory<byte> data) : base(data) { ... }
public SAV3E(bool japanese = false) : base(japanese) { ... }
```

The blank constructor `new SAV3E()` (defaulting `japanese: false`) was used.

## Harness

- Project: `verify/Gen3/Gen3Verify.csproj` (net10.0 console app, `ProjectReference`
  to `../../vendor/PKHeX.Core/PKHeX.Core.csproj`)
- Entry point: `verify/Gen3/Program.cs`
- Run with: `dotnet run` from `verify/Gen3/`

## What was confirmed working (PRIMARY verification, read directly off the live object)

```
=== Gen 3 (Emerald) Save Verification ===
Trainer: VERIFY
TID16: 12345
PartyCount: 1
  Species=25 Level=10 PID=1234ABCD
  Nature=Mild (derived from PID % 25)
  Ability=9 AbilityNumber=1 (derived from PersonalInfo + AbilityBit)
  Gender=0 (derived from PID + species gender ratio)
  OT=VERIFY

=== Primary verification complete ===
```

Confirmed:
- `sav.OT` (trainer name) round-trips correctly through `SaveBlock3SmallE.OriginalTrainerTrash`.
- `sav.TID16` set/get works.
- `sav.PartyData = [pk]` (list-setter, NOT `SetPartySlotAtIndex` directly) correctly
  sets `PartyCount = 1`, matching the documented trap from the Gen4 pass.
- `sav.PartyData[i]` round-trips `Species`, `CurrentLevel`, `PID`, `OriginalTrainerName`.

## Gen 3-specific quirks investigated

Gen 3 does **not** store Nature or Ability as independent fields the way Gen 4+
does — they're both *derived* from the Personality Value (PID), confirmed by
reading `vendor/PKHeX.Core/PKM/Shared/G3PKM.cs` (the shared abstract base for
`PK3`/`CK3`/`XK3`):

```csharp
public sealed override int Ability { get => PersonalInfo.GetAbility(AbilityBit); set { } }
public sealed override Nature Nature { get => (Nature)(PID % 25); set { } }
public sealed override byte Gender { get => EntityGender.GetFromPID(Species, PID); set { } }
public sealed override byte Form { get => Species == Unown ? EntityPID.GetUnownForm3(PID) : 0; ... }
```

Notable consequences, all observed directly in the test run above:
- Setting `Nature`, `Ability`, or `Gender` directly on a `PK3` is a **no-op**
  (setters exist to satisfy the abstract `PKM` contract but do nothing) — the
  only way to change these is to change `PID` (or brute-force search for a PID
  that yields the desired value, which is what `Form` setter does internally
  for Unown, and what real encounter generation does for forced natures/genders/abilities).
- With `PID = 0x1234ABCD` (decimal 305441741): `Nature` came out as `Mild`
  (`305441741 % 25 == 16`, and Nature index 16 is Mild), `Ability` came out as
  ID `9` (Static) with `AbilityNumber=1` (i.e. first ability slot, since
  `AbilityBit` is derived from a specific PID bit — ability ID 9 is Pikachu's
  first-slot ability), `Gender` came out `0` (male) for Pikachu's gender ratio.
  This matches expected Gen 3 mechanics.
- `CurrentLevel` setter (`PKM.cs`) computes `EXP` from the species' growth rate
  (`PersonalInfo.EXPGrowth`) — this requires `Species` to already be set before
  `CurrentLevel` is assigned, which the harness does in the correct order.
- Species storage is via an internal Gen 3 dex index (`SpeciesInternal`,
  distinct from the National Dex number) with a translation layer in
  `PK3.Species` — verified this round-trips correctly (Pikachu National Dex 25
  in, 25 out).

## Known limitation (bonus/optional check — matches the documented Gen4-style trap)

As explicitly flagged as optional/expected-to-possibly-fail in the task
instructions, a `sav.Write()` → `SaveUtil.GetSaveFile(bytes)` round trip was
attempted as a bonus check (not required for the primary goal). It **failed**,
throwing during `Write()` itself (before even reaching `GetSaveFile`):

```
System.ArgumentOutOfRangeException: Specified argument was out of the range of valid values.
   at PKHeX.Core.SAV3.WriteSectors(Span`1 data, Int32 group) in Saves\SAV3.cs:line 99
   at PKHeX.Core.SAV3.GetFinalData() in Saves\SAV3.cs:line 158
   at PKHeX.Core.SaveFile.Write(BinaryExportSetting setting) in Saves\SaveFile.cs:line 47
```

This is consistent with the known Gen4 trap: the blank constructor path does not
allocate/populate the full backing buffer that the real `Write()` checksum/sector
path expects (Gen 3 saves use a rotating flash-sector layout with per-sector
footers, which `WriteSectors` assumes are fully sized). No hand-patching of raw
bytes was attempted to work around this, per the explicit rule against
hand-writing synthetic save bytes. Only one attempt was made at this bonus check,
consistent with instructions not to spend more than one attempt fighting it.

**This does not affect the primary verification**, which reads state directly off
the live `SaveFile`/`PKM` objects and does not depend on `Write()`/re-detection.

## Caveats / follow-up work

- Library-generated blank saves were used, not a real Ruby/Sapphire/Emerald/
  FireRed/LeafGreen save dumped from actual game hardware or an emulator. **This
  should be re-verified against a real game save once one is available**, since
  a library-generated blank save cannot catch bugs in how PKHeX.Core parses the
  quirks of *real* save data (e.g. sector rotation/footer validation, actual
  Pokédex flag layouts, real box data, RS vs Emerald vs FRLG block-size
  differences).
- Only `SAV3E` (Emerald) was tested. `SAV3RS` and `SAV3FRLG` were not
  independently instantiated/verified in this pass.
- The `Write()`/re-detect round trip is not currently functional starting from a
  blank-constructed save object (see above) — this is a pre-existing PKHeX.Core
  behavior around blank-save buffer sizing, not something introduced by this
  verification work.
