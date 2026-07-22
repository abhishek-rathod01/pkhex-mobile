# CAPABILITY-GAPS.md

**Date:** 2026-07-22 · **Type:** read-only audit (this file is the only thing written).
**Two parts:** (1) documentation-accuracy discrepancies found against the live repo, then
(2) a priority-ordered map of PKHeX.Core capability the app still does not expose — including
gaps the existing `CAPABILITY-AUDIT.md` missed or under-scoped.

Method: the app's real surface was extracted by grepping every `PkhexMobile/*.cs` for `pk.`/
`sav.` member accesses (not inferred from prose), then cross-referenced against `vendor/`.
Sprite/item counts were taken by counting files on disk, not re-quoting prose.

---

## Part 1 — Documentation discrepancies

### 1.0 — Verdict on the three named target docs

- **`PROGRESS.md`** — accurate and current; carries its own explicit "superseded" notes on
  older entries. No discrepancies found.
- **`WAKEUP.md`** — accurate; "working tree clean / 0 errors" confirmed live, remaining-gaps
  list matches the code. No discrepancies found.
- **`CLAUDE.md`** — accurate. It's the oldest doc (`395577b`) and not part of this session's
  `df2f40f` doc-refresh, so its `file:line` vendor citations were spot-checked: `PK1.cs:155`
  (Gen1 friendship no-op) ✓, `PK1.cs:157` (Gen1 held-item = catch-rate byte) ✓,
  `GBPKM.cs:185-191` (Gen1/2 IV derivation) ✓, `SaveFile.cs:316-323` (`SetPartyValues`
  `PartyStatsPresent` gate) ✓ — all still correct.

The one substantially out-of-date doc is `CAPABILITY-AUDIT.md`, which the task named as a
Part 2 input rather than an audit target, but which `CLAUDE.md` points readers to as the
current capability map:

### 1.1 — `CAPABILITY-AUDIT.md` is now out of date (it's a pre-build planning doc)

`CAPABILITY-AUDIT.md` was committed in `3b75c12` at **2026-07-22 01:12** — *before* the P1/P2
build-out it recommends (`12aad99` Trainer 01:xx, `ec5cdc9` box mgmt, `a5c2ce3` transfer,
`5481cc1` EV caps, `aa5d047` nav, `df2f40f`, all later the same day). So it is a **planning
snapshot the features were built from**, not a careless stale doc — but its §1 "Already
integrated" table and §2 "Integrated?" column were never refreshed afterward, so a reader
trusting it *today* would re-implement ~11 already-shipped features. The following rows are
marked `❌ not
integrated` (or "display only" / "hardcoded") in §2 but are **now fully implemented**:

| Audit row | Audit says | Actual state (file) |
|---|---|---|
| #1 Trainer identity | ❌ (`sav.OT` read once) | Shipped — `TrainerInfoPage.xaml.cs` (`OT`, `DisplayTID`/`DisplaySID`, `Gender`, `Language`) |
| #2 Money / play time | ❌ | Shipped — `TrainerInfoPage` (`Money`/`MaxMoney`, `PlayedHours/Minutes/Seconds`) |
| #3 Held-item **editing** | ❌ display only | Shipped — `PokemonDetailPage` (`pk.ApplyHeldItem`) |
| #4 EV caps / 510 budget | ❌ hardcoded | Shipped — `pk.MaxIV`/`MaxEV`, advisory 510 caption (`5481cc1`) |
| #5 Box rename | ❌ read only | Shipped — `BoxManagement.cs` (`SetBoxName`) |
| #8 `.pk` export | ❌ | Shipped — `EntityTransferService.cs` (`WriteDecryptedDataStored`, `EntityFileNamer`) |
| #9 `.pk` import | ❌ | Shipped — `EntityTransferService` (`EntityFormat.GetFromBytes`, `EntityConverter.ConvertToType`) |
| #10 Showdown export | ❌ | Shipped — `EntityTransferService` (`new ShowdownSet(pk).Text`) |
| #12 Pokédex **display** | ❌ | Shipped — `TrainerInfoPage` (`HasPokeDex`, `SeenCount`, `CaughtCount`, `PercentCaught`) |
| #13 Showdown import | ❌ | Shipped — `EntityTransferService` (`pk.ApplySetDetails`) |
| #19 Box sort / clear / compress | ❌ | Shipped — `BoxManagement.cs` (`SortBoxes`, `ClearBoxes`, `CompressStorage`) |

