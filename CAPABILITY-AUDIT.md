# PKHeX.Core Capability Gap Audit

**Date:** 2026-07-22 · **Scope:** what `vendor/PKHeX.Core` offers vs. what `PkhexMobile` exposes.
**Status:** read-only audit. No app code was modified.

**Method.** The app's real PKHeX surface was extracted by grepping every `PkhexMobile/*.cs`
for `PKM`/`SaveFile` member accesses (not inferred from PROGRESS.md, which lags). Every
per-generation claim below was checked against the actual `vendor/` source and is cited
`file:line` — none are inherited from folklore or from the task brief's examples.

**The recurring bug class this project keeps hitting** (Gen1/2 IVs, Gen3 Nature/Ability,
Gen4 Nature): *the setter exists to satisfy the abstract `PKM`/`SaveFile` contract, but the
generation genuinely doesn't store that field, so it silently no-ops.* Every row's
"Generation caveats" column answers exactly that question. Rows marked **UNIFORM** are safe
to implement generically. Rows marked **SPLIT** need the project's established
disable-the-control-but-show-the-true-value-with-an-inline-reason treatment.

---

## 1. Already integrated — do not duplicate

| Capability | API used | Where |
|---|---|---|
| Save load / format detect | `SaveUtil.GetSaveFile` | `MainPage.xaml.cs` |
| Save export | `sav.Write()` + `FileSaver` | `PokemonDetailPage`, `BoxListPage` |
| Party list | `sav.PartyData`, `PartyCount` | `PartyListPage`, `BoxListPage` |
| Box browse | `BoxCount`, `BoxSlotCount`, `GetBoxSlotAtIndex` | `BoxListPage` |
| Box names (**read only**) | `IBoxDetailNameRead.GetBoxName` | `BoxListPage` |
| Box↔party move / swap | `Set{Box,Party}SlotAtIndex`, `DeletePartySlot`, `ResetPartyStats` | `PokemonSlotMover.cs` |
| Nickname + `IsNicknamed` | `pk.Nickname` | `PokemonDetailPage` |
| Level | `pk.CurrentLevel` (set **after** `Species`) | `PokemonDetailPage` |
| Species | `pk.Species` | `PokemonDetailPage` |
| Moves ×4 | `pk.SetMoves(...)` (recomputes PP) | `PokemonDetailPage` |
| IVs / EVs ×6 | `pk.IV_*` / `pk.EV_*` | `PokemonDetailPage` |
| Form / Nature / Ability | `pk.Form` / `Nature` (+`StatAlignment` on Fmt≥8) / `Ability` | `PokemonDetailPage` |
| Legality (**read only**) | `new LegalityAnalysis(pk)`, `la.Valid`, `la.Report()` | `PokemonDetailPage` |
| Held item (**display only**) | `pk.HeldItem` → sprite + name | `PokemonDetailPage`, list rows |
| Shiny (**display only**) | `pk.IsShiny` → shiny sprite | `SpriteHelper`, list rows |

---

## 2. Master gap table

Priority = (user value × feasibility) ÷ risk. **P1** = do first. **P4** = don't, or not yet.

