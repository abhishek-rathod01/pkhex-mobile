# Handoff: PkhexMobile Design System

## Overview
PkhexMobile is a touch-first mobile front-end for the PKHeX.Core save-editing engine — viewing and editing Pokémon save data (party, stats, moves, met info, trainer/OT data) across nine generations of games. This bundle is the complete visual design system: tokens, reusable components, and two fully composed screens (Party list, Detail/inspector), plus a reference sheet documenting every interactive state and edge case.

## About the design files
Everything in this bundle is an **HTML/React design reference** — prototypes built with inline styles and CSS custom properties to communicate the intended look, spacing, and behavior. None of it is production code to copy verbatim. The task is to **recreate these designs in the target codebase's real environment** — the app is planned as native/MAUI, so implement the visual system (colors, type, spacing, radii, shadows, motion) and component behavior using that platform's real UI primitives, not by embedding this HTML/React. If no environment is set up yet, choose the framework best suited to the target platform and implement there.

## Fidelity
**High-fidelity.** Every color, font, spacing value, radius, shadow, and interaction timing below is final — implement pixel-perfectly, not just "in the spirit of."

## Files in this bundle
```
styles.css                        global entry point (imports all tokens)
tokens/
  colors.css                      neutrals, brand red, semantic, stat, shiny
  type-colors.css                 18-color muted Pokémon type palette
  typography.css                  font families, type scale, weights, semantic roles
  spacing.css                     4px base grid + semantic spacing
  radii.css                       corner radius scale
  shadows.css                     elevation ramp + motion (easing/durations)
  fonts.css                       Google Fonts import (Space Grotesk / Manrope / JetBrains Mono)
components/
  controls/Button.jsx, IconButton.jsx, Switch.jsx
  surfaces/Card.jsx, DataRow.jsx, SectionHeader.jsx, SpriteSlot.jsx, StatBar.jsx
  indicators/Badge.jsx, Chip.jsx, LegalityBadge.jsx, TypeBadge.jsx
  (each .jsx has a sibling .d.ts with full prop docs)
ui_kits/pkhexmobile/
  PartyListScreen.jsx             scrollable party roster
  DetailScreen.jsx                full Pokémon inspector (collapsible cards)
  chrome.jsx                      shared status bar + bottom tab nav
  icons.jsx                       Lucide-style stroke icon set
  data.js                         mock save data feeding the screens
  index.html                      interactive phone-frame demo (party → detail → back)
  states-and-edge-cases.html      ★ component states + empty/edge-case reference (see below)
```

## ★ States & edge cases reference
Open `ui_kits/pkhexmobile/states-and-edge-cases.html` in a browser — it is the single source of truth for:
- **Button** — 4 variants (primary/secondary/ghost/danger) × 4 states (default/hover/pressed/disabled)
- **IconButton** — 3 variants (primary/outline/ghost) × 4 states
- **Switch** — off/on × enabled/disabled
- **Save button** — disabled when the screen is clean (no edits), enabled the instant any field becomes dirty; live toggle demo included
- **Party row & collapsible section** — press tint/scale, chevron rotation + height animation on expand/collapse
- **Held-item display** — party row (compact icon + first word) vs. detail screen (full name in a Chip) vs. empty ("No item" / "None")
- **Empty states** — no nickname (species becomes the primary line), no held item
- **Gen 1/2 genderless Pokémon** — Mewtwo (Gen 1) and Voltorb (Gen 1) render a genderless glyph instead of Male/Female; Ability, Nature, SID, and Met date are also absent/relabeled for pre-Gen-2 data
- **Party size** — 1/6 (sparse, add-slot affordance visible) vs. full 6/6

## Interactions & behavior (full detail)

### Button
- **Default**: filled poké-red (`--accent`) for primary/danger; outline slate for secondary; transparent for ghost.
- **Hover**: fill steps one shade darker (`--accent-hover` / `--accent-active` for danger) or a `--slate-100` tint for secondary/ghost.
- **Pressed**: `transform: scale(0.97)`, 120ms (`--dur-fast`, `--ease-standard`).
- **Disabled**: `opacity: 0.5`, `cursor: not-allowed`, pointer events blocked; no hover/press response.
- Sizes: sm 34px / md 44px / lg 52px tall; radius `--radius-control` (12px).

### IconButton
Same variant/state logic as Button but press scale is tighter: `scale(0.92)`. Square hit areas 34/44/52px matching Button heights. Always set an accessible `label` (renders as `aria-label`).

### Switch
- Track: `--accent` when on, `--slate-300` when off. Thumb slides via `left` 3px↔21px over 200ms (`--dur-base`, `--ease-out`).
- Disabled: track flattens to `--slate-200`, whole control drops to `opacity: 0.6`, click ignored — position is preserved so state is still legible at a glance.

### Save button (dirty/clean)
The primary Save/commit action anywhere edits happen (bottom action bar, future edit sheets) must:
1. Load **disabled** whenever the current screen has no unsaved changes (clean).
2. **Enable** the instant any field is edited (dirty) — track dirty state per screen/form, not globally.
3. Return to **disabled** immediately after a successful save.
This prevents accidental no-op writes to the save file and gives a clear "there's unsaved work" signal.

### Party row
- Default: white card (`--surface-card`), 1px hairline border (`--border-subtle`), `--shadow-card`.
- Pressed: background shifts to `--surface-sunken`, `transform: scale(0.99)`, 120ms.
- Tap opens the Detail screen.

