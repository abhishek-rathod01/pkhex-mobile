# WAKEUP — read this first

**Session completed 2026-07-22.** All three handoff items from the previous
session were done, in order, sequentially (no parallel subagents - this
project's own §8 cost lessons apply to solo sessions too, not just
multi-agent ones). Working tree is clean, everything pushed to
`github.com/abhishek-rathod01/pkhex-mobile`. `dotnet build` = **0 errors**,
7 warnings (pre-existing `CS8622` nullability on `PokemonTransferPage`
event handlers - cosmetic, not fixed this session, not new).

> Start here, then read `CLAUDE.md` (build commands, API traps, the
> recurring per-generation no-op bug class, conventions). `CAPABILITY-AUDIT.md`
> is the prioritized map of what PKHeX.Core offers vs. what the app exposes.
> `PROGRESS.md` is the long-form feature history - it now has a "Navigation
> wiring + consolidated on-device pass" section covering everything below in
> full detail.

---

## What's still NOT on-device verified or still blocked (read this first)

- **Drag-and-drop for box/party moves is still never verified on-device.**
  Four `adb input swipe`/`adb input draganddrop` attempts across two
  sessions have failed to trigger MAUI's Android `DragGestureRecognizer`.
  This is a documented ADB/automation limitation, not a suspected app bug -
  the tap-to-select fallback for the same operation *has* been verified
  on-device repeatedly. Needs a human with a real finger to close out.
- **Ball and friendship editing are genuinely not implemented** (not just
  undocumented - confirmed by grep, no trace in `PokemonDetailPage.xaml.cs`).
  Held item editing, which a previous handoff *also* listed as "not
  started," was actually fully implemented and has now been verified
  on-device this session - see below. Don't assume anything else in that
  old list is still accurate without re-checking the code first; this
  session found one item in it was already wrong.
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

- **Ball, friendship editing** - genuinely not implemented (confirmed by
  code search, not assumption).
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
