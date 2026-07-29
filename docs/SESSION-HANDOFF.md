# Handoff brief — paste this into a new session

Repo: `github.com/abhishek-rathod01/pkhex-mobile`
Branch with all work: **`claude/model-opus-jxytim`** (10 commits, all pushed, tree clean)
Trunk: `master`. The branch is **not** merged to master yet.

---

## 1. READ THIS BEFORE YOU PLAN ANYTHING

**A cloud session cannot build this project, and cannot be made to.**

- No `dotnet`. The SDK is unobtainable: `dot.net` 301-redirects to
  `builds.dotnet.microsoft.com`, which egress policy **blocks (403)**. `dl.google.com`
  (Android SDK) is blocked too. Verified by direct probe, not assumption.
- No `/dev/kvm`, no `vmx`/`svm` CPU flags → **no emulator can run locally**, ever.
- `curl`/`wget` are in the repo's own `.claude/settings.json` **deny** list.
- Do **not** try to route around the 403s (e.g. pulling SDK bits through allowed
  hosts). That is circumventing the egress policy. Report and ask instead.
- `scripts/claude-setup.sh` will not fix this. It deliberately never installs the
  toolchain — that is the *environment* Setup script's job, and setup scripts run
  at container **creation** and are skipped entirely on resume. Editing one does
  nothing to a running session; you need a brand-new session.
- To actually fix it: environment settings → network access → **Custom**, tick
  "include default list", add `builds.dotnet.microsoft.com` + `dl.google.com`, then
  start a **NEW** session.

**Therefore: CI is the compiler.** `test-build.yml` builds an APK on every push to
`claude/**`. Push small and often; each push is one type-check of the whole app.

General, portable version of all this: `docs/CLOUD-SESSION-GUIDANCE.md`.

---

## 2. WHAT WAS ACHIEVED

### First APK this project has ever produced
`ci.yml` historically built only `vendor/PKHeX.Core` and the nine Gen harnesses —
there was no `maui-android` workload step anywhere, and no APK had ever been built.
Now green, with a downloadable artifact on every push.

### In-app update checking (complete, compiles, unverified on device)
Home → **Updates**. Installed version, auto-check toggle (default on), manual
**Check now**, update card with version / notes / download size / progress / cancel,
and **Update · Later · Skip this version**.

- `Update/VersionComparer.cs` — component-wise numeric compare; handles differing
  component counts, SemVer pre-release ordering (`1.2.3-beta` < `1.2.3` but
  `1.3.0-beta` > `1.2.9`), `+build` metadata; never throws, degrades to Unknown.
- `Update/UpdateService.cs` — unauthenticated Releases API GET with the `User-Agent`
  GitHub requires (403s without it); ≤1 automatic check per 24h via `Preferences`;
  manual check bypasses gate + enabled flag + skipped tag; **every** failure path
  collapses to Unknown. Async throughout, no `.Result`/`.Wait()`, no UI touching.
- `Update/UpdateDownloader.cs` — streams to `CacheDirectory`, progress + cancel,
  deletes partials, verifies on-disk size vs published asset size before install.
- `Platforms/Android/ApkInstaller.cs` — FileProvider `content://` hand-off
  (a `file://` URI throws `FileUriExposedException` on API 24+), authority
  `com.companyname.pkhexmobile.fileprovider`, `REQUEST_INSTALL_PACKAGES` +
  `<provider>` + `res/xml/file_paths.xml` all added.
- Nothing auto-installs. Explicit press, then the OS confirms again. Unsaved edits
  warn first via `NavigationState.HasUnsavedChanges` (added this session; mirrors
  the previously page-local dirty flags).

### Three workflows
- `release.yml` — tag `v*`; fail-fast naming any missing secret; versionCode
  `major*10000 + minor*100 + patch` with a monotonicity guard; JDK 21 pinned;
  keystore decoded to `RUNNER_TEMP`, deleted in `if: always()`. **NEVER RUN.**
- `test-build.yml` — push to `claude/**` + `workflow_dispatch`. **Green.** Throwaway
  keystore per run, so it will not upgrade in place in either direction.
- `emulator-smoke.yml` — boots API 34 on a GitHub runner (they DO have KVM),
  installs, launches, navigates to Updates, fails on a dead process or fatal logcat
  exception. **Not yet run — run this.**

### Three real bugs fixed (each verified against vendored source before acting)
1. **Silent IV/EV corruption, shipping until now.** `PokemonDetailPage` gated Gen1/2
   handling on `p.Generation`, which is *origin*-derived: `PKM.Generation` tests
   `Gen7 || GG` before `VC1`, and `Gen7` matches `Version is SN/MN/US/UM` — so a
   Virtual-Console mon in Sun/Moon keeps `Version == RD`, reports Generation 1, yet
   is a `PK7` with `MaxIV 31`. Editing *anything* (even a nickname) replaced a real
   31 `IV_HP` with a 4-bit synthesised value and clobbered `IV_SPD`/`EV_SPD`. Now
   gated on `p.Format`.
2. **Export reachable during background Sort/Clear** → `.sav` with duplicated/lost
   Pokémon and a checksum not matching its payload. Export now disabled while busy.
3. **Transitive UI-thread ANR**: `ImportShowdown` → `ApplySetDetails` → full
   `LegalityAnalysis`. Backgrounded onto the existing clone.

