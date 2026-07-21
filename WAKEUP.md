# Wake-up summary — UI reskin shipped (Task 1 of 3), Tasks 2/3 in progress

**If you're reading this mid-run**: this file gets rewritten at the end of
each task in the queue below. Check the "Queue status" section first to see
what's actually done vs. still pending — don't assume this whole file
describes finished work.

## Queue status

A 3-task overnight run was queued (user going to sleep, pre-authorized
autonomous execution — builds/emulator/git commits handled without stopping
for approval, per instructions in this session):

1. **✅ DONE, committed.** UI reskin: design system, functional buttons,
   sprites, held items, app icon. Verified on-device across Gen1/5/9,
   regression-free. Full detail in `PROGRESS.md`'s "UI reskin" section.
2. **Read-only legality badge** on the detail screen via PKHeX.Core's
   `LegalityAnalysis`, specifically tested against a Pokémon edited via the
   species/move editor (to confirm it actually flags issues that edit
   introduces, not just a static "looks fine" check).
3. **Extend the design system to box view + edit forms** (box view mostly
   already done as part of Task 1's `BoxListPage` restyle — this task is
   about closing any remaining gaps and re-confirming consistency).

Rule for this run: if the same error recurs 3× in a row on any task, stop
immediately, log full details right here, and do **not** attempt the next
task. Each task requires on-device verification + commit + PROGRESS.md
update before the next one starts.

## Task 1 — what shipped (commit: see `git log`)

Full visual reskin of every screen against a separately-authored design
handoff bundle (`PkhexMobile Design System/design_handoff_pkhexmobile/`),
kept strictly to "reskin + button wiring, no data-logic changes" per
explicit instruction. Full write-up in `PROGRESS.md`; short version:

- **Design tokens → MAUI**: `Colors.xaml` (full color-token port),
  `Tokens.xaml` (new — spacing/radii/type-scale/motion/shadow resources),
  `Styles.xaml` (rewritten — text-role styles, 4 button variants matching
  the design's disabled/pressed states, card/row surface styles).
- **Fonts**: Space Grotesk/Manrope/JetBrains Mono, self-hosted as 12 static
  weight `.ttf` files generated from Google Fonts' variable-font sources via
  `fonttools` (no static files are published upstream anymore).
- **Scope call, made without stopping to ask** (documented in `PROGRESS.md`
  under "Scope decision"): the design mockup's `DetailScreen.jsx` is a
  read-only inspector with unwired Edit/Verify/LegalityBadge affordances —
  explicitly marked "not yet designed" in the bundle's own README. Adopting
  that paradigm would have meant removing the app's working direct-edit
  `Entry`/`Picker` controls to match an inert prototype. Kept the direct-edit
  form, applied the visual language to it, did not build the inert parts.
  An advisor consult before starting confirmed this reading of the
  instructions.
- **New behavior (not just style)**: Save button dirty/clean tracking per
  design-notes.md's spec — disabled while clean, enabled the instant any
  field changes, disabled again after a successful save. This is the one
  actual behavior change in Task 1; everything else preserves existing
  logic exactly.
- **Sprites**: species icons (Dex #1–905, regular+shiny) and held-item
  icons (933/2683 PKHeX item IDs matched by name, not by pokesprite's own
  numbering — those don't correspond) vendored from `msikma/pokesprite`.
  Missing sprites (Gen9 species #906+, ~65% of item IDs) fall back to a
  placeholder glyph via a layering trick (placeholder image behind a
  same-slot `Image` bound to the computed filename — a failed resource
  resolution just renders nothing, so the placeholder shows through with no
  broken-image glyph or crash). No hardcoded valid-ID list to go stale.
- **App icon**: original Poké Ball SVG (not copied from any source), wired
  via `MauiIcon`/`MauiSplashScreen`, plus Android `colorPrimary` etc.
  updated so system chrome matches. README attribution note added
  (Nintendo/Creatures Inc./GAME FREAK Inc. imagery, fan project, no
  affiliation implied).

### Environment notes for this session (things that cost time — read before repeating)

- **Background subagents and unattended permission prompts don't mix.** The
  first `git clone`-based sprite-vendoring subagent got silently killed
  (status "stopped by user", would not resume via `SendMessage`) — almost
  certainly a permission prompt with nobody available to approve it in the
  background. When the user is present, foreground work or a subagent
  launched right before the user steps away works; a subagent that needs
  network/shell permissions queued for *later* unattended execution is
  risky. If starting a new overnight background job in a future session,
  prefer giving it pre-approved, narrowly-scoped commands, or do the fetch
  in the foreground first.
- **A background agent's self-report can be wrong — verify before trusting
  it.** The species-sprite subagent reported pokesprite "doesn't cover
  Gen8/9" and stopped at Dex #809. A single direct check of
  `data/pokemon.json` (cheap, ~30 seconds) showed contiguous coverage to
  #905 — the agent had read an incomplete/wrong data source and under
  delivered by 96 species. Caught before it became stale documentation;
  redeployed a second, narrowly-scoped agent that filled exactly the gap
  (#810–905) without redoing the first 809. Lesson: an agent's own summary
  of *why* it stopped is a claim, not a fact — cross-check anything that
  will drive a downstream decision (like a fallback threshold).
- **`git clone` for a design/asset repo is far faster than per-file fetches.**
  Both sprite subagents were instructed to shallow-clone `pokesprite` rather
  than fetch hundreds/thousands of individual files — this was the
  difference between ~2–3 minutes and what would have been an impractical
  number of tool calls.
- **UI-automation coordinate scaling**: screenshots pulled via
  `adb shell screencap` are full device resolution (1080×2400 on this AVD);
  the *displayed* image in tool output is downscaled (900×2000 in this
  session) and coordinates read off the displayed image must be multiplied
  by the scale factor (1.2 here) before use in `adb shell input tap`. Mixed
  up scaled vs. unscaled coordinates twice this session (tapped into empty
  space / the wrong list row) before catching it — when a tap doesn't do
  what's expected, `adb shell uiautomator dump` + parsing `bounds="[x1,y1][x2,y2]"`
  is the reliable way to get exact real-device coordinates, faster than
  re-guessing from a screenshot.
- **Deploy command**: `dotnet build PkhexMobile/PkhexMobile.csproj -f
  net10.0-android -c Debug -t:Run` (unchanged from prior sessions — still
  the only reliable way to both build and install/launch; bare `adb install`
  still crashes with Fast Deployment's missing-assemblies error).
- **XML comment gotcha**: a `<!-- ... -->` comment that echoes a CSS custom
  property name (e.g. `--role-body`) breaks XAML parsing — literal `--`
  anywhere inside an XML comment is invalid, not just at the boundary.
  Caught immediately by the build (`MAUIG1001`), one-line fix.

## What I'd do next (if the queue stops here for any reason)

Tasks 2 and 3 are next in the queue and should proceed automatically in this
same run unless a stop condition was hit — check the top of this file for
current status if resuming cold. If Task 2 or 3 shipped, this section and
the "Queue status" section above will have been rewritten to reflect that;
if you're seeing this exact paragraph, it means the run stopped after Task 1
for some reason not captured elsewhere — check for an error log appended
below.
