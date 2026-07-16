# Gen 4 (Diamond/Pearl/Platinum/HeartGold/SoulSilver) Save-Parsing Verification

## Status: VERIFIED (library-generated save, not a real game save)

No real Gen4 `.sav` file exists anywhere in this worktree (searched for `*.sav` under
`C:\Users\abhis\Desktop\pkhex-mobile-gen4` — none found). Verification below was performed
against a **library-generated in-memory save object**, not a save produced by an actual DS
game/emulator. This should be re-verified against real game saves once one is available.

## What was tested

Harness: `verify/Gen4/` (console app, `net10.0`, `ProjectReference` to
`vendor/PKHeX.Core/PKHeX.Core.csproj`). Run via `dotnet run` from `verify/Gen4/`.

For each save type, the same recipe was used:

```csharp
var sav = new SAV4DP();      // or SAV4Pt(), SAV4HGSS()
sav.OT = "VERIFY";
var pk = sav.BlankPKM;
pk.Species = 25;             // Pikachu
pk.CurrentLevel = 10;
sav.PartyData = [pk];        // IMPORTANT: use the PartyData list-setter, not
                              // SetPartySlotAtIndex, which only conditionally updates
                              // PartyCount and can leave it at 0.
```

Verification is a direct read off the live object (trainer name, party count, per-slot
species/level) — this is the primary, required check, and it is what actually matters for
confirming PKHeX.Core's Gen4 object model works correctly.

### Results

**SAV4DP (Diamond/Pearl)** — confirmed working:
```
Trainer: VERIFY
PartyCount: 1
  Species=25 Level=10
```

**SAV4Pt (Platinum)** — bonus check, confirmed working, identical output to DP:
```
Trainer: VERIFY
PartyCount: 1
  Species=25 Level=10
```

**SAV4HGSS (HeartGold/SoulSilver)** — bonus check, confirmed working, identical output:
```
Trainer: VERIFY
PartyCount: 1
  Species=25 Level=10
```

No differences observed between DP, Pt, and HGSS for this recipe — all three save types
expose the same `OT`, `BlankPKM`, `PartyData` surface at the `SaveFile`/`SAV4` base-class
level and behave identically for trainer name / party count / species / level.

## Known limitation: Write() -> SaveUtil.GetSaveFile round-trip does not work

Going one step further than the required check — calling `sav.Write()` to serialize to bytes
and then `SaveUtil.GetSaveFile(bytes)` to simulate "load this as a file from disk" — does
**not** round-trip successfully for library-generated Gen4 saves, via the public API alone:

1. `new SAV4DP()` (parameterless constructor) leaves the base class's contiguous
   `Buffer`/`Data` empty — the parameterless ctor only populates the separate
   `GeneralBuffer`/`StorageBuffer` arrays, not the base buffer. Calling `.Write()` on this
   throws `ArgumentOutOfRangeException` deep in `BlockInfo4.GetRevision`, because
   `SAV4.SetChecksums()` indexes into `Data[PartitionSize..]` (`PartitionSize = 0x40000`),
   which doesn't exist on an empty buffer.
2. Constructing instead via `new SAV4DP(new byte[SaveUtil.SIZE_G4RAW])` (a correctly-sized
   zeroed buffer) avoids the crash. `Write()` succeeds and produces `524288` bytes
   (confirmed by this harness — matches `SaveUtil.SIZE_G4RAW`). However,
   `SaveUtil.GetSaveFile(bytes)` still returns `null` ("not recognized") — **confirmed by
   this harness's "Known limitation check" section**.

Root cause (reported by orchestrator, not re-derived here per instructions): PKHeX's Gen4
save-type detector (`IsG4DP` / `IsValidGeneralFooter2` in `SaveUtil`) requires a "size" field
at a specific footer offset. That field has no public setter anywhere in `SAV4.cs` — only the
"magic"/sdk field has one (via the public `Magic` property). Without hand-patching raw bytes
(which is forbidden by the task rules), this round-trip cannot be made to pass through the
public API alone.

**This is a known, documented limitation, not a blocker.** The object-model verification
above (trainer/party/species/level) is what confirms Gen4 save parsing works correctly for
PkhexMobile's purposes; the Write()/re-detection gap is a separate, narrower issue specific
to synthetic in-memory round-tripping.

## Follow-up

- Re-verify this recipe against a real DP/Pt/HGSS save file (e.g. exported from an emulator
  or flashcart) once one is available in the repo, to confirm the object model behaves the
  same way when initialized from real game data via `SaveUtil.GetSaveFile`.
- If Gen4 file-based loading needs to be supported end-to-end in PkhexMobile, the
  `SaveUtil` footer "size" field gap noted above will need a real (non-synthetic) save to
  test against, since it cannot be exercised via a from-scratch library-generated buffer.
