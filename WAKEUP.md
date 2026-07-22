# WAKEUP — read this first

**Session paused deliberately at a usage-limit boundary (2026-07-22, ~01:30).**
Agents were stopped by choice, not by failure. Everything is committed and pushed.
Working tree is clean. `dotnet build` = **0 errors**, 7 warnings.

> Start here, then read `CLAUDE.md` (build commands, API traps, the recurring
> per-generation no-op bug class, conventions). `CAPABILITY-AUDIT.md` is the
> prioritized map of what PKHeX.Core offers vs. what the app exposes — it drives
> the remaining roadmap. `PROGRESS.md` is the long-form feature history.

---

## State right now

- **Branch:** `master`, everything pushed to `github.com/abhishek-rathod01/pkhex-mobile`
- **Working tree:** clean
- **Build:** `dotnet build PkhexMobile/PkhexMobile.csproj -f net10.0-android -c Debug`
  → **0 errors**, 7 warnings (all `CS8622` nullability on event handlers in the new
  `PokemonTransferPage` — trivial, fix first thing)

---

## ⚠️ The three things that need doing before this work is trustworthy

**1. Nothing from this session's second half is verified on-device.**
Only one emulator exists, so on-device verification could not be parallelized. It was
granted to the per-Pokémon editor agent (move types are on-device verified). Everything
else — Trainer screen, box management, `.pk`/Showdown import-export, held-item editing,
ball, friendship, EV/IV cap rework — is **library-harness verified only**. This project
has a documented precedent (the Shell `InvalidCastException`) of a bug that appears only
on-device and never in a build. **Run a consolidated on-device pass before trusting any
of it.**

**2. Navigation for the new pages is NOT wired.**
To prevent four concurrent agents from clobbering shared files, all of them were barred
from editing `MainPage.xaml` and `AppShell.xaml.cs`; the orchestrator was going to wire
navigation in a final integration pass that never happened. So new pages exist but may be
**unreachable from the UI**. Check what routes are registered and add entry points for:
`TrainerInfoPage` (or whatever the trainer screen is named), `PokemonTransferPage`.
Use the `NavigationState` static hand-off — **never** Shell's `GoToAsync` query
dictionary (documented `InvalidCastException`; see `CLAUDE.md`).

**3. `PROGRESS.md` has stale claims.** It still says Gen 9 species sprites are
unavailable and that #911 falls back to a placeholder. **That is no longer true** — the
full #906–1025 range was vendored (commit `200afee`). Item-icon counts are stale too.
Correct these when convenient; several agents were appending concurrently so
reconciliation was deferred.

---

## What shipped this session (all pushed)

| Area | Commit(s) | State |
|---|---|---|
| Box↔party move/swap, drag + tap-to-select | `0cf6386`, `79300b3` | Tap path on-device verified. **Drag-and-drop is code-correct but was never verified** — ADB cannot drive it (4 attempts). Needs a human finger. |
| Form / Nature / Ability editing | `74a8f95` | Harness-verified across Gen1/3/4/5/9 |
| GitHub Actions CI | `2524279` | Builds PKHeX.Core + runs the 9 self-contained Gen harnesses |
| Item icons (name-matched) | `db049cc` | 933 → 976 |
| **Gen 9 species sprites #906–1025** | `200afee` | 240 files. **Species coverage now complete #1–1025**, regular + shiny |
| TM/TR item icons via move→type mapping | `a7aacb4` | +173 type-colored icons |
| Capability audit | `3b75c12` | `CAPABILITY-AUDIT.md` |
| `CLAUDE.md` context doc | `395577b` | New — lessons learned, auto-loaded each session |
| Move-type indicators | `795a1d5` | **On-device verified** |
| EV/IV caps from PKHeX.Core + 510 budget | `5481cc1` | Replaced hardcoded caps (a real defect the audit found) |
| Trainer screen + capability probe | `12aad99`, `2378298` | Proven against real saves incl. the `SAV3RSBox` `.gci` edge case |
| Box management (rename/sort/compact/clear) | `ec5cdc9` | Capability-probed |
| `.pk` + Showdown import/export service | `a5c2ce3`, `ba308ea` | Service + `PokemonTransferPage` |
| Checkpoint of remaining in-flight work | `243b4c0` | Box + detail editor remainder |

---

## Not started / deferred

- **Held item editing, ball, friendship** — the per-Pokémon editor agent was stopped
  partway through its list (it had landed move types + EV caps and was on held item).
  Verify what actually landed in `PokemonDetailPage` before redoing anything.
- **Pokédex completion display** — specced in the audit (§3.10), not built.
- Everything else in `CAPABILITY-AUDIT.md` at P3/P4: met/origin data, egg status,
  markings, bag/inventory, PP/PP-Ups, contest stats, ribbons, bulk edit, event flags,
  Mystery Gift, QR.
- **Auto-legalization remains explicitly OUT OF SCOPE** (standing user instruction).

---

## Known gaps that are fine / by design

- ~1450 item IDs still show the placeholder glyph (mostly obscure items). The fallback is
  structural — a missing image renders nothing and a placeholder shows through — so this
  degrades gracefully and needs no valid-ID list.
- Gen 9 sprites are front battle sprites while #1–905 are box/menu icons; mild style
  difference, no better source exists.
- Design system is light-theme only (no dark tokens were ever provided).

---

## Process lessons that actually mattered (full versions in `CLAUDE.md` §8)

- **Push early.** A usage limit killed 5 agents mid-run today. Because everything had been
  pushed minutes before, nothing was lost. Files on disk survive a cutoff; the real danger
  is several agents' *uncommitted* edits tangling in one shared tree.
- **Commit incrementally, one commit per feature.** This is why the second wave lost
  nothing — 7 commits were already banked when the pause came.
- **Assign disjoint file ownership** and let the orchestrator wire shared files. This
  worked; the cost is item #2 above (nav wiring left undone).
- **Verify subagent self-reports.** Two separate agents confidently reported Gen 9 sprites
  as unavailable anywhere. Both were wrong — a direct check found all 120 species. Always
  re-check claims that drive a downstream decision.
- **On-device verification can't be parallelized** — one emulator.
