# Wake-up summary — overnight 3-task queue complete, all committed

All three queued tasks finished successfully, verified on-device, and
committed. No stop conditions were hit (no error recurred 3× on any task).
This file replaces the interim per-task version written earlier in the run.

## Queue result

| Task | Result | Commit |
|---|---|---|
| 1. UI reskin (design system, sprites, held items, app icon, dirty/clean Save) | ✅ Done, verified Gen1/5/9 on-device | `1a7fb33` |
| 1.5. Verification pass gating Task 2 | ✅ Done — screenshots + regression tests, no gaps found, no fixes needed before proceeding | (part of gate, no separate commit) |
| 2. Read-only legality badge (`LegalityAnalysis`) | ✅ Done, verified it correctly flags issues introduced by species/move edits | `d1caf9f` |
| 3. Design-system consistency: box view + edit-form details | ✅ Done, closed 3 real gaps found on-device | `20b39f5` |

Working tree is clean; `dotnet build ... -c Debug` succeeds with 0
warnings/0 errors as of the last commit. Nothing is mid-flight.

## Task 1 — UI reskin

Full visual reskin of every screen against a separately-authored design
handoff bundle (`PkhexMobile Design System/design_handoff_pkhexmobile/`),
strictly "reskin + button wiring, no data-logic changes" per instruction.

- **Scope call** (confirmed via an advisor consult before starting): the
  design mockup's `DetailScreen.jsx` is a read-only inspector with unwired
  Edit/Verify/LegalityBadge affordances, explicitly marked "not yet
  designed" in the bundle's own README. Kept the app's working direct-edit
  `Entry`/`Picker` form instead of replacing it with that inert paradigm;
  applied the visual language (colors, type, spacing, radii, shadows,
  motion) to the existing form.
- **Design tokens → MAUI**: `Colors.xaml` (full token port), `Tokens.xaml`
  (new — spacing/radii/type-scale/motion/shadow), `Styles.xaml` (rewritten
  — text roles, 4 button variants with disabled/pressed states, card/row
  surfaces). Fonts (Space Grotesk/Manrope/JetBrains Mono) self-hosted as 12
  static-weight `.ttf` files generated via `fonttools` (Google Fonts only
  ships variable fonts now).
- **New behavior** (the one actual behavior change, not just style): Save
  button dirty/clean tracking — disabled while clean, enabled on first
  edit, disabled again after a successful save.
