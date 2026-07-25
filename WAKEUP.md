# WAKEUP — read this first

## DECISIONS ONLY YOU CAN MAKE (flagged, not acted on)

1. **Merge `3d-models-experimental` -> `master`?** Recommend AGAINST merging the assets in as-is
   (+214MB `.glb` bundle). See item 4 below - fetch-on-demand + disk cache is the better shape for
   this, and would mean the branch's *code* merges but not its *asset files*. Still your call.
2. **The "optimised vs full-scale model" toggle you asked for is not buildable as scoped.**
   Checked the upstream asset source directly: it only publishes one (already-optimized) variant
   per species - there is no separate full-scale/high-poly source to toggle to. If you want this,
   it would mean sourcing full-scale models from a different pipeline entirely, not a toggle over
   what's already fetched.
3. **On "it keeps crashing" - corrected, previous note was premature.** Two theories were checked
   and neither holds up as *the* explanation:
   - `Model3DViewerPage`'s `HybridWebView` crash (`PlatformView cannot be null here`,
     `HybridWebViewHandler.MapEvaluateJavaScriptAsync`) only exists on `3d-models-experimental`,
     confirmed by grep - `master` has no `HybridWebView`/3D-viewer code at all. A tentative
     mitigation is now on that branch (waits for the native WebView to exist before touching
     `DefaultFile`) but is **not confirmed to fix it** - the exception is rethrown on a posted
     async continuation, which a synchronous guard narrows but can't provably close, and the
     original crash (dated 2026-07-23) was never reproduced to test against. See that branch's
     `PROGRESS.md` for the full writeup.
   - A ~33-39s cold-start delay was measured on that branch and initially looked like strong
     evidence the +214MB/1866-file asset bundle was the cause - **but the identical ~33s delay
     was then measured on `master` too**, which has none of those assets. That refutes the
     asset-bundle theory outright; the real driver is Debug-build/emulator JIT+verification
     overhead specific to this x86_64 emulator, present across the whole project, not something
     trimming assets would fix.
   - The one crash-adjacent fix that IS real and verified this pass: `RefreshLegality`
     (`PokemonDetailPage.xaml.cs`) was running full `LegalityAnalysis` encounter-matching
     synchronously on the UI thread on every page load and save - a genuine ANR risk on real
     devices with expensive saves, independent of the emulator. Moved to a background thread
     (`Task.Run`), with a clone-before-backgrounding fix for the resulting data race. This is the
     most concrete, verified crash/freeze-adjacent fix from this pass - see the to-do list.
   - **If you're still seeing crashes**, the honest answer is neither of the above two theories
     explains it, and the emulator's log buffer doesn't show anything else. A repro (which screen,
     which save file, what action) would let this go further; guessing further without one risks
     the "unverified 'fix'" pattern flagged in item 4.
4. **Architecture decision needed: bundle vs. fetch-and-cache for large assets.** Confirmed via a
   Haiku research pass that `PokeAPI/sprites`' `official-artwork` set (CC0-licensed, full 1-1025
   coverage including shiny) would add another **~292MB** if bundled - on top of the 3D branch's
   214MB, that's ~506MB baked into the APK, which isn't viable. Recommend **fetch-on-demand +
   disk cache** for both the hi-res Pokedex artwork and (if you do want it kept) the 3D models:
   download on first view, cache under `FileSystem.CacheDirectory`, fall back to the already-
   bundled small pixel sprite if offline/uncached. This also sidesteps the 3D branch's merge-size
   concern in item 1. Not started - this is a real architecture choice (offline availability vs.
   app size vs. requiring network on first view of each Pokemon) that's yours to make before any
   code gets written against it.

## 2026-07-25 pass: crash re-investigation, 3D branch findings, two Haiku research tasks

- [x] **DONE**: Found and fixed a residual instance of the Nickname/IsNicknamed bug from the
  previous pass (caught by `advisor` review before it shipped further) - the fix compared nickname
  text against the species' default name unconditionally, which silently cleared `IsNicknamed` on
  a mon deliberately nicknamed to match its own species name. Now baseline-gated (species changed,
  or text differs from what was loaded), matching the DatePicker fix's pattern. Regression case
  added to `verify/NicknameLevelFix`, passes. Pushed (`2c46ce9`).