| # | Capability | PKHeX.Core API | Integrated? | Generation caveats | Diff. | Pri |
|---|---|---|---|---|---|---|
| 1 | **Trainer identity** (OT, TID/SID, gender, language) | `sav.OT`, `TrainerTID7`/`TrainerSID7`, `TID16`/`SID16`, `sav.Gender`, `sav.Language` | ❌ (`sav.OT` read once) | **UNIFORM** — overridden by every mainline `SAV1`…`SAV9ZA` | Low | **P1** |
| 2 | **Money / play time** | `sav.Money` (`MaxMoney`), `PlayedHours/Minutes/Seconds` | ❌ | **UNIFORM** — same override coverage as #1 | Low | **P1** |
| 3 | **Held item *editing*** | `pk.ApplyHeldItem(id, ctx)`, `pk.MaxItemID` | ❌ display only | **SPLIT** — Gen1 hard no-op (`PK1.cs:157`) | Low | **P1** |
| 4 | **EV 510-budget + correct caps** | `pk.GetMaximumEV(i)`, `pk.MaxIV/MaxEV` | ❌ hardcoded | **UNIFORM** (library handles it) — see §4.1 | Low | **P1** |
| 5 | **Box rename** | `IBoxDetailName.SetBoxName` | ❌ read only | **SPLIT** — interface check; `SAV7b` lacks it | Low | **P1** |
| 6 | **Friendship** | `pk.CurrentFriendship`, `OriginalTrainerFriendship` | ❌ | **SPLIT** — Gen1 no-op (`PK1.cs:155`) | Low | **P2** |
| 7 | **Ball** | `pk.Ball`, `pk.MaxBallID` | ❌ | **SPLIT** — Gen1/2 no-op (`GBPKM.cs:135`) | Low | **P2** |
| 8 | **`.pk` entity export** | `EntityFileNamer.GetName`, `pk.WriteDecryptedDataParty` | ❌ | **UNIFORM** | Low | **P2** |
| 9 | **`.pk` entity import** | `EntityFormat.GetFromBytes`, `EntityConverter.ConvertToType` | ❌ | **UNIFORM** (converter reports failure) | Med | **P2** |
| 10 | **Showdown *export*** | `new ShowdownSet(pk).Text` | ❌ | **UNIFORM** | Low | **P2** |
| 11 | **PP / PP-Ups** | `pk.Move1_PP`, `Move1_PPUps`, … | ❌ | **UNIFORM** | Low | **P2** |
| 12 | **Pokédex *display*** | `HasPokeDex`, `GetSeen/GetCaught`, `SeenCount`, `PercentCaught` | ❌ | **UNIFORM** for *reads* (all gens route via `Zukan`) | Low | **P2** |
| 13 | **Showdown *import*** | `pk.ApplySetDetails(ShowdownSet)` | ❌ | **UNIFORM** but writes ~15 fields at once | Med | **P3** |
| 14 | **Origin / met data** | `pk.Version`, `MetLocation`, `MetLevel`, `MetYear/Month/Day`, `EggLocation` | ❌ | **SPLIT** — dates are `virtual{get=>0;set{}}` on `PKM.cs:185-187`, real Gen4+ | Med | **P3** |
| 15 | **Egg status** | `pk.IsEgg`, `ForceHatchPKM`, `SetEggMetData` | ❌ | **SPLIT** — Gen1 has no eggs | Med | **P3** |
| 16 | **Language / OT gender / handler** | `pk.Language`, `OriginalTrainerName/Gender`, `HandlingTrainer*`, `CurrentHandler` | ❌ | **SPLIT** — handler fields `virtual{}` pre-Gen6 (`PKM.cs:188-190`) | Med | **P3** |
| 17 | **Bag / inventory** | `sav.Inventory` → `PlayerBag.Pouches` → `InventoryPouch`, `CopyTo(sav)` | ❌ | **UNIFORM-ish** — base returns `EmptyPlayerBag` (graceful) | Med-Hi | **P3** |
| 18 | **Markings** | `IAppliedMarkings<bool>` / `<MarkingColor>`, `ToggleMarking` | ❌ | **SPLIT** — generic param differs Gen3-6 vs Gen7+; none Gen1/2 | Med | **P3** |
| 19 | **Box ops: sort/clear/compress** | `sav.SortBoxes`, `ClearBoxes`, `CompressStorage`, `ModifyBoxes` | ❌ | **UNIFORM** | Med | **P3** |
| 20 | **Box wallpaper / current box** | `IBoxDetailWallpaper`, `sav.CurrentBox`, `BoxesUnlocked` | ❌ | **SPLIT** — interface check | Low | **P3** |
| 21 | **PC / box binary dump** | `GetPCBinary`, `GetBoxBinary`, `SetPCBinary`, `SetBoxBinary` | ❌ | **UNIFORM** | Low | **P3** |
| 22 | **Pokérus** | `pk.PokerusStrain`, `PokerusDays`, `Editing/Pokerus.cs` | ❌ | **SPLIT** — absent Gen1 | Low | **P3** |
| 23 | **Contest stats** | `IContestStats` | ❌ | Gen3-6 only | Med | **P4** |
| 24 | **Ribbons** | `IRibbonIndex`, `RibbonApplicator` | ❌ | ~130 props across gens; applicator is **legality-driven** | High | **P4** |
| 25 | **Bulk / batch edit** | `EntityBatchEditor`, `BatchFilters`, `StringInstruction` | ❌ | **UNIFORM** but reflection-based; needs a query UI | High | **P4** |
| 26 | **Event flags / work** | `sav.GetFlag/SetFlag`, `EventWorkspace`, `EventLabelCollection` | ❌ | Raw offsets, per-game label files | High | **P4** |
| 27 | **Mystery Gift** | `MysteryGift`, `WC6/WC7/WC8/WC9/PCD/PGF`… | ❌ | Per-gen card classes, no common editor | High | **P4** |
| 28 | **Format-specific single-gen interfaces** — Hyper Training (`IHyperTrain`), AVs (`IAwakened`, LGPE), Dynamax (`IDynamaxLevel`), Gigantamax, Tera type (`ITeraType`), height/weight/scale (`IScaledSize`), Alpha/Noble (LA), Ganbaru, memories (`IMemoryOT/HT`), Super Training, affection, sociability, `ITechRecord`/`IMoveShop8`, `IHomeTrack`, `IGeoTrack`, `IRegionOrigin` | one interface each | ❌ | **Each is one-or-two-generations-only by construction** — every one needs its own `is IFoo` check + disabled state everywhere else | Med each | **P4** |
| 29 | **QR code import/export** | `QRMessageUtil`, `QRPK7` | ❌ | Gen7-centric; needs a camera/encoder dep | Med | **P4** |
| 30 | **Auto-legalization / suggestions** | `EncounterSuggestion`, `LegalMoveSource`, `RibbonApplicator.SetAllValidRibbons`, `SetDefaultNickname(la)` | ❌ | — | — | **OUT OF SCOPE** (§5) |

