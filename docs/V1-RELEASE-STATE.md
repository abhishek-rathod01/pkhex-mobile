# v1.0.0 release state — what is done, what is unverified, what blocks the tag

Written 2026-07-28 from a cloud session with **no .NET SDK, no Android SDK, no
emulator and no device**. Every claim below is labelled with how it was checked.

---

## The one thing blocking the tag

`release.yml` hard-fails unless all four signing secrets exist:

```
ANDROID_KEYSTORE_BASE64  ANDROID_KEYSTORE_PASSWORD
ANDROID_KEY_ALIAS        ANDROID_KEY_PASSWORD
```

They do not exist yet. **Tagging `v1.0.0` right now produces a failed workflow and
no release.** That failure is deliberate — the alternative is an unsigned APK that
no device will install, discovered on your phone instead of in CI.

Follow `docs/RELEASE-SIGNING.md`: create the keystore, base64 it, upload the four
secrets, then tag. No keystore or password was generated for you, by design — the
signing key must never pass through an AI session or a repo.

Once the secrets exist:

```bash
git tag v1.0.0
git push origin v1.0.0
```

**Do not lose the keystore.** Without it you can never ship an upgrade to anyone who
installed v1.0.0; they would have to uninstall and lose app data.

---

## Verification levels used here

| Level | Meaning |
|---|---|
| **CI-compiled** | Built, packaged and signed by GitHub Actions. Not executed. |
| **Source-verified** | Reasoning checked against vendored PKHeX.Core source. Not executed. |
| **Static analysis** | Derived from reading assets/bundles. Weaker than a harness. |
| **Device-verified** | Ran on real hardware. **Nothing in this release is at this level.** |

---

## Shipped in v1.0.0

### In-app update checking — CI-compiled
Home → Updates. Installed version, auto-check toggle (default on), manual
**Check now**, and an update card with version, notes, download size, progress and
cancel, plus **Update / Later / Skip this version**.

- Queries the GitHub Releases API unauthenticated, with the `User-Agent` GitHub
  requires (it 403s without one).
- At most one automatic network check per 24h, persisted in `Preferences`. Manual
  checks bypass the gate, the enabled flag and any skipped tag.
- Version comparison is component-wise and numeric, handles differing component
  counts, SemVer pre-release ordering (`1.2.3-beta` < `1.2.3`, but
  `1.3.0-beta` > `1.2.9`), `+build` metadata, and degrades to *unknown* rather than
  throwing on malformed input.
- **Every** failure path — offline, timeout, rate limit, malformed JSON, no
  releases — collapses to unknown. An automatic check renders nothing at all in
  that case: no error, no spinner, no hang. Unknown is surfaced only on a *manual*
  check, where you pressed a button and deserve an answer.
- Async throughout. No `.Result`, no `.Wait()`, no `Task.Run` wrapping sync work,
  nothing touching UI off-thread.
- Downloads stream to `CacheDirectory` with progress and cancel, delete partial
  files, and verify on-disk size against the published asset size before offering
  install.
- **Nothing auto-installs.** Download is an explicit press; Android's package
  installer confirms again. Unsaved edits trigger a warning first.

### Release + test-build + emulator workflows — partially exercised
- `release.yml` — tag-triggered, fail-fast on missing secrets, versionCode
  `major*10000 + minor*100 + patch` with a monotonicity guard, JDK 21 pinned,
  keystore decoded to `RUNNER_TEMP` and deleted in `if: always()`. **Never run.**
- `test-build.yml` — **green**. Produces a downloadable APK on every push. Signed
  with a throwaway key, so it will not upgrade in place in either direction.
- `emulator-smoke.yml` — boots API 34, installs, launches, navigates to Updates,
  fails on a dead process or a fatal logcat exception. Written this session.

### Bug fixes — source-verified
1. **Silent IV/EV corruption on VC-transferred Gen1/2 mons in Gen7+ saves.**
   `PokemonDetailPage` gated Gen1/2 handling on `p.Generation`, which is *origin*-
   derived: `PKM.Generation` tests `Gen7 || GG` before `VC1`, and `Gen7` matches
   `Version is SN/MN/US/UM`, so a VC Pikachu keeps `Version == RD`, falls through to
   `VC1`, and reports Generation 1 while actually being a `PK7` with `MaxIV 31`.
   Editing *anything* — even a nickname — replaced a real 31 `IV_HP` with a 4-bit
   value synthesised from other stats and clobbered `IV_SPD`/`EV_SPD`. Now gated on
   `p.Format`.