- [x] **DONE**: Moved `RefreshLegality`'s `LegalityAnalysis` construction off the UI thread
  (`Task.Run`) - it was running full encounter-matching synchronously on every page load/save, a
  genuine ANR risk on real devices independent of the emulator. Then caught and fixed a
  cross-thread data race my own fix introduced (was handing the live, mutable `PKM` to the
  background thread - now clones first). Both pushed (`998bad9`). This is the most concrete,
  verified crash/freeze-adjacent fix from this pass.
- [x] **DONE (tentative, not confirmed)**: `Model3DViewerPage` on `3d-models-experimental` now
  waits for the native WebView to exist before setting `DefaultFile`, narrowing the window for a
  real `PlatformView cannot be null here` crash found in logcat. NOT confirmed to fix it - see
  decision item 3 above and that branch's `PROGRESS.md`.
- [x] **DONE**: Investigated and **refuted** the "3D branch's asset bundle causes a slow/ANR'ing
  cold start" theory - `master` (no 3D assets) showed the identical ~33s cold-start delay under
  the same conditions. See decision item 3 above.
- **Researched, not built** (Haiku, read-only, independently sanity-checked): Pokedex hi-res
  artwork sourcing. `PokeAPI/sprites`'s `official-artwork` set is CC0-licensed, confirmed full
  1-1025 coverage including shiny (~150KB avg/image, ~292MB total for the full normal+shiny set).
  Does NOT cover Mega/regional/alternate forms - those would fall back to base-species art or need
  separate sourcing. Deferred pending the bundle-vs-fetch decision (item 4 above) - building this
  as a straight asset bundle would be the wrong call before that's settled.
- **Researched, not built** (Haiku, read-only): Mega Evolution integration into the Pokedex
  evolution-chain display. PKHeX.Core models Megas as a `Form` value on the same species (not a
  separate evolution node) - `ItemStorage9ZA.GetExpectedMegaStoneOrPrimalOrb(species, form)` gives
  the species->Mega-form->required-item mapping, already used elsewhere in this app's Pokedex
  "Forms" card. Scoped as **medium effort**: the evolution-chain walk (`PokedexService.cs`, built
  on `EvolutionTree.Evolves9`) would need Mega nodes manually grafted in and marked "battle-only,"
  and there's currently no form-aware sprite support at all (`SpriteHelper.SpeciesSpriteFile` takes
  no form parameter, and no per-form sprite files are bundled) - would need that built first or
  fall back to the base species sprite for Mega nodes. Deferred - not a crash fix, lower priority
  than the open items above per the user's own "keeps crashing" complaint.

## LIVE TO-DO LIST (2026-07-23 overnight session, unattended - user asked this be kept explicit)

User is asleep and unreachable for permission approvals for the remainder of this session.
Standing rules: **at most one background subagent running at a time, Haiku only**; any subagent
must **commit but NOT push** (a prior subagent's `git push` triggered a permission prompt the user
couldn't answer while asleep - the orchestrator pushes on its behalf afterward instead, same
pattern already used successfully for Track A this session).

- [x] **DONE**: Pokedex "Where to Find" card (species -> encounter locations/games/methods via
  `EncounterMovesetGenerator.GenerateEncounters`, PUBLIC PKHeX.Core API, no network) + Mega
  Evolution trigger items (via `ItemStorage9ZA.GetExpectedMegaStoneOrPrimalOrb`, also PUBLIC) +
  Dex Entries per-game flavor text (bundled from PokeAPI via a Haiku subagent, all 1025 species) +
  a shiny sprite toggle. Full write-up in `PROGRESS.md`'s "Pokedex 'Where to Find'..." section,
  including a **real on-device ANR** found and fixed (the encounter scan was synchronous on the UI
  thread; fixed with `Task.Run` + a loading state + a stale-species guard). Verified on-device
  against Charizard (Megas) and Pikachu (largest encounter set, 161 wild rows alone) with no ANR
  and correct "+N more" capping. Encounter rate/% confirmed NOT available in PKHeX.Core at all -
  stated explicitly in the UI rather than network-fetched or estimated.
