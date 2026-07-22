# CLAUDE.md — working context for PkhexMobile

A .NET MAUI (Android-first) app wrapping a vendored `PKHeX.Core` (`vendor/PKHeX.Core`) to
view and edit Pokémon save files across Gen 1-9.

**Companion docs:** `PROGRESS.md` (feature-by-feature technical history — the *why* behind
every decision), `WAKEUP.md` (session handoffs + environment notes), `CAPABILITY-AUDIT.md`
(source-cited map of PKHeX.Core capabilities vs. what the app exposes, with per-generation
caveats and priorities).

This file is the distilled, load-bearing stuff: things that have already cost real time or
caused real bugs. Read it before writing code.

---

## 1. Build and deploy

```bash
# Build (the bar is 0 warnings / 0 errors — hold it)
dotnet build PkhexMobile/PkhexMobile.csproj -f net10.0-android -c Debug

# Build + deploy + launch on the emulator. This is the ONLY reliable deploy path.
dotnet build PkhexMobile/PkhexMobile.csproj -f net10.0-android -c Debug -t:Run
```

- **Never `adb install` the Debug APK directly.** It crashes on launch with `SIGABRT` /
  *"No assemblies found in `.../files/.__override__/x86_64`... Assuming this is part of Fast
  Deployment."* Debug builds expect the tooling to push assemblies separately; a bare
  `adb install` never does. This cost ~40 minutes once.
- **`-t:Run` sometimes reports "up to date" without actually redeploying.** If a UI change
  doesn't appear, force-stop and relaunch, or uninstall first:
  ```bash
  adb shell am force-stop com.companyname.pkhexmobile
  adb shell am start -n com.companyname.pkhexmobile/crc64b42f7bba2754976c.MainActivity
  ```
- Emulator: AVD `PkhexMobile_Emulator` (API 36). **There is only one** — see §8.

---

## 2. PKHeX.Core API traps (each of these was a real bug)

| Trap | Rule |
|---|---|
| `SaveUtil.GetSaveFile(byte[])` **wraps** the array as `Memory<byte>`, it does not clone | Clone the array before passing it, or later `Write()` calls mutate your "original" copy in place and byte-comparisons silently pass |
| Party stat block goes stale on edit | `SaveFile.SetPartyValues` only calls `ResetPartyStats()` when `!pk.PartyStatsPresent` (`SaveFile.cs:316-323`), which is **false for every real party mon**. Call `pk.ResetPartyStats()` **explicitly** after any species/level/IV/EV/nature/form change |
| `CurrentLevel` is stored as EXP, interpreted via the species' growth rate | **Set `pk.Species` before `pk.CurrentLevel`.** Reversed, a level set under the old growth curve is reinterpreted under the new species' curve |
| Raw `Move1..4` leaves stale PP | Use `pk.SetMoves(...)`, which recomputes PP |
| Gen 8+ "Mint" mechanic | `PKM.LoadStats`/`ResetPartyStats` read **`StatAlignment`**, not `Nature`, for the stat-boost calc when `Format >= 8`. Setting `Nature` alone leaves stats completely unmoved — sync both |
| Party storage has **no gaps** below `PartyCount`; boxes **can** have holes | The only valid empty party target is index `PartyCount` (append). Vacating a middle party slot must close the gap (`DeletePartySlot` shifts down). `SetPartySlotAtIndex` throws above index 5 |
| Party and box are **separate address spaces** | `GetPartyOffset` vs `GetBoxSlotOffset` are unrelated. Never route a box-sourced mon through a party-index write. See `PokemonSlotMover.cs` — that's the verified core; don't bypass it |

**Navigation:** passing a `SaveFile` or `PKM` through Shell's `GoToAsync` query dictionary
crashes with `InvalidCastException: Object must implement IConvertible` — Shell coerces
dictionary values while resolving the route, before the destination page ever sees them.
Use the static hand-off in `PkhexMobile/NavigationState.cs`. This only reproduces at runtime
on-device, never in `dotnet build`.

---

## 3. THE recurring bug class — per-generation silent no-ops

> A setter exists to satisfy the abstract `PKM`/`SaveFile` contract, but that generation
> genuinely doesn't store the field — so it silently does nothing. No exception, no signal.

This has bitten the project **five+ times**. Assume nothing; verify per generation.

**Confirmed against real saves:**