2. **Export during a background Sort/Clear.** `SetBusy` disabled every entry point
   except Export, which calls `Write()` (re-checksum, re-encrypt) on the UI thread
   while `Task.Run` permutes boxes underneath it — producing a `.sav` with
   duplicated/lost Pokémon and a checksum not matching its payload.
3. **Transitive UI-thread ANR in Showdown apply.** `ImportShowdown` reaches
   `ApplySetDetails`, which constructs a full `LegalityAnalysis`. The earlier ANR
   audit grepped only for direct `new LegalityAnalysis` calls and missed it, so
   **`WAKEUP.md`'s claim that the ANR audit was exhausted was wrong.**

### 3D viewer — ported, **shipped OFF**
Ported by *reading* `3d-models-experimental` (`git show`), never merged — that
branch is confirmed **not** an ancestor of HEAD, so its 214MB of `.glb` blobs stay
out of history. No model asset is tracked.

**Discolouration root cause (static analysis):** `<model-viewer>` defaults
`tone-mapping` to ACES Filmic, which desaturates already-pale game-rip albedo and
withholds model-viewer's own ×1.3 exposure compensation on that path — flat and
~23% dark. Fixed with `tone-mapping="neutral"`, `environment-image="neutral"`,
`exposure="1"`.

The `EXT_texture_webp` theory is **refuted**: model-viewer registers that extension
by default; the reported flat-tan Charizard *is* its body texture's modal colour
`#EFAB62`; a dropped texture would render white (68 of 70 sampled materials carry no
`baseColorFactor`); and Draco geometry decoded on-device, proving `blob:` URLs and
Workers worked. sRGB plumbing is correct too.

Feature flag is **off** because rendering has never been seen working. Open
questions: whether `HttpListener` works on .NET for Android (~60% per analysis), and
whether the WebView loads a cleartext loopback URL without a
`network_security_config.xml` opt-in (~25%, and the more likely blocker — it is
**not** yet added).

**Separate real defect:** `Resources/Raw/model3d/README.md` claims nothing loads
from a CDN. False — Draco points at `gstatic.com`, no decoder is vendored, and all
933 models declare `KHR_draco_mesh_compression` as required, so every model needs a
live network fetch to render. Blocker for offline use. Recorded, not fixed.

---

## Requires a human on real hardware — none of this is verified

1. The install intent firing and the OS install prompt appearing.
2. The unknown-sources permission prompt and its settings screen on your ROM.
3. **`FileProvider.GetUriForFile` not throwing** — it raises
   `IllegalArgumentException: Failed to find configured root` at runtime if the
   cache path isn't covered by `file_paths.xml`. **No compile catches this. Highest
   risk item in the release.** `emulator-smoke.yml` greps for it specifically.
4. Download → install end to end over a real network.
5. **Whether an update actually replaces the installed app.** Needs two
   same-key, different-version APKs. A test-build APK and a release APK are signed
   with *different* keys and will fail with `INSTALL_FAILED_UPDATE_INCOMPATIBLE`.
6. 3D rendering, and whether the tone-mapping fix looks right.
7. Drag-and-drop box moves — MAUI's `DragGestureRecognizer` does not respond to
   `adb input swipe` (documented across several sessions). Needs a human finger.
8. Any performance or startup-time claim. Nothing was profiled.

---

## Open decision for the maintainer

`ApplySetDetails` also calls `SetRandomEC`, rerolls the PID via `SetIsShiny`
(de-shinying a shiny mon, and on Gen3-5 clobbering the nature the set just asked
for), fills hyper-training data, and auto-fills relearn moves from a
`LegalityAnalysis`. That is auto-legalisation, which CLAUDE.md §9 rules out and
which `EntityTransferService.cs`'s own header claims never happens. Avoiding it
means hand-rolling a field-by-field apply instead of using PKHeX.Core's. Not done —
it is a scope decision, not a bug fix.
