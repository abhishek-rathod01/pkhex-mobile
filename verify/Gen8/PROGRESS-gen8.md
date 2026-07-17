# Gen 8 (Sword/Shield) Save-Parsing Verification

## Scope

Verifies that PKHeX.Core (vendored at `vendor/PKHeX.Core/`) can construct, populate, and
read back a Generation 8 save file (`SAV8SWSH`, used by Pokémon Sword/Shield) as a plain
net10.0 console app, with no Android/MAUI/emulator involved.

Only **SWSH** (`SAV8SWSH`) was exercised. **BDSP** (`SAV8BS`) and **Legends: Arceus**
(`SAV8LA`) were NOT separately tested — they use notably different save structures from
SWSH (see "Not covered" below) and are out of scope for this pass.

## Test data source

**Library-generated blank save only.** No real Sword/Shield `.sav` file exists in this
worktree (confirmed via a search for `*.sav` under the repo root — none found), and per
the task rules no synthetic save bytes were hand-written anywhere. The save file under
test was constructed entirely through PKHeX.Core's own public API:

```csharp
var sav = new SAV8SWSH();   // parameterless blank constructor (see vendor/PKHeX.Core/Saves/SAV8SWSH.cs:21-28)
sav.OT = "VERIFY";
var pk = sav.BlankPKM;      // returns new PK8()
pk.Species = 25;            // Pikachu
pk.CurrentLevel = 10;
sav.PartyData = [pk];       // list-setter -> sets PartyCount = ctr correctly
```

`SAV8SWSH()` builds its state from `BlankBlocks8.GetBlankBlocks()` (a full set of blank
`SCBlock` key/value entries), then runs `Initialize()` and `ClearBoxes()`. This differs
architecturally from Gen4's dual-partition system: Gen8+ uses the "SCBlock" system, a
flat collection of individually-checksummed key/value blocks (see
`vendor/PKHeX.Core/Saves/Encryption/SwishCrypto/SwishCrypto.cs`) rather than a single
big buffer with an internal footer.

## Harness

- `verify/Gen8/Gen8Verify.csproj` — net10.0 console app, `ProjectReference` to
  `../../vendor/PKHeX.Core/PKHeX.Core.csproj`.
- `verify/Gen8/Program.cs` — builds the blank save as above, prints trainer/party info,
  then attempts an optional bonus round-trip check.
- Run via `dotnet run` from `verify/Gen8/`.

## Results

### Primary verification (required) — PASS

Read directly off the live `SAV8SWSH` object, no serialization involved:

```
Trainer: VERIFY
PartyCount: 1
  Species=25 Level=10

PRIMARY VERIFICATION RESULT: PASS
```

Confirmed working:
- Trainer name (`sav.OT`) set and read back correctly.
- Party count (`sav.PartyCount`) correctly reflects the assigned party via the
  `PartyData` list-setter (which sets `PartyCount = ctr` directly, per the same
  caveat noted for Gen4 — `SetPartySlotAtIndex` alone would not reliably do this).
- Species (`25`, Pikachu) and level (`10`) round-trip correctly on the `PK8` stored in
  the party slot.

### Bonus / optional check — `Write()` + `SaveUtil.GetSaveFile` round-trip

Unlike Gen4 (which threw `ArgumentOutOfRangeException` in checksum code), for Gen8
SWSH the `Write()` call **succeeded without throwing**:

```
Write() succeeded. Output length: 1581126 bytes (0x182046).
SaveUtil.GetSaveFile returned null: format was NOT re-detected from the written bytes.
```

Root cause (read from source, not guessed): `SaveUtil.GetSaveFile` dispatches to format
sniffing that requires the byte length to exactly match one of a fixed set of known
SWSH save-file sizes (see `vendor/PKHeX.Core/Saves/Util/SaveUtil.cs`):

```
SIZE_G8SWSH    = 0x1716B3  // 1.0
SIZE_G8SWSH_1  = 0x17195E  // 1.0 -> 1.1
SIZE_G8SWSH_2  = 0x180B19  // 1.0 -> 1.1 -> 1.2
SIZE_G8SWSH_2B = 0x180AD0  // 1.0 -> 1.2
SIZE_G8SWSH_3  = 0x1876B1  // 1.0 -> 1.1 -> 1.2 -> 1.3
SIZE_G8SWSH_3A = 0x187693  // 1.0 -> 1.1 -> 1.3
SIZE_G8SWSH_3B = 0x187668  // 1.0 -> 1.2 -> 1.3
SIZE_G8SWSH_3C = 0x18764A  // 1.0 -> 1.3
```

The blank-constructed save's block set (`BlankBlocks8.GetBlankBlocks()`) serializes via
`SwishCrypto.Encrypt` to **0x182046** bytes — not one of the sizes above (it doesn't
correspond to any real shipped game-version/DLC combination's exact block layout), so
`IsG8SWSH`/`IsSizeGen8SWSH` returns false and `GetSaveFile` returns `null`. This is a
**different failure mode than Gen4**: no crash, no corrupted buffer — just a
byte-length mismatch against a fixed allow-list of known real save sizes, which a
library-generated blank save (not derived from a real game dump) doesn't happen to hit.
This matches the task's expectation that the SCBlock architecture could behave
differently from Gen4's failure mode, and it did.

This is an **optional/secondary check only** — per the task instructions, it is not
required to pass, and no hand-patching of bytes was attempted to work around it.

## Known limitations

1. The `Write()` → `SaveUtil.GetSaveFile()` re-detection round-trip does not succeed for
   a library-generated blank save, because its serialized size doesn't match any of the
   hard-coded known-good SWSH save sizes in `SaveUtil`. This is expected/documented, not
   a bug in the verification harness. It would very likely succeed with a real
   game-dumped save loaded through `new SAV8SWSH(data)` / `SaveUtil.GetSaveFile`, since
   the real dump's on-disk size would match one of the allow-listed constants.
2. BDSP (`SAV8BS`) and Legends: Arceus (`SAV8LA`) were not tested in this pass — they
   have their own distinct SAV classes and were out of scope here.
3. All results above come from a library-generated blank save, not a real console/game
   dump. **Library-generated saves should be re-verified against real Sword/Shield game
   saves once one becomes available**, particularly to confirm the `Write()`/size/
   re-detection behavior against genuine save sizes.

## Summary

| Check | Status |
|---|---|
| Blank `SAV8SWSH` construction | PASS |
| Trainer name (OT) read/write | PASS |
| Party count via `PartyData` setter | PASS |
| Species/Level round-trip on party Pokémon | PASS |
| `Write()` (serialize to bytes) | PASS (no throw) |
| `SaveUtil.GetSaveFile` re-detection of written bytes | Did not re-detect (size mismatch against known real save sizes) — documented as a known limitation, not required for this task |
| BDSP / Legends Arceus | Not tested (out of scope) |