### Collapsible section (SectionHeader + Card)
- Header row is the full-width tap target when `collapsible`.
- Chevron rotates 180° on open, `--dur-base` (200ms).
- Content wrapper animates `max-height` 0 → content height over `--dur-slow` (320ms), `--ease-standard`.
- Main and Stats sections default **open**; Met info and OT/Misc default **collapsed**.

### Held item
- **Party row**: `Bag` icon (13px) + first word of item name only, `--text-tertiary`, 11px semibold — space is tight in the row.
- **Detail screen (Main card)**: full item name inside a `Chip` with a `Bag` icon — the Chip only renders when an item exists.
- **Empty, party row**: no icon at all — plain "No item" in `--slate-300`, avoids implying an item is present.
- **Empty, detail screen**: muted "None" text, `--text-tertiary`, no Chip.

### Empty states
- **No nickname**: species name promotes to the primary (bold, display-font) line; if a nickname exists, species drops to a secondary caption instead. Detail screen's Nickname `DataRow` shows "Not set · {species}" in muted tertiary text when absent.
- **No gender (Gen 1)**: a filled "genderless" glyph (circle + short stem) in `--text-tertiary` replaces the Male (blue, `--stat-spa`) / Female (pink, `--stat-spe`) icon, both in the party row and the detail hero.
- **No held item**: see above.
- **Gen 1/2 fields that don't exist**: Ability, Nature, held item, SID, and Met date/level render as muted "— not in Gen {n}" or "Not recorded" text rather than being hidden — the row stays in place so the layout doesn't jump between generations. Gen 1 also swaps EV (0–252) for Stat-EXP (0–65535) and caps IV at 15 instead of 31; the Stats section header reflects the correct ranges per save.
- **Party size**: 1–6 members supported; below 6 an always-visible dashed "Add Pokémon to party" affordance fills the remaining space. Header badges ("X/6 slots", "Y legal", "Z to review") recompute from the live roster.

### Motion (system-wide)
- `--dur-fast` 120ms — press feedback.
- `--dur-base` 200ms — toggles, chevrons.
- `--dur-slow` 320ms — stat-bar fills, section expand/collapse.
- Easing: `cubic-bezier(0.2, 0.8, 0.2, 1)` standard; `cubic-bezier(0.16, 1, 0.3, 1)` for the switch thumb. No bounce/spring anywhere.

## Design tokens

### Color
```
Neutrals (cool slate): --slate-0 #FFFFFF … --slate-950 #0B0F16 (12 steps)
Brand red: --red-50 #FFF1F1 … --red-800 #8B1B1F — --accent = --red-500 #E5484D
Semantic: green (pass) #D6F5E6/#16A46B/#0E8556, amber (warn) #FCEFCB/#E7960B/#C07C05,
          red (fail) reuses brand red-100/700, blue (info) #DBEAFE/#3B7DED/#2A63C4,
          gold (shiny) #FBEFC5/#F2C24B/#E7A812
Stat accents: HP #EB5A54 · Atk #F0913E · Def #F2C94C · SpA #62A9F5 · SpD #58C48A · Spe #E06BC9
Type palette: 18 muted hues, each with a soft background tint — see tokens/type-colors.css
Aliases: --bg-app (slate-50), --surface-card (white), --surface-sunken (slate-100),
         --text-primary/secondary/tertiary (slate-900/600/500), --border-subtle/default (slate-200/300)
```

### Typography
- Display: **Space Grotesk** (screen/section titles, tight tracking `-0.02em`)
- Body: **Manrope** (UI text, labels)
- Mono: **JetBrains Mono** (all numeric/ID data — levels, IVs/EVs, TID/SID, dates, PID hex)
- Scale (1.20 ratio): 11 / 12 / 13 / 15 / 16 / 18 / 21 / 25 / 30 / 36 px
- Weights: 400/500/600/700/800

### Spacing
4px base grid: 4·8·12·16·20·24·32·40·48·64. Semantic: 16px screen gutter, 16px card padding, 12px between stacked cards, 44px minimum touch target.

### Radii
xs 6 · sm 8 · md 12 (controls) · lg 16 (cards) · xl 20 · 2xl 28 · pill 999 (chips/badges/switch)

### Shadows
xs/sm/card/raised/overlay — all cool-tinted (`rgba(20,26,35,…)`), no hard or colored shadows. Shiny Pokémon use a gold ring (`0 0 0 3px var(--gold-100)` + gold border) instead of a shadow.

## Content rules
- Sentence case for labels/buttons ("Add move"); ALL-CAPS only for tiny tracked section labels and status pills.
- Numbers/IDs always monospace.
- Exact PKHeX/Pokémon terminology — never simplified ("Held item", "OT", "TID / SID", "Nature", "Stat-exp").
- No emoji anywhere — meaning carried by icon + color.

## Assets
- **Icons**: Lucide (ISC license) stroke icons, hand-inlined in `icons.jsx` for this prototype — production should load Lucide from its npm/CDN package rather than copying these inline paths.
- **Sprites**: `SpriteSlot` is a placeholder frame (neutral background + Poké Ball glyph until a `src` is supplied). No sprite artwork is included — production supplies real sprite images.
- **No logo** was supplied; the wordmark "PkhexMobile" set in Space Grotesk stands in for a brand mark.

## Not yet designed (roadmap)
Box-grid view, full edit forms (Switch/Chip already support the visual language for editable state), a wired legality-check engine (`LegalityBadge` UI is ready, logic is not), inline field editing beyond the reserved edit-button affordances.