- **Sprites**: species icons (Dex #1–905, regular+shiny — Gen9/SV isn't in
  pokesprite yet) and held-item icons (933/2683 PKHeX item IDs, matched by
  name since pokesprite's own numbering doesn't correspond to PKHeX's) from
  `msikma/pokesprite`. Missing sprites fall back to a placeholder glyph via
  image layering (no hardcoded valid-ID list to maintain).
- **App icon**: original Poké Ball SVG, wired via `MauiIcon`/
  `MauiSplashScreen`; README attribution note added.

**On-device regression verification** (Task 1.5, before Task 2 started):
Gen1 (`gen1_real.sav`, MEW) — Gen1/2 IV/DV caps, derived HP, linked SpD,
live clamp all confirmed intact, no crash. Gen5 (`gen5_real.sav`) — full
nickname-edit → Save (dirty→enabled) → real FileSaver dialog → saved →
button reset to disabled → reloaded party list shows the edit. Gen9
(`gen9_real.sav`, Skeledirge) — species+move edit → save → pulled the
exported file and read it back via a throwaway PKHeX.Core harness (deleted
after use): `Stat_Level` and `Stat_HPMax` correctly recomputed, confirming
the species-before-level and stale-stat-block bugs a previous session fixed
are still fixed. No regressions found anywhere; nothing needed fixing
before Task 2 could start.

## Task 2 — Read-only legality badge

Added `PokemonDetailPage.RefreshLegality`, which runs PKHeX.Core's own
`LegalityAnalysis(pk)` directly (no reimplementation, no auto-fix) and
displays `la.Valid` as a green "LEGAL" / red "ILLEGAL" banner with
`la.Report()`'s human-readable text underneath. Recomputed on load and
again after a successful save — not live per-keystroke (encounter matching
isn't free, and the existing hero/title refresh already follows this same
cadence).

**Verified the specific thing the task asked for**: a real Gen9 save mon
(Skeledirge) showed green "LEGAL" until edited via the species Picker
(Skeledirge→Quaxwell) and saved — the banner then correctly flipped to red
"ILLEGAL" with a specific, itemized reason list (invalid moves for the new
species, unmatched encounter, invalid ability, unexpected TR-learned
flags). Also confirmed the banner correctly stays on the *stale* result
while a field is being edited but not yet saved (by design, not a bug), and
that `LegalityAnalysis` doesn't crash on Gen1's substantially different
parse path (tested against `gen1_real.sav`'s MEW, which — correctly —
showed ILLEGAL/"unable to match an encounter," since it's a hand-crafted
test mon, not a legitimately-obtained one).

## Task 3 — Design-system consistency: box view + edit forms

Audited (didn't rebuild from scratch) `BoxListPage` and the detail screen's
read-only rows against the visual language from Task 1, since box view was
already restyled in that pass. Found and fixed 3 concrete gaps, all XAML-
only (no `.cs` changes):

1. `BoxListPage`'s row template was missing the held-item indicator that
   `PartyListPage`'s row already had (both bind the same
   `PartyEntryDisplay` — straight copy-paste gap).
2. The box-selector `Picker` had no label caption above it, unlike every
   other field in the detail form.
3. The read-only Nature/Ability rows didn't match the design's `DataRow`
   spec (right-aligned semibold value, bottom divider) — now they do.

Box editing (moving/swapping slots) remains explicitly out of scope, as
instructed — nothing here touches that.

## Environment notes for next session (things that cost time this run — read before repeating)

- **Background subagents and unattended permission prompts don't mix.** A
  `git clone`-based sprite-vendoring subagent was silently killed early in
  this run (status "stopped by user," would not resume via `SendMessage`)
  — almost certainly a permission prompt with nobody available to approve
  it. Redeploying as a fresh agent (not resuming) worked once the user was
  back to approve prompts. If queuing background work for later unattended
  execution, prefer pre-approved/narrowly-scoped commands.
- **A background agent's self-report can be wrong — verify anything that
  drives a downstream decision.** The species-sprite subagent claimed
  pokesprite "doesn't cover Gen8/9" and stopped at Dex #809; a direct
  30-second check of `data/pokemon.json` showed contiguous coverage to
  #905. Caught before it became stale documentation or a wrong fallback
  threshold in the app.
- **UI-automation coordinate scaling**: `adb shell screencap` output is
  full device resolution (1080×2400 on this AVD); the image as *displayed*
  in tool output is downscaled (900×2000 this session) — multiply displayed
  coordinates by the scale factor (1.2 here) before `adb shell input tap`.
  When a tap doesn't land where expected, `adb shell uiautomator dump` +
  parsing `bounds="[x1,y1][x2,y2]"` is faster and more reliable than
  re-guessing from a screenshot.
- **Deploy command**: `dotnet build PkhexMobile/PkhexMobile.csproj -f
  net10.0-android -c Debug -t:Run`. A bare `adb install` still crashes
  (Fast Deployment's missing-assemblies error). `-t:Run` sometimes reports
  "up to date" without actually redeploying new code — if a UI change
  doesn't show up after `-t:Run`, `adb shell am force-stop
  com.companyname.pkhexmobile` then relaunch via `adb shell am start -n
  com.companyname.pkhexmobile/crc64b42f7bba2754976c.MainActivity` to be
  sure you're looking at the current build, or `adb uninstall` first.
- **XML comment gotcha**: a `<!-- ... -->` comment that echoes a CSS custom
  property name (e.g. `--role-body`) breaks XAML parsing — literal `--`
  anywhere inside an XML comment is invalid, not just at the boundary.
  Build catches it immediately (`MAUIG1001`), one-line fix.
- **File-picker default location drifts** between runs based on where the
  system Storage Access Framework UI was last used (Download vs. Documents
  vs. root) — don't assume a fixed starting folder when scripting picker
  navigation; dump the UI hierarchy and check the breadcrumb first.

## What's next (not started, no blocker)

- ~~**Box editing** (moving/swapping slots between boxes/party)~~ — **done,
  see "Box/party move + swap" below.**
- **Form/Nature/Ability editing** — still read-only (PID-derived on several
  gens for Nature/Ability; Form has no per-species form list yet).
- **Gen9 species sprite gap** — pokesprite has no Scarlet/Violet/Legends
  Z-A icons yet; ~120 species (dex #906+) show the placeholder until a
  source with Gen9 coverage is vendored.
- **Item icon gap** — ~65% of PKHeX item IDs (mostly TM/TR and minor items)
  have no matching pokesprite icon and show the placeholder.
- The legality badge is read-only by design (task scope) — no "fix
  legality" action exists or was requested.
- **Drag-and-drop between two different boxes** — not attempted; would
  need both boxes visible simultaneously. The tap-to-select fallback
  (select in box A, switch the picker, tap a destination in box B) already
  covers this case — see below.

## Box/party move + swap — completed (2026-07-21)

Enabled moving Pokemon between box and party slots (both directions) plus
box-to-box moves, via drag-and-drop and a tap-to-select-then-tap-destination
fallback, both wired into `BoxListPage` (now shows a party grid + box grid
together, gated behind a new "Move mode" switch so the pre-existing
tap-to-view-detail browse behavior is unchanged when the switch is off).
Full writeup, including the guard-by-guard reasoning for the write-path
core and the on-device verification table, is in `PROGRESS.md`'s
"Box/party move + swap" section (2026-07-21) — this is a pointer to it,
not a duplicate.

**Sequencing that paid off:** per an advisor consult before writing any
XAML, the data-integrity-sensitive part (`PkhexMobile/PokemonSlotMover.cs`)
was built and proven against real Gen1/5/9 saves
(`verify/BoxPartyMove/Program.cs`, all cases pass) *before* any grid UI
existed — so the part with real data-loss/duplication risk was already
verified correct before the harder-to-verify UI layer was touched.

**Genuine empirical finding, not assumed:** the task's own working theory
("a box-sourced PKM should have `Stat_HPMax == 0`, so the library's
existing `SetPartyValues` auto-gate should already handle box→party stat
resets for free") was verified **false for Gen9** specifically — a fresh
`GetBoxSlotAtIndex` read on the real `pkmnscarlet_100\main` save has
`Stat_HPMax == 12`, `PartyStatsPresent == true` even before any write.
Gen1 and Gen5 *do* match the theory (`Stat_HPMax == 0`). Good thing the
mover was built to call `pk.ResetPartyStats()` explicitly and
unconditionally on every box→party transition rather than trusting the
auto-gate — relying on the theory alone would have silently reintroduced
the exact stale-party-stat-block bug the "Species + move editing" session
already found and fixed once, specifically for Gen9 box→party moves.

**Gap caught and closed mid-task:** the grid UI initially had no way to
export a move to disk at all (a move only mutates the in-memory `SaveFile`
— unlike the per-mon edit form, there's no natural "Save Changes" button on
a slot-move screen). Caught before declaring the task done, not by a user
report; added a dirty/clean-tracked "Export Save" button to `BoxListPage`,
verified on-device (real FileSaver dialog, then read back the exported
file with a throwaway harness to confirm the on-device swap round-tripped
correctly, deleted the harness after use).

**On-device (`PkhexMobile_Emulator`) vs. library-only, the usual split:**
tap-to-select — every variant (party↔box swap, box↔box swap, cross-box
move with picker switching mid-selection, same-slot cancel, Move-mode-off
browse regression, Export Save + real FileSaver + read-back) — was driven
with real touch input on the emulator against `gen9_real.sav`, plus one
Gen1 smoke swap against `gen1_real.sav` (no crash). **Drag-and-drop is
wired identically in code** (`DragGestureRecognizer`/`DropGestureRecognizer`,
same `PerformMove` call as the tap path) but **could not be triggered via
ADB** — tried `adb shell input swipe` (two attempts, 1.2s/2.5s duration)
*and*, on an advisor's suggestion that a hold-then-move command might
succeed where a synthetic swipe didn't, `adb shell input draganddrop`
(two more attempts, 1s/3s hold — this command exists on this AVD's API
level and is purpose-built for exactly this kind of gesture). All four
attempts: no crash, no move, no visible drag feedback at all. This is an
ADB/automation limitation, not evidence the drag code itself is wrong,
but it means drag specifically is code-verified and manual-touch-verified
*never* in this pass — **flagging this prominently rather than folding it
into a table row**: one of the two co-equal required interaction methods
(drag) is implemented but functionally unverified; the other (tap-to-select)
is fully verified on real touch input across every case the task asked
for. A future session with either a physical device or genuine manual
interaction (not scripted ADB) is needed to close this specific gap.

**Environment notes for next session (additions to the list above):**

- **ADB tap coordinates from a Read-tool screenshot need the same 1.2×
  scale-up as everywhere else** — several taps in this session initially
  missed because a coordinate was read directly off the *displayed*
  900×2000 screenshot without multiplying by the documented 1.2 factor to
  get real 1080×2400 device coordinates. Symptom looks exactly like "the
  app didn't respond to the tap"; always cross-check with
  `adb shell uiautomator dump` + `bounds="[x1,y1][x2,y2]"` (those bounds
  are already in real device coordinates, no scaling needed) before
  concluding a UI defect from a screenshot-only read.
- **A page's own layout can shift the coordinates of everything below
  it between taps** — e.g. once `BoxListPage`'s status label or Export
  button changed height/visibility, every grid cell below it moved. Prefer
  re-dumping/re-screenshotting after any state change that could alter
  content height, rather than reusing coordinates from an earlier
  screenshot of the "same" page.
- **`git status` showing commits you didn't make is expected, not an
  error, when other agents share this working tree** — two unrelated
  commits (a GitHub Actions CI workflow, additional vendored item icons)
  landed on `master` mid-session from other concurrent agents, exactly as
  this project's environment notes warned could happen. `git diff`/`git
  log` confirmed no overlap with this task's files before committing;
  worth the same check next time this happens rather than assuming it's
  your own uncommitted work resurfacing.

Delivered: `PkhexMobile/PokemonSlotMover.cs`, `SlotCellDisplay.cs`
(new), `BoxListPage.xaml[.cs]` (rewritten), a `SlotCellStyle` addition to
`Styles.xaml`, `verify/BoxPartyMove/` (kept — covers the write-path core,
matches this project's convention of keeping harnesses that test
production code directly via `<Compile Include>`), and
`verify/OnDeviceBoxPartyMove/screenshots/` (53 screenshots). The one-off
export-read-back harness used to confirm the on-device round-trip was
deleted after use, matching the `OnDeviceEvFix` convention.

## Task 2 — Form/Nature/Ability editing (2026-07-22)

The agent doing this task was killed mid-run by a session usage-limit cutoff
(not a permission block, not a code failure) partway through on-device
verification — resumed and finished by the parent session picking up its
uncommitted working-tree state directly, rather than restarting from
scratch. Everything the interrupted agent had written (code + both harnesses
+ 74 on-device screenshots) was reviewed and independently re-verified
before being folded in:

- **Rebuilt the app** (`dotnet build ... -f net10.0-android -c Debug`) —
  0 warnings/errors against the agent's in-progress diff.
- **Re-ran `verify/FormNatureAbilityEdit/Program.cs` myself** — all 6
  cases pass (Gen1 no-op confirmation, Gen5 Nature+Ability edit, Gen5 Form
  edit via a Giratina form switch, Gen9 Nature+Ability edit, Gen9 Form
  edit via a Zygarde form switch, plus bonus Gen3/Gen4 real-save
  confirmations), each with the original file confirmed byte-for-byte
  untouched on disk afterward.
- **Reviewed the on-device screenshot sequence** (`verify/OnDeviceFormNatureAbility/screenshots/`,
  74 frames) — it runs cleanly through Gen9 (Ability/Nature edits, a
  Species+Form change to Squawkabilly, real FileSaver save, legality
  badge check) and Gen5 (Nature edit, real FileSaver save) and stops
  cleanly at a successful Gen5 save confirmation, not mid-crash — the
  session limit hit right after, not during, a save.
- **Reviewed the full code diff** (`PokemonDetailPage.xaml[.cs]`,
  `PartyEntryDisplay.cs`) line by line before trusting it. See
  PROGRESS.md's "Form + Nature + Ability editing" section for the full
  per-generation table and the genuine finding worth flagging here too:
  **Gen8+'s Mint mechanic (`StatAlignment`) is a separate byte from
  `Nature`** that `PKM.LoadStats` actually reads for the stat-boost
  calculation — the interrupted agent's own harness caught this itself
  (first pass set only `Nature`, the Gen9 stat-block assertion failed
  outright), fixed it by syncing `pk.StatAlignment = newNature` whenever
  `pk.Format >= 8`, and the fix is confirmed correct by the harness's
  Stat_ATK/Stat_SPA before/after dumps. Also worth flagging: Gen4 turned
  out to have a split behavior (Ability/Form real, Nature still a no-op)
  that contradicts a same-generation-behaves-the-same assumption — caught
  by the bonus Gen4 test the agent ran against a real HeartGold save
  specifically because it was the row of the table most likely to be
  assumed wrong by pattern-matching against Gen3.
- Committed as a new commit on `master` after this review, per the
  project's "new commit, don't amend" rule — this session didn't touch
  anything the interrupted agent hadn't already written, only verified it
  and completed the documentation/commit step it never reached.

**Environment note:** background agents can also be terminated by a
session/usage-limit cutoff, not just a permission prompt — a different
failure mode than the `git clone` permission-prompt kill documented above,
but the same recovery approach applies: check the working tree for
salvageable in-progress work (`git status`/`git diff`) before assuming
anything needs to be redone from scratch. In this case the interrupted
agent's work was complete and correct; the only thing missing was the
docs-and-commit step.
