# WAKEUP — read this first

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
- **In progress / next**: working through `CAPABILITY-GAPS.md` Tier A -
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

Ball and Friendship (items #1/#2 in the gap analysis) are now done - the
next Tier A items, all small:

- **Pokémon-level Gender editing** - `pk.Gender`, real from Gen4
  (`PK4.cs:170`). Identical shape to the shipped Nature/Ability pickers.
  `CAPABILITY-AUDIT.md` under-scoped this (buried in its traps section
  instead of the main feature table).
- **Manual PP / PP-Ups editing** - `pk.Move1_PP..4_PP`/`Move1_PPUps..`,
  uniform across all generations. The app only ever calls `SetMoves`
  (auto-max PP) today.
- **Three read-only-safe display additions** (no legality-warning
  treatment needed, same shape as the existing legality badge/type chips):
  computed final stats (`Stat_HPMax`/`Stat_ATK`/etc. - the app shows the
  *inputs* but never the resulting battle numbers), species type chip(s)
  (reuses the move-type-chip component that already exists), and Hidden
  Power type/power (derived from IVs, Gen2-7).
- **Locked-slot guard in `PokemonSlotMover.MoveOrSwap`** - a hardening
  item, not a feature: `BoxManagement`'s bulk sort/clear ops already check
  `sav.IsAnySlotLockedInBox`; the per-slot move/swap path doesn't. One
  guard, prevents overwriting battle-box/daycare/in-transit slots on later
  gens.

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
