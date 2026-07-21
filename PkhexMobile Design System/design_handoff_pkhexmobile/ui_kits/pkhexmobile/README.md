# PkhexMobile — UI Kit

High-fidelity recreation of PkhexMobile's two core save-editor screens, composed from the design system's own components.

## Screens
- **PartyListScreen.jsx** — scrollable roster: sprite, nickname/species, level, dual-type chips, held-item indicator, shiny/gender marks, and a compact legality dot per row. Header carries save context + legality summary badges.
- **DetailScreen.jsx** — full inspector organised as stacked, collapsible cards mapped to PKHeX's tab groups: **Main**, **Met info**, **Stats** (generation-aware IV/EV caps), **Moves** (PP), **OT / Misc** (trainer, ribbons, memory). A legality banner sits under the hero and a sticky Verify / Edit action bar reserves room for the future legality check + inline editing.

## Support files
- `data.js` — mock party + Charizard detail (`window.PKX_PARTY`, `window.PKX_DETAIL`).
- `icons.jsx` — Lucide-style stroke icon set (`window.PKXIcons`).
- `chrome.jsx` — shared status bar + bottom tab nav (`window.PKXChrome`).
- `index.html` — mounts an interactive phone frame; tap a party row to open the detail, back to return.

Sprites use `SpriteSlot` placeholders — production supplies real sprite PNGs.
