# Gen 7 Save Support Verification (Sun/Moon, Ultra Sun/Ultra Moon)

## Scope

Generation 7 covers: Sun/Moon (`SAV7SM`), Ultra Sun/Ultra Moon (`SAV7USUM`), and
Let's Go Pikachu/Eevee (`SAV7b`). Per task instructions, only **SM and USUM**
were tested here. **LGPE (`SAV7b`) was not separately tested** — it uses a
notably different save structure (different block layout / no shared
`SAV_BEEF` base) than SM/USUM, and was explicitly out of scope for this pass.

## Test method

**No real save file was used or found.** A search for `*.sav` files under the
worktree turned up nothing, so — per the required recipe — a save was
constructed purely through PKHeX.Core's own blank constructors:

- `new SAV7SM()` — confirmed via `vendor/PKHeX.Core/Saves/SAV7SM.cs`: parameterless
  constructor calling `base(SaveUtil.SIZE_G7SM, SaveBlockAccessor7SM.BlockMetadataOffset)`,
  then `Blocks = new SaveBlockAccessor7SM(this)`, `Initialize()`, `ClearBoxes()`.
- `new SAV7USUM()` — same pattern, calling `base(SaveUtil.SIZE_G7USUM, boUU)`.

Both are simple parameterless calls, no hand-written bytes involved anywhere.

No synthetic/hand-crafted save bytes were written at any point, per the
project rule.

## Harness

- `verify/Gen7/Gen7Verify.csproj` — net10.0 console app, `ProjectReference` to
  `../../vendor/PKHeX.Core/PKHeX.Core.csproj`.
- `verify/Gen7/Program.cs` — exercises the recipe against both `SAV7SM` and
  `SAV7USUM`, plus one Gen7-specific quirk check and one optional bonus check.

Run with `dotnet run` from `verify/Gen7/`.

## Results

### Primary verification (required) — PASSED for both SM and USUM

Direct reads off the live `SaveFile` object (no serialization):

```
=== SAV7SM (Sun/Moon) ===
Trainer: VERIFY
PartyCount: 1
  Species=25 Level=10

=== SAV7USUM (Ultra Sun/Ultra Moon) ===
Trainer: VERIFY
PartyCount: 1
  Species=25 Level=10
```

Confirmed working for both SM and USUM:
- Trainer name (`sav.OT`) round-trips correctly through the setter.
- `sav.PartyData = [pk]` correctly sets `PartyCount` to 1 (using the
  list-setter, not `SetPartySlotAtIndex`, per the required recipe — the
  setter explicitly assigns `PartyCount = ctr`).
- Species (25, Pikachu) and CurrentLevel (10) are preserved on the
  round-tripped party slot read back via `sav.PartyData[i]`.

### Gen7-specific quirk check — Alolan forms — PASSED

Gen 7 introduced regional (Alolan) forms, stored via the `PK7.Form` property
(`vendor/PKHeX.Core/PKM/PK7.cs:106`, packed into bits of byte `0x1D`). Tested
by setting an Alolan Rattata (Species=19, Form=1) into a `SAV7USUM` party:

```
=== Gen7 quirk: Alolan form (Rattata, Form=1) ===
  Species=19 Form=1 Level=12
```

Form data round-trips correctly through `PartyData` get/set, confirming
Alolan-form Pokémon are handled correctly by the party storage path used here.

Z-moves were not separately tested — they are move-data / battle mechanics,
not part of the save-parsing surface (species/level/OT/party) this task
verifies, and are out of scope for a read-only save-parsing check.

### Bonus check (optional) — Write() + SaveUtil.GetSaveFile round-trip — PARTIAL / KNOWN LIMITATION

```
=== Bonus: Write()/GetSaveFile round-trip (SAV7SM) ===
Write() succeeded, byte length: 441856
GetSaveFile did NOT re-detect the save format (returned null).
```

Unlike the Gen4 case (which threw `ArgumentOutOfRangeException` inside
checksum code), **Gen7's `Write()` did not crash** — it produced a correctly
sized buffer (441856 bytes = `0x6BE00` = `SaveUtil.SIZE_G7SM`, exactly
matching the expected SM save size). The failure is purely in
**re-detection**, and the root cause was traced precisely:

- `SaveUtil.GetSaveFile` / `IsG7SM` (`vendor/PKHeX.Core/Saves/Util/SaveUtil.cs:385`)
  requires both the correct file length **and** `HasSaveFooterBEEF(data)`
  (`SaveUtil.cs:412`), which checks that the last `0x1F0` bytes begin with the
  magic value `0x42454546` ("BEEF").
- That "BEEF" magic is part of the block-info table maintained by
  `SAV_BEEF` (`vendor/PKHeX.Core/Saves/SAV_BEEF.cs`), which both `SAV7SM` and
  `SAV7USUM` derive from. The blank constructor allocates a zeroed buffer of
  the right total size and computes block checksums via `SetChecksums()` /
  `AllBlocks.SetChecksums(Data)`, but never populates the block-info table's
  "BEEF" magic marker itself — that marker is only ever present in a buffer
  that originated from a real 3DS-written save (loaded via the
  `SAV7SM(Memory<byte> data)` constructor).
- Net effect: this is structurally the same class of gap flagged as a known
  trap for Gen4 (a field the blank constructor doesn't populate that
  format-sniffing depends on), just manifesting as a clean `null` from
  `GetSaveFile` instead of a thrown exception. No public PKHeX.Core API was
  found to set this marker on a from-scratch blank save.
- Per instructions, this was attempted once as an optional secondary check,
  did not throw, and is documented here as a factual, known limitation — no
  further attempts were made and no bytes were hand-patched to work around it.

This does **not** affect the primary verification, which reads directly off
the live `SaveFile` object and does not depend on the BEEF footer.

## Summary

| Check | SM | USUM |
|---|---|---|
| Blank construction | OK | OK |
| OT round-trip | OK | OK |
| PartyCount via `PartyData` setter | OK | OK |
| Species / Level round-trip | OK | OK |
| Alolan `Form` round-trip (USUM) | — | OK |
| `Write()` produces correctly-sized buffer | OK | not separately run |
| `SaveUtil.GetSaveFile()` re-detection after `Write()` | Fails (returns null; BEEF footer never populated by blank ctor) | not separately run |

LGPE (`SAV7b`) was not tested in this pass.

## Caveat

All of the above was verified against a **library-generated** blank save, not
a real Sun/Moon/Ultra Sun/Ultra Moon save dumped from a game/emulator. This
should be **re-verified against a real Gen7 save file** once one becomes
available, particularly to confirm the BEEF footer / block-info assumptions
above and general behavior of a save file with populated real-world data
(existing Pokédex, items, etc.).