### 3D viewer ported (flag OFF) + discolouration root cause found
Ported by **reading** `3d-models-experimental` (`git show`), never merged. Verified:
that branch is **not** an ancestor of HEAD, and zero `.glb`/`.gltf`/`.bin` tracked.

**Root cause of the off-colour models (static analysis):** `<model-viewer>` defaults
`tone-mapping` to **ACES Filmic**, which desaturates already-pale game-rip albedo
*and* withholds model-viewer's own ×1.3 exposure compensation on that path → flat
and ~23% dark. Fixed with `tone-mapping="neutral" environment-image="neutral"
exposure="1"`.

**`EXT_texture_webp` is REFUTED — do not re-open it.** model-viewer registers that
extension by default; the flat-tan Charizard *is* its body texture's modal colour
`#EFAB62`; a dropped texture renders **white** (68/70 sampled materials have no
`baseColorFactor`); Draco geometry decoded on-device, proving `blob:` URLs and
Workers worked. sRGB plumbing is also correct.

---

## 3. WHAT TO DO NEXT, IN ORDER

1. **Run `emulator-smoke.yml`** (Actions tab). It is the only check that executes the
   app, and it greps for `IllegalArgumentException: Failed to find configured root` —
   the FileProvider failure that is the highest-risk item in the release and that no
   compiler catches. Do this before anything else.
2. **Cut v1.0.0** — blocked on the user, deliberately. `release.yml` hard-fails until
   `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`,
   `ANDROID_KEY_PASSWORD` exist. `docs/RELEASE-SIGNING.md` has the exact `keytool` and
   `gh secret set` commands. **Never generate a keystore or password for the user.**
   Then `git tag v1.0.0 && git push origin v1.0.0`.
3. **Get a human to device-verify** the list in `docs/V1-RELEASE-STATE.md`.
4. **3D, only if the user wants it on:** add `res/xml/network_security_config.xml`
   permitting cleartext for `127.0.0.1`/`localhost` and reference it from
   `<application>`. The analysis rated "WebView refuses the cleartext loopback URL"
   (~75% likely to bite) as a *more* likely blocker than "HttpListener unsupported on
   Android" (~40%). If `CA1416` fires on the `HttpListener` lines during a build,
   that IS the answer — fall back to `WebViewAssetLoader`, which gives a real HTTPS
   origin and removes both problems.
5. **Merge to `master`** once device-verified. Use a normal merge — this branch
   carries no large binaries.

---

## 4. HARD CONSTRAINTS — DO NOT VIOLATE

- **Never `git merge`/`cherry-pick` `3d-models-experimental`.** +214MB of `.glb`
  blobs become permanent in history; `git rm` does not undo it. Port by reading.
- **Never commit** model assets, keystores, `.b64`, passwords, tokens, or `.sav`
  files (they carry real trainer data and the repo is public).
- **Never overwrite an original save.** All edits export to a new file.
- **Auto-legalisation stays out of scope** (long-standing, repeatedly declined).
- **Never reintroduce synchronous UI-thread work** — there is a documented ANR.
- **Never route around an egress 403.**

---

## 5. CORRECTIONS TO EXISTING DOCS

- `WAKEUP.md`'s claim that the synchronous-ANR audit was "genuinely done" was
  **WRONG** — it grepped only for direct `new LegalityAnalysis` calls and missed the
  transitive one. Treat "audit exhausted" claims in that file with suspicion.
- `docs/CLOUD-NOTES.md` claimed the .NET download works because
  `dotnet.microsoft.com` is allowlisted. The landing page is; the **CDN it redirects
  to is not**. Corrected.
- `CAPABILITY-AUDIT.md` remains stale — use `CAPABILITY-GAPS.md`.
- `Resources/Raw/model3d/README.md` claims nothing loads from a CDN. **False** —
  Draco points at `gstatic.com`, no decoder is vendored, and all 933 models require
  `KHR_draco_mesh_compression`. Every model needs a live fetch to render. Real
  blocker for offline use; recorded, not fixed.

---

## 6. OPEN DECISION FOR THE USER (do not decide this yourself)

`ApplySetDetails` also calls `SetRandomEC`, rerolls the PID via `SetIsShiny`
(de-shinying a shiny mon, and on Gen3-5 clobbering the nature the set just asked
for), fills hyper-training data, and auto-fills relearn moves from a
`LegalityAnalysis`. That is auto-legalisation — which CLAUDE.md §9 rules out and
which `EntityTransferService.cs`'s own header claims never happens. Avoiding it means
hand-rolling a field-by-field apply. It is a scope decision, not a bug fix.

---

## 7. VERIFICATION HONESTY — THE PROJECT'S STANDARD

Three distinct claims; never blur them:

| Level | Status in this branch |
|---|---|
| **Compiles** | ✅ every commit, CI-verified 6× |
| **Harness-verified** | ❌ none added (no local SDK to run one) |
| **Device-verified** | ❌ **nothing, at all** |

Nothing here has run on a phone. This project has a documented bug (Shell
`GoToAsync` `InvalidCastException`) that passed every harness and appeared only on
device. **Do not present a green build as device verification.**

Unverified and needing a human with real hardware: install intent, unknown-sources
prompt, FileProvider URI grant, download→install end to end, whether an update
actually *replaces* the installed app, 3D rendering and whether the tone-mapping fix
looks right, drag-and-drop box moves (MAUI's `DragGestureRecognizer` does not respond
to `adb input swipe` — needs a human finger), and any performance claim (nothing was
profiled).