Correspondingly, audit §3.1–3.5 and §3.8–3.10 ("Top 10 recommended next features") and the
§4.1 EV-cap fix are all **done**. **Recommendation:** refresh the audit's §1 table and §2
"Integrated?" column, or add a banner that it is a superseded snapshot — `CLAUDE.md` still
points to it as "the prioritized map of what PKHeX.Core offers vs. what the app exposes," so a
reader trusting it today would re-implement ~11 already-shipped features.

### 1.2 — Audit §4.2 locked-slot guard is only *partially* closed

§4.2 flagged that `PokemonSlotMover.cs` writes box/party slots without checking
`IsBoxSlotLocked`/`IsAnySlotLockedInBox`. `BoxManagement.cs:191,238` now guards its **bulk**
sort/clear ops with `sav.IsAnySlotLockedInBox`, but the **per-slot move/swap** path the audit
specifically named — `PokemonSlotMover.MoveOrSwap` — still has **no lock guard** (confirmed:
grep finds zero `Locked`/`SlotFlags` references in `PokemonSlotMover.cs`). The exact concern
the audit raised remains open for the exact code it named. Small one-line hardening; see
Part 2 §8.

### 1.3 — Items that are actually accurate (verified, no change needed)

- **Sprite/item counts are now correct.** Species = 1025 regular + 1025 shiny = 2050 files on
  disk; items = 1149 files. These match the corrected figures in `PROGRESS.md`/`WAKEUP.md`
  (the earlier "#905 ceiling" and "933/2683" figures were already fixed this session via
  `200afee`/`df2f40f`).
- **All commit hashes referenced in the docs exist and describe what they claim** — spot-checked
  `6dacc46`, `66f3bb8`, `200afee`, `db049cc`, `a7aacb4`, `df48f97`, `f4d88e8`, `3b75c12`,
  `aa5d047`, `df2f40f`.
- **`WAKEUP.md`'s "working tree is clean" is true** — `git status --porcelain` returns nothing
  right now. (The environment's start-of-conversation `gitStatus` snapshot showed ~40 modified
  files, but that snapshot is stale; the live tree is clean. Not a doc defect.)
- `PROGRESS.md` is internally consistent and carries explicit "superseded" notes on its own
  older entries; `WAKEUP.md`'s remaining-gaps list (met/origin, egg, markings, bag, PP/PP-Ups,
  contest, ribbons, bulk, event flags, Mystery Gift, QR) matches the code.

### 1.4 — One under-scoping in the audit's own gap analysis

`CAPABILITY-AUDIT.md` never lists **Pokémon-level Gender editing** as a candidate feature in
its master §2 table — it appears only in §5.2 as a "looks-editable-but-derived" trap. Now that
Nature/Ability editing has shipped (same disable-and-explain shape), Gender is the natural next
same-shape feature and deserves a real row. Carried into Part 2 §3 below.

---

## Part 2 — Capability gaps (priority-ordered: value vs. effort)

"Safe to expose" = read-only, like the legality badge. "Applied-as-is" = editable field that
needs this project's standing warning + read-only `LegalityAnalysis` treatment. "SPLIT" = needs
the disable-control-but-show-true-value + inline-reason pattern on generations that no-op.

### Tier A — high value, low effort

