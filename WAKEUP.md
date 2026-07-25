# WAKEUP — read this first

> Read this file, then `CLAUDE.md` (build commands, API traps, the recurring per-generation
> no-op bug class, conventions). `PROGRESS.md` is the long-form technical history — every
> decision's *why*. `CAPABILITY-GAPS.md` is the priority-ordered map of PKHeX.Core capability
> the app still doesn't expose (last refreshed 2026-07-22, still accurate — Tier A is fully
> done, Tier B's bag/inventory item is next). `CAPABILITY-AUDIT.md` is a stale pre-build
> planning snapshot — don't trust it without cross-checking `CAPABILITY-GAPS.md` first.

## Current state (as of 2026-07-25)

- `master`: clean working tree, all pushed, `dotnet build -f net10.0-android -c Debug` = **0
  errors, 0 warnings** (the 7 baseline nullability warnings were fixed this pass — a genuine
  0/0 build now, not just 0 errors).
- `3d-models-experimental`: pushed, NOT merged, stays a separate branch. Do not `git merge` it
  into `master` — see "Next steps" below for why and what to do instead.
- All 9 CI harnesses (`verify/Gen1..9`) pass locally, matching what CI runs.
- No known open crash/data-corruption bugs. The three real bugs found this session (nickname/
  IsNicknamed, CurrentLevel EXP loss, DatePicker corruption) are fixed and covered by a general
  invariant harness (`verify/UntouchedSaveInvariant`) that passes clean across 5 generations.

## Next steps, in priority order

1. **3D model color/rendering — the actual blocker for the whole 3D feature.** Two WebView-
   origin prototypes both failed (see "3D viewer investigation" below for the full trace). The
   recommended next attempt, not yet built: a tiny loopback HTTP server (`HttpListener` bound to
   `127.0.0.1`) serving the on-disk model cache, paired with a plain `WebView.Source = <url>`
   instead of `HybridWebView`. This gives the page a real, unambiguous origin — fixing the
   `blob:null` texture-load failures and the Draco Worker failures in one move — and *also*
   eliminates the `HybridWebView`/`DefaultFile`/`EvaluateJavaScriptAsync` crash path entirely,
   since it wouldn't use `HybridWebView` at all. Test it against the largest model (species 979,
   ~8.2MB) before building the full fetch/cache pipeline on top of it, same as the two prototypes
   already tried — `adb logcat -d | grep chromium` shows real JS console output directly, no
   bridge needed, useful for reading whatever this attempt does.
2. **Hi-res Pokedex artwork — ready to build, no open technical question.** Source confirmed:
   `PokeAPI/sprites`'s `official-artwork` (CC0-licensed, full 1-1025 coverage incl. shiny,
   ~150KB/image, ~292MB for the complete set). Straightforward `HttpClient` GET + disk cache
   under `FileSystem.CacheDirectory` + `Image` control, falling back to the existing bundled
   pixel sprite when offline/uncached/not-yet-fetched. No serving-mechanism problem like the 3D
   case — this can be built independently and doesn't block on item 1.
3. **Port the 3D viewer feature onto `master`, once item 1 is solved.** Not a `git merge` of
   `3d-models-experimental` — that branch's commits carry +214MB of `.glb` blobs, and once such a
   commit becomes an ancestor of `master` those blobs are in `master`'s history forever (`git rm`
   afterward does NOT undo this). Port the *code* (page, route, "View in 3D" button) freshly,
   rewritten against fetch-on-demand + the loopback-server serving mechanism from item 1.