| Field | Reality |
|---|---|
| Gen 1/2 IVs | 4-bit DVs (**0-15**, not 0-31). `IV_HP` has no storage — derived from the low bits of ATK/DEF/SPE/SPC. `IV_SPD`/`EV_SPD` mirror SPA (one shared Special stat). `GBPKM.cs:185-191` |
| Gen 1/2 EVs | 16-bit **Stat Exp (0-65535)**, not the modern 0-252 system |
| Gen 3 | `Nature`/`Ability`/`Gender` setters are no-ops (PID-derived). `CurrentFriendship` **aliases** `OriginalTrainerFriendship` (`G3PKM.cs:39`) |
| Gen 4 | `Nature` still a no-op, but `Ability` and `Form` are **real** — same-generation split, don't pattern-match from Gen 3 |
| Nature / Form / Ability | Real from **Gen 5** / **Gen 4** / **Gen 4** respectively |
| Gen 1 held item | Hard no-op — that byte is the **catch rate** (`PK1.cs:157`) |
| Gen 1 friendship | No-op (`PK1.cs:155`) |
| Gen 1/2 Ball | No-op (`GBPKM.cs:135`) |
| Gen 2 items | **Different item ID space** — sprite/name lookup must route via `SpriteItem`, not `HeldItem` |
| `SAV7b` (Let's Go) | Does **not** implement `IBoxDetailName` — and the save inventory contains one |
| `SAV3RSBox` | Does **not** override trainer fields — gate on a **capability probe, not `Generation`** |

**Empirically refuted theory, kept as a warning:** "box slots never carry a party stat block."
False — a fresh box read on real Gen 9 Scarlet returns `Stat_HPMax=12`, `PartyStatsPresent=true`.
Gen 1/5 return 0. This is why box→party moves call `ResetPartyStats()` **unconditionally**
rather than trusting the library's gate.

### Established UI treatment (follow it)

**Disable the control, still show the true current value, explain why inline.**
Never silently accept an edit that has no effect. Examples already in the codebase: Gen 1/2
HP/SpD IV fields disabled and live-derived; Gen 3/4 Nature picker disabled with a caption.
Live-clamp out-of-range input as the user types so bad values can't be entered at all — not
just rejected at save time.

**Deliberately not done:** PKHeX.Core's `SetPIDNature` *can* force a Nature on Gen 3/4 by
rerolling the PID — but it **de-shinies the mon** as a side effect. Too surprising to hide
behind a plain picker. Same call for Gen 1-3's Unown-only Form setter.

---

## 4. Verification methodology (this project's standard)

1. **Console harness per feature**: `verify/<Name>/Program.cs` + `.csproj`. Model new ones on
   `verify/FormNatureAbilityEdit/Program.cs`. Where it tests a production class, link the real
   file via `<Compile Include>` rather than reimplementing it (`verify/BoxPartyMove` does this).
2. **Round-trip for real**: set → `sav.Write()` → reload via `SaveUtil.GetSaveFile(byte[])` →
   confirm persistence. Anything that doesn't survive that is not verified.
3. **Assert actual stat *values*, not just that a getter returns what you set.** Both of the
   last two genuine bugs (stale stat block, Gen 8+ Mint) were invisible to getter checks and
   only surfaced because a harness asserted computed HP/Atk.
4. **Confirm the original file on disk is byte-for-byte untouched** after every run.
5. **Real saves** live in `C:\Users\abhis\Downloads\sav files pkmn` — full inventory table in
   `PROGRESS.md`. Gen1 `POKEMON RED-0.sav`, Gen2 Crystal, Gen3 `pokeemerald (2).sav`,
   Gen5 `Pokemon Black Version.sav`, Gen9 `pkmnscarlet_100\main` are the usual suspects.
6. **Always distinguish "on-device verified" from "library-harness only"** in write-ups. The
   project has a documented precedent (the Shell `InvalidCastException`) of a bug that only
   appeared on-device and never in a build. Don't let harness success imply device success.
7. Keep harnesses that cover production code; delete one-off throwaways after use.

---

## 5. UI / design conventions

- Design tokens live in `Resources/Styles/`: `Colors.xaml` (neutrals, brand, semantic, **18-type
  palette** with paired `TypeFire`/`TypeFireBg` accent+tint), `Tokens.xaml` (spacing, radii, type
  scale, motion, shadows), `Styles.xaml` (text roles, 4 button variants, card/row surfaces).
  **Reuse these — don't invent colors or spacing.**
- Every input gets an uppercase `LabelCaptionStyle` caption above it.
- Save buttons are dirty/clean tracked: disabled when clean → enabled on first edit → disabled
  after a successful save. Guard programmatic population with the `isLoading` flag so it isn't
  misread as a user edit.
- The design system is **light-only** — no dark tokens were ever provided, so there's no
  `AppThemeBinding` dark branch. That's a scope fact, not an oversight.
- Sprites: species `Resources/Images/species/spr_{dex:D4}[_s].png`, items
  `Resources/Images/items/item_{pkhexItemId:D4}.png`. **Item files are keyed by PKHeX's item ID
  space** (from `text_Items_en.txt`, ID = line number − 1) — pokesprite's own numbering does
  *not* correspond. Missing sprites need no handling: a MAUI `Image` that can't resolve its
  source renders nothing, and a placeholder layered *behind* it shows through. Never maintain a
  hardcoded valid-ID list.
- XAML resource names must be lowercase, start with a letter, and contain only `[a-z0-9_]`.
- **A literal `--` anywhere inside an XML comment breaks XAML parsing** (`MAUIG1001`) — including
  when quoting CSS custom property names like `--role-body`.
- `PkhexMobile.csproj`'s `MauiImage` glob is recursive (`Resources\Images\**\*`) with
  `Resize="False"` on sprite folders so pixel art isn't upscaled/blurred.

---

## 6. Emulator / ADB automation

- **Coordinate scaling:** `adb shell screencap` output is full device resolution (1080×2400);
  the image as *displayed* in tool output is downscaled (900×2000). **Multiply displayed
  coordinates by 1.2** before `adb shell input tap`. A mis-scaled tap looks exactly like "the
  app ignored my tap."
- **Prefer `adb shell uiautomator dump`** and parse `bounds="[x1,y1][x2,y2]"` — those are already
  real device coordinates, no scaling needed, and far more reliable than reading a screenshot.
- **Re-dump after any state change that alters content height** — a status label appearing or a
  button changing visibility moves every element below it.
- **FileSaver filename-autocomplete hazard (hit twice):** the system save dialog can pre-fill or
  autocomplete the filename to an existing file — including the *original save being edited*.
  Always clear it, type a distinct name, and verify via `uiautomator dump` before tapping SAVE.
- The file picker's default folder drifts between runs (Download vs Documents vs root). Don't
  assume a starting location; check the breadcrumb.
- **Drag-and-drop cannot be driven via ADB.** `input swipe` and `input draganddrop` both fail to
  trigger MAUI's `DragGestureRecognizer` (4 attempts, no crash, no effect). The recognizers *are*
  correctly wired in XAML — this is an automation limitation, not an app bug. Drag needs a human.

---

## 7. Repo hygiene

- **Never commit `.sav` / `.bin` save files.** They're derived from personal game saves and carry
  trainer name, ID, and full party/box contents — and this repo is pushed publicly. Covered by
  `verify/**/*.sav` in `.gitignore`. Screenshots and `uiautomator` XML dumps are fine.
- Remote: `github.com/abhishek-rathod01/pkhex-mobile`. `vendor/PKHeX.Core` is vendored
  third-party code — see the README license notice.
- CI (`.github/workflows/ci.yml`) builds `vendor/PKHeX.Core` and runs the 9 self-contained
  `verify/Gen1..9` harnesses. The 5 harnesses that hardcode local save paths are deliberately
  excluded and documented in the workflow.

---

## 8. Working with parallel subagents (hard-won this session)

- **Subagent self-reports can be confidently wrong.** Independently verify anything that drives a
  downstream decision. Two separate agents wrongly reported sprite coverage as unavailable
  ("pokesprite stops at #809" — actually #905; "PokeAPI has no Gen 9" — it has all of #906-1025).
  Both were caught by a 30-second direct check. Re-run their harness; read their diff.
- **Unattended permission prompts kill background agents silently.** A `git clone` agent was lost
  this way with the user away. Prefer command patterns already proven in this repo; avoid novel
  network operations in unattended runs.
- **Usage-limit cutoffs kill agents mid-run.** Files on disk survive, so work is recoverable —
  but several agents' *uncommitted* half-edits intermingled in one shared tree are painful to
  untangle. Therefore: **commit incrementally, one commit per feature as it compiles**, and
  **push early** so work is durable off-machine.
- **Assign strict, disjoint file ownership.** One agent per file, always. Shared files
  (`MainPage.xaml`, `AppShell.xaml.cs`, `Styles.xaml`, `PROGRESS.md`, `WAKEUP.md`) are the real
  collision risk — have agents *report* the wiring they need and let the orchestrator apply it in
  a single integration pass.
- **On-device verification cannot be parallelized** — one emulator. Grant it to exactly one agent;
  everyone else does library-level verification, and the orchestrator runs a consolidated
  on-device pass at the end.
- The Edit tool's read-before-edit requirement is a useful lost-update guard on shared files: it
  fails if the file changed underneath. Re-read and retry; never work around it.

---

## 9. Standing scope decisions

- **No auto-legalization, no auto-fix.** Explicit, repeated user instruction. `LegalityAnalysis`
  is wired **read-only** (green LEGAL / red ILLEGAL + `la.Report()`), recomputed on load and after
  a successful save — not per keystroke, since encounter matching isn't free. Species/move/form/
  nature/ability edits are applied exactly as chosen, behind a persistent "applied as-is; other
  tools may flag this as illegal" warning.
- Editing is **direct-inline** (`Entry`/`Picker`), not the read-only-inspector-plus-edit-mode
  paradigm in the design bundle. That bundle's inspector was an unwired prototype; adopting it
  would have meant deleting working, verified controls.
- Box (PC) mons opened from the box view are read-only via a **structural** guard: a `null`
  parent `SaveFile` hides the Save button entirely. That's deliberate, not incidental.