**1. Friendship editing** *(audit #6, still open)*
- API: `pk.CurrentFriendship` (0-255), `pk.OriginalTrainerFriendship`, one-tap `pk.MaximizeFriendship()` (`CommonEdits.cs:398`).
- Write. **SPLIT:** Gen1 no-op (`PK1.cs:155`); Gen2 real (`PK2.cs:81`); Gen3 aliases both properties (`G3PKM.cs:39`) so show one field pre-Gen6. Gen4+ two independent fields.
- Size: **small.** Bolts onto the detail page. Applied-as-is (rarely legality-flagged, but keep the banner).

**2. Ball editing** *(audit #7, still open)*
- API: `pk.Ball`, bounded by `pk.MaxBallID`. Use `pk.Ball` directly — `G4PKM.cs:275` already hides the DPPt/HGSS two-byte split.
- Write. **SPLIT:** Gen1/2 no-op (`GBPKM.cs:135`); Gen3+ real.
- Size: **small.** Applied-as-is.

**3. Pokémon-level Gender editing** *(audit UNDER-SCOPED — §5.2 trap only)*
- API: `pk.Gender` (real settable byte from Gen4: `PK4.cs:170`, `PK5.cs:192`).
- Write. **SPLIT:** Gen1/2 derived from IVs (`GBPKM.cs:106-123`), Gen3 PID no-op (`G3PKM.cs:37`), Gen4+ real — identical shape to the already-shipped Nature/Ability pickers.
- Size: **small–medium.** Applied-as-is (a gender that contradicts the species gender ratio / PID will be flagged by the legality badge, which is the intended behavior).

**4. Manual PP / PP-Ups editing** *(audit #11, still open)*
- API: `pk.Move1_PP…Move4_PP`, `pk.Move1_PPUps…` (abstract `PKM.cs:133-140`). The app only calls `SetMoves` (auto-max PP) today; this exposes manual control.
- Write. **UNIFORM.** Size: **small**, attaches to the existing Moves card. Applied-as-is.

**5. Computed final stats display** *(NOT in audit — new)*
- API: `pk.Stat_HPMax`/`Stat_ATK`/`Stat_DEF`/`Stat_SPA`/`Stat_SPD`/`Stat_SPE` (abstract `PKM.cs:155-`), populated by `ResetPartyStats()` for party mons; compute via `PersonalInfo` for box mons.
- **Read-only-safe.** Size: **small.** Value: the app currently shows the *inputs* (IV/EV/level/nature) but never the resulting battle numbers those produce — a natural, safe read-only card.

**6. Species type chip(s) display** *(NOT in audit — new)*
- API: `IPersonalType.Type1`/`Type2` (`PersonalInfo/Interfaces/IPersonalType.cs:11,16`) on the mon's `PersonalInfo`. Reuse the existing type-badge component already built for the move-type chips (`3b75c12`/`795a1d5`).
- **Read-only-safe.** Size: **small** (component already exists).

**7. Hidden Power type / power readout** *(NOT in audit — new)*
- API: `HiddenPower.GetType(IVs, ctx)` / `GetPower` (`Editing/HiddenPower.cs:16`). Derived from IVs; relevant Gen2-7. `HiddenPowerApplicator` exists if editing is later wanted (pre-Gen8 only).
- **Read-only-safe** as a display. Size: **small.**

**8. Locked-slot guard in `PokemonSlotMover.MoveOrSwap`** *(audit §4.2, still open)*
- API: `sav.IsBoxSlotLocked` / `GetBoxSlotFlags` / `IsBoxSlotOverwriteProtected` (`SaveFile.cs:483-488`).
- Write-path **hardening**, not a user feature. Size: **small** (one guard). Prevents overwriting battle-box/daycare/in-transit slots on later gens. `BoxManagement`'s bulk ops already do this; the move path does not.

### Tier B — medium value / effort

**9. Origin / met data** *(audit #14)*
- API: `pk.Version`, `MetLocation`, `MetLevel`, `MetYear/Month/Day`, `EggLocation`.
- Read + write. **SPLIT:** met dates are `virtual { get => 0; set { } }` pre-Gen4 (`PKM.cs:185-187`), real Gen4+. Size: **medium.** Applied-as-is (heavily legality-coupled).

**10. Egg status / hatch** *(audit #15)*
- API: `pk.IsEgg` (currently only *read* in `BoxManagement.cs:298`'s backup snapshot), `ForceHatchPKM`, `SetEggMetData`.
- Write. **SPLIT:** no eggs in Gen1. Size: **medium.**

**11. Bag / inventory editing** *(audit #17)*
- API: `sav.Inventory` → `PlayerBag.Pouches` → `InventoryPouch`, `CopyTo(sav)`.
- Read + write. Base returns `EmptyPlayerBag` (graceful). Size: **medium–high.** Mostly uniform; item ID spaces differ per gen (reuse the item-sprite keying already solved).

**12. Markings** *(audit #18)*
- API: `IAppliedMarkings<bool>` / `<MarkingColor>`, `ToggleMarking`.
- Read + write. **SPLIT:** generic param differs Gen3-6 (bool) vs Gen7+ (color); none in Gen1/2. Size: **medium.** Cosmetic, low legality risk.

**13. Pokérus** *(audit #22)*
- API: `pk.PokerusStrain`, `PokerusDays`, `Editing/Pokerus.cs`.
- Write. **SPLIT:** absent Gen1. Size: **small–medium.**

**14. Characteristic string** *(NOT in audit — new, trivial)*
- API: `EntityCharacteristic.GetCharacteristic(maxStatIndex, maxStatValue)` (`PKM/Util/EntityCharacteristic.cs:16`) → the "Takes plenty of siestas"-style flavor line, derived from IVs/EC. Gen3+.
- **Read-only-safe.** Size: **tiny.** Nice pairing with the stats card (§5).

**15. Box wallpaper / current box** *(audit #20)*
- API: `IBoxDetailWallpaper`, `sav.CurrentBox` (currently *read* only), `BoxesUnlocked` (read).
- Write. **SPLIT:** interface check. Size: **small–medium.** Cosmetic.

### Tier C — low priority (large, niche, or scope-constrained)

**16. Contest stats** *(audit #23)* — `IContestStats`. Gen3-6 only. Medium. Applied-as-is.

**17. Ribbons** *(audit #24)* — `IRibbonIndex`, ~130 props. **Constraint:** `RibbonApplicator`'s
only bulk entry points take a `LegalityAnalysis`, i.e. you cannot do ribbons "the easy way"
without importing the auto-fix stance this project rules out. Manual per-ribbon toggles are
possible but High effort. Applied-as-is.

**18. Bulk / batch edit** *(audit #25)* — `EntityBatchEditor`, `StringInstruction`. Reflection-based; needs a query UI. High.

**19. Event flags / work** *(audit #26)* — `sav.GetFlag/SetFlag`, `EventWorkspace`, per-game label files. High.

**20. Mystery Gift** *(audit #27)* — `MysteryGift`, `WC6/7/8/9`, `PCD`/`PGF`. Per-gen card classes, no common editor. High.

**21. Single-generation interface cluster** *(audit #28)* — each is one/two gens by construction,
each needs its own `is IFoo` probe + disabled state elsewhere: Hyper Training (`IHyperTrain`),
Tera type (`ITeraType`, Gen9), height/weight/scale (`IScaledSize`), Dynamax/Gigantamax (Gen8),
AVs (`IAwakened`, LGPE), OT/HT **Memories** (`IMemoryOT/HT`, Gen6+), Super Training,
`ITechRecord`/`IMoveShop8`, HOME tracker (`IHomeTrack`), Alpha/Noble (LA). Medium *each*,
collectively large. Defer until a specific generation the user plays justifies one.

**22. QR import/export** *(audit #29)* — `QRMessageUtil`, `QRPK7`. Gen7-centric; needs a camera/encoder dependency. Medium.

### Explicitly OUT OF SCOPE (do not build — standing decisions)

- **Auto-legalization / suggestions** (audit #30, §5.1) — repeated user instruction. `EncounterSuggestion`, `LegalMoveSource`, `RibbonApplicator.SetAllValidRibbons`, `SetDefaultNickname(la)`.
- **Pokédex *writing*** (§5.3) — `SetSeen`/`SetCaught` are `virtual { }` no-ops overridden only by SAV1/2/3/6AO/6XY; Gen4/5/7/8/9 route through per-gen `Zukan` substructures. Textbook silent-no-op minefield. Dex *display* already shipped; dex *editing* would need explicit per-gen work.
- **Shiny toggle** (§5.4) — `pk.IsShiny` is PID-derived; `SetShiny` mutates the PID, moving Gen3/4 Nature/Gender/ability as side effects. Same reason `SetPIDNature` was already declined. Not a quick win.