---

## 3. Top 10 recommended next features

### 3.1 — Trainer identity editor (P1, low risk) 🥇
**API:** `sav.OT`, `sav.TrainerTID7`/`TrainerSID7` (or `TID16`/`SID16`), `sav.Gender`, `sav.Language`,
plus `sav.MaxStringLengthTrainer` for the name-length bound.
**Why safest:** verified that *every* mainline save class — `SAV1`, `SAV2`, `SAV3`, `SAV4`, `SAV5`,
`SAV6`, `SAV7`, `SAV7b`, `SAV8BS`, `SAV8LA`, `SAV8SWSH`, `SAV9SV`, `SAV9ZA` — overrides
`OT` (the base `SaveFile.cs:130` default is only a fallback). No no-op risk.
**Pitfalls:** use `TrainerIDDisplayFormat` (`SaveFile.cs:142`) to decide whether to show the
6-digit Gen7+ `TrainerTID7` or the 5-digit `TID16` — showing the wrong one is confusing but
harmless. Clamp OT name to `MaxStringLengthTrainer`. Needs a **new page** (this is save-level,
not per-mon) — suggest a "Trainer" card on `MainPage`.
**Caveat:** `SAV3RSBox` (box-only GC dump, present in this project's save inventory) does not
override these — gate the UI on a capability probe, not on `Generation`.

### 3.2 — Money + play time (P1, low risk)
**API:** `sav.Money` (bound with `sav.MaxMoney`, `SaveFile.cs:139`), `sav.PlayedHours/Minutes/Seconds`.
Same override-coverage verification as 3.1 — uniform across all mainline saves. Ship it on the
same Trainer page. Clamp minutes/seconds to 0-59 in the UI; the library does not.

### 3.3 — Held item **editing** (P1, low risk, high visible value)
**API:** `pk.ApplyHeldItem(itemId, pk.Context)` (`Editing/CommonEdits.cs:286`) — *not* raw
`pk.HeldItem = id`. It runs `ItemConverter.GetItemForFormat` and bounds-checks against
`pk.MaxItemID`, zeroing anything out of range.
**Generation reality (verified):**
- **Gen1 — hard no-op.** `PK1.cs:157`: `HeldItem { get => 0; set { } }`. RBY has no held items;
  that byte is the **catch rate** (`PK1.cs:63`, and there's a whole `CatchRateApplicator` for it).
  → disable the picker for Gen1 with an inline reason. Exactly the Nature/Ability precedent.
- **Gen2+ — real.** `PK2.cs:60` stores it. Note `PK2.cs:59` `SpriteItem` routes through
  `ItemConverter.GetItemFuture2` — Gen2 item IDs are a **different ID space**, so the existing
  sprite/name lookup must use `SpriteItem`, not `HeldItem`, or Gen2 shows the wrong icon.
**UI:** populate from `GameInfo` item strings, bounded by `pk.MaxItemID`. The display half
(sprite + name) already exists — this is purely making it writable.

### 3.4 — Fix EV caps: adopt `GetMaximumEV` (P1, low risk, correctness)
See §4.1 — this is a genuine defect, a de-branching win, and cheap. Do it before anything
else touches the stats form.

### 3.5 — Box rename (P1, low risk)
**API:** `if (sav is IBoxDetailName n) n.SetBoxName(box, text);`
The app *already reads* names via `IBoxDetailNameRead` in `BoxListPage`, so this is the
matching setter — half-done already.
**Coverage (verified):** implemented by `SAV1/2/3/3RSBox/3Colo/3XD/4BR/4HGSS/4Sinnoh/5/6AO/6XY/7/8BS/8LA/8SWSH/9SV/9ZA`.
**`SAV7b` (Let's Go) does *not* implement it** — and this project's save inventory contains a
`SAV7b` file, so this path *will* be hit. The `is IBoxDetailName` check is mandatory, not
defensive boilerplate. Bound input with `sav.MaxStringLengthTrainer` and note that
`GetDefaultBoxName` is only a display fallback — writing it back would persist "Box 3" as a
literal name.

### 3.6 — Friendship (P2)
**API:** `pk.CurrentFriendship` (0-255), `pk.OriginalTrainerFriendship`; `pk.MaximizeFriendship()`
exists as a one-tap convenience (`CommonEdits.cs:398`).
**Generation reality:** **Gen1 no-op** (`PK1.cs:155` `get => 0; set { }` — RBY has no happiness).
Gen2 real (`PK2.cs:81`). **Gen3 aliases the two properties together** (`G3PKM.cs:39`:
`CurrentFriendship => OriginalTrainerFriendship`) — so exposing both as separate fields on Gen3
lets a user edit one and watch the other change. Show a single field pre-Gen6, or accept the
linkage the way Gen1/2's SpA/SpD linkage is already handled.

### 3.7 — Ball (P2)
**API:** `pk.Ball`, bounded by `pk.MaxBallID`.
**Generation reality:** **Gen1/2 no-op** (`GBPKM.cs:135` `Ball { get => 0; set { } }`).
Gen3+ real. Gen3 packs it into a bitfield (`PK3.cs:136`) and **Gen4 stores it in two different
bytes** depending on DPPt vs HGSS (`PK4.cs:269`/`:273`) — but `G4PKM.cs:275` already wraps that
behind the plain `Ball` property, so **use `pk.Ball` and the split is handled for you**. Do not
touch `BallDPPt`/`BallHGSS` directly.

### 3.8 — `.pk` single-entity export (P2, low risk) — then import (P2, medium)
**Export** is the easy half and a genuine PKHeX-desktop staple:
`EntityFileNamer.GetName(pk)` for the filename + `pk.DecryptedPartyExport` (or
`WriteDecryptedDataStored`) for bytes → existing `FileSaver`. Extension comes from
`sav.PKMExtensions` / `EntityFileExtension`. Uniform across gens.
**Import** needs `EntityFormat.GetFromBytes(bytes)` (`PKM/Util/EntityFormat.cs:159`) then, if the
file's format differs from the save's, `EntityConverter.ConvertToType(pk, sav.PKMType, out var result)`
— **check `result`**, it reports incompatibility rather than throwing. Guard with
`EntityConverter.IsCompatibleWithModifications`. Do not silently drop a failed conversion.

### 3.9 — Showdown export (P2, low) — import separately (P3, medium)
**Export:** `new ShowdownSet(pk).Text` (`ShowdownSet.cs:809` ctor, `:427` `Text`). One line,
uniform, immediately useful (share a set to clipboard). Ship this alone first.
**Import:** `new ShowdownSet(text)` then `pk.ApplySetDetails(set)` (`CommonEdits.cs:158`).
Higher risk *not* because of generations but because it writes ~15 fields at once (species,
moves, EVs/IVs, nature, ability, item, level, shininess, friendship, Tera). It will produce
illegal mons freely — same "applied as-is" warning the species/move editor already shows, and it
must trigger the same `ResetPartyStats()` + species-before-level ordering the app already
learned to do. Check `set.InvalidLines` and surface parse errors.

### 3.10 — PP / PP-Ups, and Pokédex *display* (P2, both low)
**PP:** `pk.Move1_PP`…`Move4_PP`, `Move1_PPUps`…, all abstract on `PKM.cs:133-140` → uniform.
The app already calls `SetMoves` (which sets max PP); this exposes manual control. Low value
alone, near-zero cost bolted onto the existing Moves card.
**Pokédex display:** `sav.HasPokeDex`, `GetSeen`/`GetCaught`, `SeenCount`, `CaughtCount`,
`PercentCaught` — **reads are uniform**, correctly routed through each gen's `Zukan` substructure
(`SAV4.cs:471`, `SAV5.cs:115`, `SAV7.cs:178`, `SAV8SWSH.cs:200`, `SAV9SV.cs:205`). A
completion-percentage card is cheap and safe. **Dex *writing* is a different story — see §5.3.**

---

## 4. Cross-cutting findings

### 4.1 — The app hand-rolls IV/EV bounds that PKHeX.Core already provides generically (and gets EVs wrong)

`PokemonDetailPage.xaml.cs:11-13,130-134` computes:
```csharp
isGen12 = p.Generation is 1 or 2;
ivMax = isGen12 ? 15 : 31;
evMax = isGen12 ? 65535 : 252;
```
PKHeX.Core exposes both as abstract members already — `PKM.cs:298-299` — with these
verified per-format values:

| Format | `pk.MaxIV` | `pk.MaxEV` | App's `evMax` | Match? |
|---|---|---|---|---|
| Gen1/2 (`GBPKM.cs:18-19`) | 15 | 65535 | 65535 | ✅ |
| Gen3 (`G3PKM.cs:23-24`) | 31 | **255** | 252 | ❌ |
| Gen4 (`G4PKM.cs:24-25`) | 31 | **255** | 252 | ❌ |
| Gen5 (`PK5.cs:306-307`) | 31 | **255** | 252 | ❌ |
| Gen6/7 (`G6PKM.cs:125-126`) | 31 | 252 | 252 | ✅ |
| Gen8/9 (`G8PKM.cs:54-55`, `PK9.cs:72-73`) | 31 | 252 | 252 | ✅ |

**Two distinct problems, one fix:**

1. **Under-cap on Gen3/4/5.** Those formats store EVs up to 255 per stat; the app's hardcoded
   252 blocks legitimate 253-255 values that exist in real Gen3-5 saves. Same failure shape as
   the already-fixed Gen1/2 `byte.TryParse("65535")` bug — an over-narrow cap rejecting real data.
2. **No 510-total budget enforcement at all.** PKHeX's own editor uses
   `pk.GetMaximumEV(index)` (`CommonEdits.cs:329`), which returns
   `Clamp(510 - (EVTotal - thisEV), 0, 252)` for Gen3+ and `65535` for Gen1/2. The app lets a
   user set all six EVs to 252 (total 1512), silently producing an illegal mon with no feedback.

**Recommended fix:** replace both hardcoded ternaries with `pk.MaxIV` / `pk.GetMaximumEV(i)`.
This *removes* a per-generation branch rather than adding one — squarely on this project's
"fully generic, no per-generation branching" principle — and adds the missing budget constraint
for free.

**Scope limit (important):** `MaxIV`/`MaxEV` replace only the **numeric bounds**. The Gen1/2
*structural* behavior — HP IV derived from the other four DVs' low bits, SpD linked to SpA —
is separate and has **no generic library handle**. I checked `ISeparateIVs`, the obvious
candidate: it is implemented **only by `CK3`/`XK3`** (Colosseum/XD), not by `GBPKM`. So the
app's existing field-disabling logic for Gen1/2 is correct and must stay hand-rolled. Do not
let this refactor delete it.

### 4.2 — Latent safety gap in already-shipped code: locked box slots

`SaveFile` exposes `GetBoxSlotFlags`, `IsBoxSlotLocked`, `IsBoxSlotOverwriteProtected`
(`SaveFile.cs:483-488`) to mark slots that must not be written — battle-box / daycare /
in-transit entries on later gens. `PokemonSlotMover.cs` calls none of them (confirmed: the
app's entire `sav.` surface is `BlankPKM`, `BoxCount`, `BoxSlotCount`, `DeletePartySlot`,
`Get/SetBoxSlotAtIndex`, `Get/SetPartySlotAtIndex`, `OT`, `PartyCount`, `PartyData`, `Write`).
One-line guard in `MoveOrSwap`; flagged here for completeness since it touches shipped
write-path code, but it is a hardening item, not a known-reproduced corruption.

---

## 5. Traps and out-of-scope — name these, don't build them

### 5.1 — Auto-legalization: **OUT OF SCOPE** (repeatedly ruled out by the user)
`EncounterSuggestion`, `LegalMoveSource`/`LegalMoveComboSource`, `EntitySuggestionUtil`,
`RibbonApplicator.SetAllValidRibbons`, `pk.SetDefaultNickname(la)`, and the
`Editing/Bulk/Entity/Suggestion/` tree all exist and are tempting. The app's stance is
read-only legality reporting + "edits applied as-is." **Do not wire any of these.** Note that
`RibbonApplicator`'s only bulk entry points *take a `LegalityAnalysis`* — meaning ribbons
cannot be done "the easy way" without importing the auto-fix stance.

### 5.2 — Fields that look editable but are derived (the no-op trap)
Verified, do **not** ship these as plain enabled controls:

| Field | Gen1/2 | Gen3 | Gen4 | Gen5+ |
|---|---|---|---|---|
| **Gender** | derived from IVs, no setter (`GBPKM.cs:106-123`) | **no-op**, PID-derived (`G3PKM.cs:37`) | **real** (`PK4.cs:170`) | **real** (`PK5.cs:192`) |
| Nature | no concept | no-op (PID) | **no-op** | real *(already shipped)* |
| Ability | no concept | no-op (PID) | real | real *(already shipped)* |
| HeldItem | Gen1 no-op / Gen2 real | real | real | real |
| Ball | no-op | real | real | real |
| Friendship | Gen1 no-op / Gen2 real | real (aliased) | real | real |

Gender follows the **exact same shape** as the already-solved Nature/Ability case — Gen1/2/3
non-storing, Gen4+ real. It is a legitimate feature, but it is **not** the "quick win" it
looks like; it needs the disable-and-explain treatment from day one.

### 5.3 — Pokédex *writing* is a per-gen minefield (reads are not)
`SaveFile.cs:375-378` declares `SetSeen`/`SetCaught` as `virtual { }` — **silent no-ops by
default**. They are overridden **only** by `SAV1`, `SAV2`, `SAV3`, `SAV6AO`, `SAV6XY`.
Gen4/5/7/8/9 override only the *getters* plus a `protected SetDex(PKM)`, routing writes through
per-gen `Zukan4`/`Zukan5`/`Zukan7`/`Zukan8`/`Zukan9` substructures with different shapes.
So `sav.SetCaught(species, true)` **compiles and does nothing** on a Scarlet save — a textbook
instance of this project's signature bug class. Ship dex *display* (§3.10); treat dex *editing*
as P4 requiring explicit per-gen work.

### 5.4 — Shininess is not a stored boolean
`pk.IsShiny` is derived from PID/DVs. `CommonEdits.cs:99-138` (`SetIsShiny`/`SetShiny`/`SetUnshiny`)
works by **mutating the PID**, which changes Gen3/4 Nature, Gender and ability slot as
side-effects (they're all PID-derived — see §5.2). This is precisely why the project already
declined `SetPIDNature` for Gen3/4 Nature. A "shiny" toggle is therefore a **substantial,
careful feature**, not a quick win; if built, it must warn that PID-derived fields will move.

### 5.5 — Single-generation interface sprawl (row 28)
Hyper Training, AVs, Dynamax, Gigantamax, Tera type, scale/height/weight, Alpha/Noble,
Ganbaru, memories, Super Training, contest stats, `ITechRecord`, `IMoveShop8`, HOME tracker,
geo/region origin. Each is real, each is **one or two generations only by construction**, and
each costs its own `is IFoo` probe + disabled state on every other generation. Individually
low value on mobile; collectively a large surface. Recommend deferring the whole cluster until
the P1/P2 list is done, then picking only those tied to a generation the user actually plays.

---

## 6. Suggested swarm slicing

Independent, no file conflicts between lanes except where noted.

| Lane | Items | Touches |
|---|---|---|
| **A — Trainer/save-level** | §3.1 trainer identity, §3.2 money/playtime | new page + `MainPage.xaml[.cs]` |
| **B — Per-mon quick wins** | §3.3 held item, §3.6 friendship, §3.7 ball | `PokemonDetailPage.xaml[.cs]` ⚠ shared with C |
| **C — Stats correctness** | §4.1 `MaxIV`/`GetMaximumEV` refactor | `PokemonDetailPage.xaml.cs` ⚠ shared with B — **land C first, it's small** |
| **D — File I/O** | §3.8 `.pk` export then import, §3.9 Showdown export | new helper + detail-page buttons |
| **E — Box-level** | §3.5 box rename, §4.2 locked-slot guard, box sort/clear | `BoxListPage`, `PokemonSlotMover.cs` |
| **F — Read-only cards** | §3.10 Pokédex display, PP fields | detail page + main page |

**Sequencing note:** lanes B and C both edit `PokemonDetailPage.xaml.cs`. C is a ~10-line
change — merge it first, then B rebases onto it.

**Every lane must:** keep the "no per-generation branching where the library provides a generic
answer" principle; use the disable-but-show-true-value + inline-reason pattern for SPLIT rows;
and verify against real saves with a `verify/<Feature>/Program.cs` harness before claiming done,
per this project's established convention.