- [x] **DONE**: Fetched real `.glb` 3D models (Track C-2) - 933/974 species, +213.9MB, on
  `3d-models-experimental` (NOT master). Two dispatch attempts: the first correctly did nothing
  because its worktree branched from `master` (which lacks this feature's files); redispatched
  after switching the orchestrator's checkout to `3d-models-experimental` first. Independently
  verified before merging (no code/doc files touched, all 933 file sizes spot-checked, a worktree
  branch-name discrepancy resolved via hash-level `git cat-file`/`git merge-base` checks - the
  commits were correct, the branch name the agent reported was not trustworthy at face value).
  **On-device pass with a real model also done**: Charizard's model renders/rotates correctly
  (untextured - a source-repo data-quality issue, not a pipeline bug); 2D fallback re-confirmed
  for a missing species (#521). Full write-up in `PROGRESS.md`'s "Track C-2" section on that
  branch. **Merge to master intentionally left undecided** - the ~214MB size tradeoff deserves a
  deliberate call, not a default merge; flagged for the user's own review.
- [x] **DONE**: Characteristic string (Computed card), Pokerus Strain/Days editing (Main card),
  and Markings editing (new "Markings" card), all on `PokemonDetailPage`. All verified
  library-level and on-device (`gen9_real.sav`). Markings hunt turned up a real on-device
  rendering bug (not data loss) - see `PROGRESS.md`'s "Markings editing" section for the
  Unicode-emoji-presentation root cause and fix.
- [x] **Investigated (not fixed)**: the off-colour/untextured 3D model appearance, per explicit
  user request. Confirmed no separate full-scale model source exists at the assets repo; confirmed
  the .glb files DO have real embedded WebP-textured materials; root cause narrowed to a plausible
  but NOT conclusively confirmed `EXT_texture_webp` handling gap in the vendored model-viewer
  build - needs a JS console listener wired into `HybridWebView` (doesn't exist yet) to actually
  see the in-page error before attempting a fix. See `PROGRESS.md`'s "Texture investigation"
  section on the `3d-models-experimental` branch for the full evidence trail.
- [x] **DONE**: Combined-field regression test (`verify/CombinedFieldSave`) - stacks Gender +
  Pokerus + Markings + PP/PP-Ups + an IV change onto one mon in one save call across Gen1/2/3/5/9,
  since every field above had only ever been verified in isolation. All pass; no cross-feature
  regression found. Also re-confirmed `ci.yml` only wires up the 9 self-contained Gen1-9
  harnesses - none of this session's 6 hardcoded-local-path harnesses (GenderPPEdit,
  EncounterLocationData, CharacteristicDisplay, PokerusEdit, MarkingsEdit, CombinedFieldSave) can
  break CI.
- [x] **DONE**: Origin/Met data editing (Version, Met Location/Level/Date, Egg Location/Date) - new
  "Origin" card on `PokemonDetailPage`. Location pickers sourced from PKHeX.Core's own
  `GameInfo.GetLocationList` (real location names, not raw IDs); dates use a native MAUI
  `DatePicker`. Verified library-level (`verify/OriginMetDataEdit`, Gen1-5/9) AND on-device against
  `gen9_real.sav` (real edit through the real picker/date-dialog UI, saved, pulled back, confirmed
  via a standalone check). Full write-up in PROGRESS.md.
- [x] **DONE**: Is Egg toggle (plain `Switch` at the top of the Origin card, direct `pk.IsEgg =
  value` write - deliberately NOT the auto-suggesting `ForceHatchPKM`/`SetEggMetData` helpers, per
  the standing no-auto-fix rule). Verified library-level (`verify/IsEggEdit`, Gen1-5/9, including
  Gen3's real nickname-forcing side effect) and on-device toggle confirmed against `gen9_real.sav`.
  This closes out CAPABILITY-GAPS.md Tier B #10 (egg status/hatch) together with the Egg
  Location/Date fields already added in the Origin card above.
- [x] **DONE (found + fixed a real bug)**: the Origin card's Met/Egg `DatePicker`s were silently
  corrupting an UNTOUCHED mon's unset date (`0/0/0` -> `EggMonth/EggDay = 1/1`) on every single
  save, because `SafeDate`'s display clamp got written straight back unconditionally. Caught by a
  new `verify/OriginMetDataEdit` regression case (load a real save, touch nothing, run the exact
  save path, assert the date is still `0/0/0` - failed pre-fix, passes post-fix). Fixed with a
  load-time baseline comparison (only decompose+write if the picker's value actually moved).
  **Not yet re-confirmed on-device** - the emulator became slow/unresponsive to basic `adb`
  commands partway through this check; a next session should redo the on-device pass once it's in
  a clean state. The library-level fix is conclusive on its own (see PROGRESS.md).
- [x] **DONE (revisited, correctly stayed read-only)**: Box wallpaper. `BoxListPage` already had a
  read-only Wallpaper row from an earlier pass, with a well-reasoned decision not to make it
  editable (PKHeX.Core has no per-format wallpaper count/max anywhere, so a write can't be
  bound-checked - confirmed this reasoning still holds, did not reopen it). Added the one thing
  that WAS missing: `BoxManagement.GetBoxWallpaperName` resolves the raw index to a real name
  ("FOREST (#0)") when it falls within the known 32-entry list, confirmed against real Gen3/4/5/9
  saves. Box wallpaper editing itself (Tier B #15) is now considered closed - not a gap, a
  documented library limitation.
- [x] **DONE (found + fixed TWO real bugs, more severe than the DatePicker one)**: dispatched a
  read-only, no-commit Haiku subagent to audit `PokemonDetailPage.xaml.cs` for the same bug class.
  Both findings independently re-verified against real saves before trusting them (per this
  project's own standing rule about subagent self-reports), and both were real:
  1. **Nickname/IsNicknamed forced true on every single save, for every mon.** A real
     non-nicknamed mon's stored `Nickname` already equals its species' default name (confirmed:
     every party mon in `gen9_real.sav`, most of Gen1 RBY's and Gen3 Emerald's) - so the old
     unconditional `pk.IsNicknamed = true` silently promoted every one of those mons to
     "explicitly nicknamed" on the very next save, whatever field was actually edited. Fixed via
     PKHeX.Core's own `SpeciesName.IsNicknamed`/`SetNickname`/`ClearNickname` split.
  2. **CurrentLevel discarding EXP overshoot on every single save.** `pk.CurrentLevel = level` ran
     unconditionally; its setter snaps EXP to the exact level threshold, discarding any real
     "progress toward next level" (invisible on this project's real test saves because they're all
     level 100, where overshoot is impossible - confirmed with a synthetic level-50 mon that lost
     real progress). Fixed by only recomputing EXP when species/form/level actually changed.

  Both affect **every save this app has ever made**, not just Gen4+ mons - more severe than the
  DatePicker bug. Verified via new `verify/NicknameLevelFix` (untouched-save preservation across
  Gen1/3/5/9, genuine-nickname/clear-nickname/species-change edge cases, explicit level changes
  still working correctly) - all pass. Not yet re-confirmed on-device (same emulator-instability
  note as the DatePicker fix - library-level proof is conclusive on its own). **Any save this app
  has ever written may already carry silently-flipped IsNicknamed flags or lost EXP overshoot on
  a below-max-level mon** - there's no way to retroactively detect which files were affected, so
  this is flagged for awareness only, not a "scan and repair" task.
- [ ] Continue `CAPABILITY-GAPS.md` Tier B (remaining): bag/inventory editing (the one sizeable
  item left - medium-high per CAPABILITY-GAPS.md, item ID space differs per gen but the item-sprite
  keying work is already solved elsewhere in this app). Priority order and API citations in
  `CAPABILITY-GAPS.md` Part 2.
- [ ] Then Tier C if time allows: contest stats, ribbons, bulk/batch edit, event flags, Mystery
  Gift, the single-generation interface cluster (Hyper Training/Tera type/size-scale/Dynamax/AVs/
  Memories/Super Training/tech records/HOME tracker/Alpha-Noble), QR import/export.
  `CAPABILITY-GAPS.md` Part 2 has the full list with sizes and per-gen scope - explicitly LOW
  priority, niche, or large; don't front-load these over Tier B.
- [ ] Explicitly OUT OF SCOPE regardless (standing user instruction, unchanged all session):
  auto-legalization/suggestions, Pokédex *writing* (display-only is fine and already shipped),
  shiny *toggle* (PID-derived, same de-shinying-side-effect problem as `SetPIDNature`).
- [ ] Throughout: keep UI consistent with the established design tokens
  (`Resources/Styles/Colors.xaml`/`Tokens.xaml`/`Styles.xaml` - card borders, section titles, label
  captions, button variants) rather than inventing new visual patterns per feature - this was an
  explicit ask this round ("good frontend important, make nice frontend choices").
- [ ] Keep committing incrementally (one feature per commit, as it compiles/verifies) and pushing
  after every commit - not batching everything for one giant end-of-session commit.
- [ ] Before ending the session: update this file's summary section (below) and `PROGRESS.md` one
  final time with a clean combined status, matching the standing pattern already used twice this
  session.

---

**Session 2026-07-23 (overnight, unattended): Pokedex browse UI + experimental
3D viewer, then started on the `CAPABILITY-GAPS.md` Tier A backlog.** Summary,
newest first:

## `master` branch (pushed, clean)

- **Pokedex browse/detail screens** (`PokedexListPage`/`PokedexDetailPage`/
  `PokedexService`) - full National Dex #1-1025 browsing with search/gen
  filter, base stats, abilities, forms (Mega/Gmax/regional), and full
  branching evolution chains with item cross-references, all sourced from
  PKHeX.Core directly (no network dependency). Not tied to a loaded save.
  Reference/item data assembled by a subagent (`Pokedex-manifest.json`,
  `item-info.md`) - see the "Worktree isolation gap" note in `PROGRESS.md`
  if working with parallel subagents again; nothing wrong with the data
  itself, just a process lesson about `git commit` landing on the wrong
  branch under worktree isolation.
- **Gender editing, manual PP/PP-Ups editing, and a read-only "Computed" card**
  (battle stats/type chips/Hidden Power) on `PokemonDetailPage`, plus a
  locked-slot guard in `PokemonSlotMover.MoveOrSwap`. Closes Tier A items
  #3/#4/#5/#6/#7/#8 from `CAPABILITY-GAPS.md`. Full write-up (including a
  real crash found and fixed mid-session - Gen1's raw species-type byte is
  NOT safe to index into the modern type table directly) in `PROGRESS.md`'s
  "Gender editing, manual PP/PP-Ups editing" section. Verified library-level
  (`verify/GenderPPEdit`, new; `verify/BoxPartyMove`, extended with a
  synthetically-locked-slot case) and on-device against `gen9_real.sav`.
- **In progress / next**: continuing through `CAPABILITY-GAPS.md` Tier B -
  see "Next candidates" below for exactly where this session left off.

## `3d-models-experimental` branch (pushed, NOT merged - stays separate)

3D model viewer (`Model3DViewerPage`, "View in 3D" button on the Pokedex
detail screen), `HybridWebView` + the vendored `model-viewer` web component,
2D-sprite fallback when no model is bundled. **No `.glb` files are bundled
yet** - Track C-2 (fetching real models from `github.com/Pokemon-3D-api/
assets`) was deliberately not run this session (see PROGRESS.md's "Not done
in this pass" - judged lower priority than the Tier A gap backlog once the
viewer mechanism itself was proven working). Full write-up, including the
six-attempt debugging history for the per-species parameterization
mechanism, in `PROGRESS.md`'s "3D model viewer" section. Do not merge to
master without first adding real models and running an on-device pass with
at least one real model bundled (only the 2D fallback path has been
verified on-device so far - the actual render-a-real-model path was only
proven with a public-domain test duck, since removed).

---

**Previous session (2026-07-22), in two parts.** Part 1 (below the fold)
closed the previous session's three handoff items. Part 2, at the very top
of this file, ran two tracks in parallel: Track A (a subagent) audited the
docs and mapped remaining PKHeX.Core capability into `CAPABILITY-GAPS.md`;
Track B (this session, main thread) added per-move legality indicators,
did a manual on-device bug hunt, and closed out Ball/Friendship editing.
Working tree is clean, everything pushed to
`github.com/abhishek-rathod01/pkhex-mobile`. `dotnet build` = **0 errors**,
7 warnings (pre-existing `CS8622` nullability on `PokemonTransferPage`
event handlers - cosmetic, unrelated to this session).

> Start here, then read `CLAUDE.md` (build commands, API traps, the
> recurring per-generation no-op bug class, conventions). `CAPABILITY-GAPS.md`
> (new this session) is now the current priority map of unexposed PKHeX.Core
> capability - see "Next candidates" below for its top items.
> **`CAPABILITY-AUDIT.md` is now a stale planning snapshot** (see that
> section below) - don't trust its §1/§2 tables without cross-checking
> `CAPABILITY-GAPS.md` first. `PROGRESS.md` is the long-form feature history.

---

## Part 2 (most recent) — Two-track session: docs/gaps audit + on-device bug hunt

### Track A (subagent, read-only): `CAPABILITY-GAPS.md`

Audited `PROGRESS.md`/`WAKEUP.md`/`CLAUDE.md` against the live repo (all
three accurate - no discrepancies) and found **`CAPABILITY-AUDIT.md` is
stale**: it's a pre-build planning snapshot (committed `3b75c12` at 01:12,
*before* the same-day P1/P2 build-out), and its own §1/§2 tables were never
refreshed - ~11 rows still say `❌ not integrated` for features that
shipped later the same day (Trainer screen, held-item editing, EV caps,
box rename/sort/clear, `.pk`/Showdown import+export, Pokédex display).
`CLAUDE.md` still points readers to it as the current map. **Not fixed
this session** (Track A was read-only by design) - refresh it or add a
superseded banner next time someone's in that area.

Also cataloged every confirmed-unexposed PKHeX.Core capability into
`CAPABILITY-GAPS.md`, priority-ordered - see "Next candidates" below for
the short version.

### Track B (main thread): three pieces of work, all pushed

1. **Per-move legality indicators** - each of the 4 move rows now has a
   pass/fail dot sourced from `LegalityAnalysis.Info.Moves[i]` (the
   library's own per-slot verdict), plus a reason caption for whichever
   move(s) are actually illegal. Verified against a real Gen9 save both
   library-level (`verify/MoveLegalityIndicator`) and on-device
   (`verify/OnDeviceMoveLegality/`).

2. **Manual on-device bug hunt - two real bugs found and fixed:**
   - **Box Management panel content collapsing to zero height.** Opening
     the Boxes screen's "Manage" panel showed only the box picker and
     read-only "Box details" card - "Rename box" and "Box tools"
     (sort/compact/delete-all) existed in the UI tree but were completely
     unreachable by scrolling, on every fresh open. Root cause: a
     `ScrollView` that starts `IsVisible="False"` is never correctly
     measured by the Android renderer the first time it's shown, and the
     same collapse could recur after any later refresh. Fixed with
     `InvalidateMeasure()`. Confirmed fixed on-device, including actually
     running Sort/Rename afterward.
   - **A real, previously-undiscovered "as if traded" side-effect bug**,
     found while verifying the new Friendship field's round-trip: even a
     **nickname-only edit** on a real Gen9 save was silently flipping
     `CurrentHandler` and fabricating Handling Trainer data (name/gender/
     language/friendship) on every single save, because
     `PokemonDetailPage.OnSaveChangesClicked`'s `SetPartySlotAtIndex` call
     (and `EntityTransferService.WriteIntoPartySlot`, used by the .pk/
     Showdown transfer page) both omitted `EntityImportSettings.None` -
     `PokemonSlotMover.cs` already had this exact guard, documented, for
     the same reason; these two call sites just never got it. **This bug
     has been live since the nickname/level editor first shipped** - it
     was invisible until a field's correctness started depending on
     reading back through the handler-routed getter. Fixed both call
     sites; verified library-level (`verify/BallFriendshipEdit`) and
     on-device that `CurrentHandler` no longer flips.

3. **Ball + Friendship editing** (closes an item this file previously
   listed as "genuinely not implemented" - see corrected list below).
   Ball real from Gen3 (Gen1/2 hard no-op, `GBPKM.cs:135`); Friendship real
   from Gen2 (Gen1 hard no-op, `PK1.cs:155`; Gen3/4/5 alias
   `OriginalTrainerFriendship`, a genuine write under a different name,
   not a no-op). Same disable-but-show-the-truth pattern as the existing
   Held Item/Ability/Nature/Form fields.

All work commits: `00565f3` (move legality), `a3b5165` (CAPABILITY-GAPS.md),
`e394ab6` (Box Management fix), `34e0b72` (Ball/Friendship + the
EntityImportSettings fix).

### Next candidates (from `CAPABILITY-GAPS.md`, highest value/lowest effort first)

**All of Tier A is now done** (Ball, Friendship, Gender, PP/PP-Ups, computed
stats, type chip(s), Hidden Power, locked-slot guard - see the "Gender
editing..." section of `PROGRESS.md` for the last five). Next up is Tier B,
medium value/effort, in the gap doc's own priority order:

- **Origin/met data** (`pk.Version`, `MetLocation`, `MetLevel`,
  `MetYear/Month/Day`, `EggLocation`) - **SPLIT**, met dates are
  `virtual { get => 0; set { } }` pre-Gen4 (`PKM.cs:185-187`), real Gen4+.
  Heavily legality-coupled (applied-as-is, same banner as everything else).
- **Egg status/hatch** (`pk.IsEgg`, currently read-only in one spot;
  `ForceHatchPKM`, `SetEggMetData`) - **SPLIT**, no eggs in Gen1.
- **Bag/inventory editing** (`sav.Inventory` -> `PlayerBag.Pouches` ->
  `InventoryPouch`) - medium-high, item ID spaces differ per gen but the
  item-sprite keying problem is already solved elsewhere in this app.
- **Markings** (`IAppliedMarkings<bool>`/`<MarkingColor>`) - **SPLIT**,
  bool pre-Gen7, color Gen7+, none in Gen1/2. Cosmetic, low legality risk.
- **Pokérus** (`pk.PokerusStrain`, `PokerusDays`) - **SPLIT**, absent Gen1.
- **Characteristic string** (`EntityCharacteristic.GetCharacteristic`) -
  tiny, read-only-safe, Gen3+. Natural pairing with the Computed card just
  shipped (same card, one more line) if picked up.
- **Box wallpaper/current box** (`IBoxDetailWallpaper`) - small-medium,
  cosmetic.

Full priority-ordered list with API citations, sizes, and per-generation
scope in `CAPABILITY-GAPS.md`.

---

## Part 1 — Previous handoff's three items (closed out earlier this session)

## What's still NOT on-device verified or still blocked (read this first)

- **Drag-and-drop for box/party moves is still never verified on-device.**
  Four `adb input swipe`/`adb input draganddrop` attempts across two
  sessions have failed to trigger MAUI's Android `DragGestureRecognizer`.
  This is a documented ADB/automation limitation, not a suspected app bug -
  the tap-to-select fallback for the same operation *has* been verified
  on-device repeatedly. Needs a human with a real finger to close out.
- **Ball and Friendship editing are now implemented** (see Part 2 above) -
  this bullet used to say "genuinely not implemented," confirmed by grep
  at the time. Don't assume anything else in the "not started" list below
  is still accurate without re-checking the code first; two items in it
  (held item, now this) turned out to be wrong across two passes.
- **Gen 8 Legends Arceus specifically** has no real save file in this
  project's inventory (`pcdata-legendes Arceus.bin` is a box-only partial
  dump, not a main save) - Gen 8 verification rests on Sword/HeartGold.
  Not something this session touched or could fix without a real file.
- **~57% of PKHeX item IDs still have no icon** (1149 of 2684 now covered,
  up from 976 before this session's predecessor's TM/TR pass) - falls back
  to the placeholder glyph by design, not a bug, but still an open gap if
  someone wants to close it further.

---

## What this session did (all pushed, one commit per item)

### Item 1 — Navigation wiring (commit `aa5d047`)

`TrainerInfoPage` and `PokemonTransferPage` existed in code but had no
route registration or UI entry point, left disconnected last session to
avoid concurrent agents clobbering `MainPage.xaml`/`AppShell.xaml.cs`.
Fixed: both routes registered in `AppShell.xaml.cs`; `MainPage` gained a
"View Trainer" button; `PokemonDetailPage` gained an "Import / Export"
button that forwards the current Pokémon through the existing
`NavigationState` hand-off (same contract, no new fields, box-opened
read-only mons correctly land on export-only).

### Item 2 — Consolidated on-device pass (no code changes, verification only)

Booted `PkhexMobile_Emulator`, drove all three requested flows plus one
bonus discovery, all with real `FileSaver`/`FilePicker`/touch input, all
against real save files. **No bugs found** - everything held up, including
two brand-new-this-session code paths that had never touched a device
before. Screenshots in `verify/OnDeviceNavAudit/screenshots/`.

1. **Trainer screen** (newly reachable) - real OT/TID/SID/Money/PlayTime/
   Pokédex data rendered from `gen5_real.sav`; OT edit correctly
   live-truncated at the 7-char SAV5BW limit; saved and round-tripped
   through real file I/O.
2. **`.pk` / Showdown transfer** (brand new page, first-ever on-device run)
   - exported a `.pk5`, re-parsed it, matched the on-screen data exactly.
   Imported a **real Gen3 `.pk3` file** (from this project's own save
   inventory) through the real file picker - the app correctly converted
   PK3→PK5 and reported it. Applied a hand-typed Showdown set. Saved;
   round-tripped correctly. This is the project's first real on-device
   cross-generation entity conversion.
3. **Box↔party move/swap** - re-verified (tap-to-select path already had
   on-device coverage from a prior session). Moved a party Pokémon into an
   empty box slot; counts and both grids updated correctly.
4. **Form/Nature/Ability editing** - re-verified (already had coverage from
   a prior session). Changed a Nature; stat block recalculated correctly;
   round-tripped through real file I/O.
5. **Bonus, not on the original list**: while auditing the code for Item 3,
   found **held item editing was already fully implemented** but never
   documented as done or verified on-device (a prior `WAKEUP.md` had
   wrongly filed it as "not started" alongside ball/friendship). Verified
   it here: changed a held item to Master Ball via the picker, saved,
   confirmed `HeldItem=1` persisted in the exported file.

### Item 3 — Documentation corrections (folded into the `PROGRESS.md` section above)

- Gen 9 species sprites: corrected the claim that dex #906+ shows a
  placeholder - commit `200afee` (from a prior session) filled that gap;
  species coverage is #1-1025 complete. Two separate mentions in
  `PROGRESS.md` were fixed, plus the dated roadmap entry got a
  superseded-by note rather than being deleted (keeps the historical
  narrative intact).
- Item icon coverage: replaced the stale "933/2683 (~35%)" figure with the
  current true count, **counted directly from the vendored files**
  (1149/2684, ~43%) rather than re-quoted from an old commit message.
- Held item editing: removed from the "not started" framing everywhere it
  appeared in `PROGRESS.md`, with a pointer to this session's on-device
  verification.

---

## Corrected roadmap / not started (as of this session)

- **Ball, friendship editing** - now implemented, see Part 2 above. (Was
  genuinely not implemented as of the previous pass in this file - this
  entry is kept to show the correction, not because it's still true.)
- **Pokédex completion display** - a prior `WAKEUP.md` filed this as "not
  built," which was wrong: `TrainerInfoPage.PopulateDex` implements it
  (Seen/Caught/Complete %, gated on `sav.HasPokeDex`) and it's now been
  on-device verified as part of this session's Trainer-screen pass (it was
  just unreachable until Item 1's nav wiring, same root cause as the
  Trainer/Transfer pages themselves).
- **Drag-and-drop verification** - see the top of this file.
- Everything else in `CAPABILITY-AUDIT.md` at P3/P4 not otherwise mentioned
  here: met/origin data, egg status, markings, bag/inventory, PP/PP-Ups,
  contest stats, ribbons, bulk edit, event flags, Mystery Gift, QR.
- **Auto-legalization remains explicitly OUT OF SCOPE** (standing user
  instruction, unchanged).

---

## Known gaps that are fine / by design (unchanged from before)

- Placeholder glyph for the ~57% of item IDs with no icon, and any species
  outside #1-1025 - structural fallback, no crash, no hardcoded ID list.
- Gen 9 sprites are front battle sprites while #1-905 are box/menu icons -
  mild style difference, no better free source exists.
- Design system is light-theme only.

---

## Process / environment notes from this session

- **`adb` + Git Bash path translation is a real hazard.** MSYS rewrites
  any argument that looks like a Unix path, including `adb shell`/`adb
  push`/`adb pull` remote paths like `/sdcard/...` - it silently mangles
  them into a Windows path (`C:/Program Files/Git/sdcard/...`) and the
  remote command then fails in a confusing way (dumps to the wrong place,
  "file pushed" but to nowhere findable). Fix: prefix the *specific*
  command with `MSYS_NO_PATHCONV=1` when the argument is a remote device
  path; leave it unset when the argument is a real local Windows/POSIX
  path (the two needs conflict within a single `adb pull`, so `cd` into
  the target local directory and pull with a bare relative filename rather
  than fighting both at once). This cost real time this session before the
  pattern was found - worth promoting into `CLAUDE.md` §6 if it recurs.
- **A single stray/queued touch event caused one confusing double-navigation**
  (a tap that should have landed on one button appeared to open a Pokémon
  detail screen two pages deep). Root-caused to residual input from a
  preceding file-picker scroll gesture still being delivered after the
  picker closed - not an app bug. Re-confirmed the exact same tap
  coordinates via a fresh `uiautomator dump` immediately before retrying,
  and it worked correctly. Lesson: if a tap produces a wildly-unexpected
  result, don't assume an app bug - re-dump and retry once before
  concluding anything.
- **Subagent-spawning was explicitly declined this session** even when the
  user asked mid-turn to parallelize, because the remaining work
  (single-emulator on-device testing, shared-doc edits to `PROGRESS.md`/
  `WAKEUP.md`) is exactly the collision-prone shape `CLAUDE.md` §8 already
  warns about. Explained the reasoning to the user rather than silently
  complying or silently ignoring the request.