4. **Mega Evolution in the Pokedex evolution chain** — scoped, not started. PKHeX.Core models a
   Mega as a `Form` value on the same species (`ItemStorage9ZA.GetExpectedMegaStoneOrPrimalOrb`
   gives the species→form→required-item mapping). Medium effort: the evolution-chain walk
   (`PokedexService.cs`, built on `EvolutionTree.Evolves9`) needs Mega nodes manually grafted in
   and marked "battle-only," and there's no form-aware sprite support yet
   (`SpriteHelper.SpeciesSpriteFile` takes no form parameter) — build that first, or fall back to
   the base-species sprite/artwork for Mega nodes (this app's existing missing-asset convention).
   Mega art coverage via PokeAPI is ~42% direct + the rest via base-species fallback — workable,
   not a blocker.
5. **`CAPABILITY-GAPS.md` Tier B #11: bag/inventory editing.** The one sizeable remaining item
   from the *save-editing* feature backlog (separate track from items 1-4 above, which are
   Pokedex reference-data features, not save-editing capability). `sav.Inventory` →
   `PlayerBag.Pouches` → `InventoryPouch`, `CopyTo(sav)`. Medium-high size; item ID spaces differ
   per gen but the item-sprite ID-keying problem is already solved elsewhere in this app. Hasn't
   been touched since being flagged — still the next real save-editing feature if that track is
   picked back up instead of the Pokedex/3D track above.
6. **"Extensive bug testing" is not exhausted, just out of active leads.** The synchronous-ANR
   audit (item type: expensive `LegalityAnalysis`/`EncounterMovesetGenerator` calls on the UI
   thread) is genuinely done — grepped the whole app, only two call sites exist, both correctly
   backgrounded. If picking this up again: consider extending `verify/UntouchedSaveInvariant`'s
   pattern to `PokemonTransferPage`'s import/apply path (a Haiku audit found it clean by
   inspection, but it's never been machine-verified the way `PokemonDetailPage` now is), or ask
   the user for a concrete crash repro (screen, save file, action) if they report one — guessing
   without a repro risks the "unverified fix" pattern that bit this session twice already (see
   below).

## Decisions the user has already made (don't re-litigate)

- **Fetch-on-demand, not bundling**, for both 3D models and hi-res Pokedex art (2026-07-25).
  Bundling both (+214MB / +292MB) would add ~506MB to the APK — not viable.
- **"Merge it all"** (2026-07-25) — interpreted as approving the 3D viewer feature's integration
  into `master` under the fetch-on-demand architecture, not a literal branch merge (which would
  permanently bake the 214MB of blobs into `master`'s history — see Next Steps item 3). If this
  reading is wrong, the user should say so explicitly; nothing has been done yet that would need
  undoing.
- **Non-official asset sources are fine** (2026-07-25) for anything not available from an
  official/first-party source, as long as it's non-commercial, well documented, and disclaimed —
  this is why `PokeAPI/sprites` (a CC0-licensed fan/community repo, not Nintendo's own) is an
  acceptable source for hi-res artwork.
- **Delegate freely to Haiku subagents** (2026-07-25) for safe, low-risk, read-only research —
  the "at most one background subagent" cap from earlier sessions is lifted; multiple concurrent
  Haiku agents are fine as long as the work is genuinely low-risk (research/audits, not
  production writes) and self-reports are still independently verified before being trusted (see
  `CLAUDE.md` §8 — this rule itself is unchanged, just the concurrency cap).
- **The "optimised vs full-scale 3D model" toggle is not buildable as scoped** — the upstream
  asset source only publishes one (already-optimized) variant per species. Would need a
  different source entirely, not a toggle over what's already fetched.
- **Auto-legalization, auto-fix, Pokédex *writing*, and a PID-mutating shiny toggle remain
  explicitly out of scope** — long-standing, unchanged across every session.

## 3D viewer investigation (2026-07-23 through 2026-07-25, condensed — see PROGRESS.md on both
## branches for the full evidence trail)

- **The "keeps crashing" report does NOT trace to the 3D branch's asset bundle.** A ~33-39s
  cold-start delay measured on `3d-models-experimental` initially looked like strong evidence of
  the +214MB/1866-file bundle causing it — refuted by measuring the identical ~33s delay on
  `master`, which has none of those assets. It's Debug-build/x86_64-emulator JIT+verification
  overhead across the whole project, not fixable by trimming assets, and not confirmed to affect
  a real device or a Release build at all.
- **The one confirmed, fixed ANR-class bug**: `RefreshLegality` (`PokemonDetailPage.xaml.cs`) ran
  full `LegalityAnalysis` encounter-matching synchronously on the UI thread on every page load and
  save. Fixed with `Task.Run`; a resulting cross-thread data race (backgrounding the *live*,
  still-editable `PKM`) was caught and fixed in a follow-up commit. This is the most concrete,
  verified crash/freeze-adjacent fix from the whole investigation.
- **A real `HybridWebView` crash exists on `3d-models-experimental` only** (`PlatformView cannot
  be null here`, `HybridWebViewHandler.MapEvaluateJavaScriptAsync` — MAUI's own internal handling
  of the `DefaultFile` property change, not anything this app's code calls directly). A tentative
  mitigation is on that branch (waits for the native WebView to exist before touching
  `DefaultFile`) but is **not confirmed to fix it** — the exception is rethrown on a posted async
  continuation, which a synchronous guard narrows but can't provably close, and the original
  crash was never reproduced to test the mitigation against.
- **Off-colour models**: confirmed the `.glb` files have real embedded WebP textures
  (`bufferView`, not external `uri` — one theory eliminated for free). Two on-device WebView
  rendering prototypes (see Next Steps item 1) both failed to render at all, for reasons that
  look like an opaque/wrong page origin breaking `blob:` URL creation and Web Worker (Draco
  decoder) initialization. Root cause not fully closed; a loopback HTTP server is the next thing
  to try, not yet built.
- **Two Haiku research passes, independently sanity-checked**: hi-res Pokedex artwork source
  confirmed (see Next Steps item 2); Mega Evolution's PKHeX.Core representation and integration
  scope confirmed (see Next Steps item 4).

## Bugs found and fixed this session (full write-ups in `PROGRESS.md`)

All of the following are the SAME bug class: **a save operation silently mutating a field the
user never touched**, because a value was reassigned unconditionally instead of only when it
actually changed from what was loaded. Four instances found across three passes, each one
initially missed by a harness that only checked the specific field it was written for — which is
why `verify/UntouchedSaveInvariant` (a general "assert full field identity on a zero-edit save"
harness) now exists and should be extended, not bypassed, for any new PKM-mutating feature.

1. **DatePicker date corruption** — an untouched mon's unset (`0/0/0`) Met/Egg date got clamped
   to `(2000,1,1)` for display, then written back as a real date on every save. Fixed with a
   load-time baseline comparison.
2. **Nickname/IsNicknamed forced true on every save** — a non-nicknamed mon's stored `Nickname`
   already equals its species' default name, so an unconditional `pk.IsNicknamed = true` silently
   promoted every such mon to "explicitly nicknamed" on any edit at all. Fixed via PKHeX.Core's
   own `SpeciesName.IsNicknamed`/`SetNickname`/`ClearNickname` split — **first fix attempt**.
3. **The fix for #2 had a residual bug in the opposite direction**, caught by `advisor` review
   before shipping further: a mon deliberately nicknamed to exactly its own species' default name
   (a real, valid scenario) got silently un-nicknamed by the value-comparison logic. Fixed by
   switching to a baseline comparison (did the text actually change from what was loaded), same
   pattern as the DatePicker fix — **second fix attempt, now verified by the general invariant
   harness, not just a bug-specific one**.
4. **`CurrentLevel = level` reassigned unconditionally on every save** — the setter unconditionally
   snaps EXP to the exact level threshold, discarding real "overshoot" progress toward the next
   level on any below-max-level mon. Fixed by only recomputing EXP when species/form/level
   actually changed.

**Any save this app has ever written before these fixes may already carry silently-flipped
`IsNicknamed` flags or lost EXP overshoot** — there's no way to retroactively detect which files
were affected. Flagged for awareness, not a "scan and repair" task.

## Standing process notes (unchanged across sessions, still apply)

- **Never `git merge` a branch whose commits carry large binary assets into `master`** if the
  intent is to eventually drop those assets — the blobs stay in history forever regardless of
  later deletion. Port code fresh, or `git merge --squash` with the asset files excluded from the
  staged tree.
- **`adb` + Git Bash path translation is a real hazard.** MSYS silently rewrites `/sdcard/...`-
  style remote paths into a mangled Windows path. Prefix the specific command with
  `MSYS_NO_PATHCONV=1` for remote device paths; leave it unset for local Windows paths — the two
  needs conflict within a single `adb pull`, so either use the PowerShell tool for pulls (it
  doesn't have this problem) or `cd` into the local target directory first.
- **Real WebView/JS console output is visible directly via `adb logcat -d | grep chromium`** — no
  message bridge or custom `WebChromeClient` needed. A prior branch note wrongly assumed this
  needed building first.
- **Subagent self-reports must be independently verified before being trusted** — this bit the
  project twice with real, severe bugs (nickname/level) that a subagent found correctly but that
  needed re-confirmation against real saves before acting on them. Still the rule even with the
  concurrency cap lifted.
- **Drag-and-drop for box/party moves is still never verified on-device** — a documented ADB
  automation limitation (`input swipe`/`draganddrop` don't trigger MAUI's `DragGestureRecognizer`
  after 4 attempts across sessions), not a suspected app bug. Needs a human with a real finger.
- Real save files for testing live in `C:\Users\abhis\Downloads\sav files pkmn` — see
  `PROGRESS.md`/`CLAUDE.md` for the specific per-generation file names still in use.

---

*Older session history (2026-07-22 and earlier — Trainer screen, box management, .pk/Showdown
transfer, Pokedex browse UI, the original CAPABILITY-GAPS.md audit, Gender/PP/Ball/Friendship
editing, Markings, Origin/Met data, Is Egg, Box wallpaper, the first two bug-class discoveries)
has been trimmed from this file now that it's fully superseded by the summary above and the
detailed write-ups already in `PROGRESS.md`. Nothing in that history is still an open task —
everything actionable from it is folded into "Next steps" above. If you need the blow-by-blow,
`git log` and `PROGRESS.md` have it; this file's job is to be the current, actionable snapshot,
not a full session archive.*
