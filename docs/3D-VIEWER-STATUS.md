# 3D viewer — status

**Status: ported, gated OFF, never seen working.**

Everything under `PkhexMobile/Model3D/` was written at source level only, in an environment with
no .NET SDK, no emulator, and no device. It has **not been compiled**, let alone run. Read the
"What could not be verified" section before you trust any sentence in the rest of this file.

`Model3DFeature.IsEnabled` ships `false`. With the gate off, the viewer page still renders
correctly — it shows a plain "3D view is turned off" card — so a stray navigation to it cannot
produce a blank screen.

---

## What was ported

| File | What it is |
|---|---|
| `PkhexMobile/Model3D/Model3DFeature.cs` | The single on/off gate. `IsEnabled` is `false`. A read-only property, not a `const`, so the guards it protects do not constant-fold into CS0162 unreachable-code warnings. |
| `PkhexMobile/Model3D/LoopbackModelServer.cs` | `HttpListener` bound to `127.0.0.1` on an ephemeral port, serving the model cache read-only. Never throws at the caller; failure becomes `IsAvailable == false` plus a user-showable `UnavailableReason`. |
| `PkhexMobile/Model3D/Model3DCache.cs` | Fetch-on-demand download of one `.glb` per species (plus the `model-viewer` JS bundle) into `FileSystem.CacheDirectory/model3d`. Async, cancellable, fails silently. **No upstream URL is configured** — see below. |
| `PkhexMobile/Model3D/Model3DViewerPage.xaml{,.cs}` | A plain `WebView` pointed at the loopback URL, with an honest-degradation status card behind it. |

Nothing else was touched: no Shell route, no `AppShell`, no `MainPage`, no `MauiProgram`, no
csproj, no manifest, no "View in 3D" button. The page is **unreachable** until someone wires it.

**No binary assets were added.** Not one `.glb`, `.bin`, or `.gltf`. The abandoned
`3d-models-experimental` branch was read with `git show` only — never merged, never
cherry-picked — precisely because its commits carry ~214MB of model blobs that `git rm` cannot
retroactively remove from history.

### The upstream model URL is not decided

`Model3DCache.ModelSourceUrlTemplate` and `Model3DCache.ViewerRuntimeUrl` are both empty string
constants, clearly marked. With them empty, every fetch returns `false` immediately and never
touches the network, and the page shows "This build has no download source configured for 3D
models." That is the intended unconfigured behaviour, not a bug.

Whoever fills them in owns three questions that are policy questions, not coding ones: the
licence of the source (the experimental branch named `github.com/Pokemon-3D-api/assets`, which
has **not** been vetted the way PokeAPI/sprites was for artwork), URL stability (pin a commit
SHA, not a branch), and size (test against species 979, ~8.2MB — the largest known model — not
species 1).

---

## What the loopback approach is meant to fix, and why

Two on-device WebView prototypes were built on `3d-models-experimental` and **both failed to
render anything** (WAKEUP.md, "3D viewer investigation", 2026-07-23 → 2026-07-25):

1. **`HybridWebView` + `HybridRoot`/`DefaultFile`.** Six recorded attempts. It ended up needing
   one pre-generated HTML file *per species* because `DefaultFile` is matched as a literal asset
   filename — neither `?query` nor `#fragment` survives it (both gave `net::ERR_INVALID_RESPONSE`,
   the fragment case proving it is not URL parsing at all). Worse, it carried a real crash:
   `PlatformView cannot be null here`, thrown from MAUI's own
   `HybridWebViewHandler.MapEvaluateJavaScriptAsync` when a `DefaultFile` change fires before the
   native WebView exists. The mitigation on that branch was never confirmed to fix it — the
   exception is rethrown on a posted async continuation, which a synchronous guard narrows but
   cannot provably close.
2. **A plain `WebView` on a `file://` URL.** Android's WebView renderer process is sandboxed and
   gets `net::ERR_ACCESS_DENIED` reading the app's own private storage over `file://`, even
   though the app process reads and writes there freely.

Neither prototype produced a rendered model. The failures looked like consequences of an opaque
or missing page **origin**: `blob:` URL creation for textures and Web Worker construction for
the Draco mesh decoder are both origin-sensitive, and both broke. (One theory was eliminated for
free along the way: the `.glb` files do carry real embedded WebP textures in a `bufferView`, not
external `uri` references, so off-colour rendering is not a missing-external-texture problem.)

A loopback HTTP server is a bet on one change fixing all of that at once: the page gets a real,
ordinary, same-origin HTTP origin. It also lets the page be a plain `WebView` with a `Source`
URL, which deletes `HybridWebView` — and its crash path, and its literal-filename constraint —
from the design entirely. The generated viewer page is parameterised by URL path
(`/viewer/25.html`), synthesised in memory, so the 1000-files-on-disk workaround is gone too.

**That is a hypothesis with good reasoning behind it. It is not a result.**

---

## The central unknown: does `HttpListener` work on .NET for Android?

### Conclusion

**Probably, but I would not bet the feature on it — and there is a second, more likely blocker
sitting in front of it that has nothing to do with `HttpListener` at all.**

Confidence that `new HttpListener()` + `Start()` on `http://127.0.0.1:<port>/` succeeds on
`net10.0-android`: **roughly 60%.** That is "more likely than not", not "expected to work".

Confidence that the Android WebView will then actually load that `http://127.0.0.1:<port>/` URL
**without a manifest change**: **low, maybe 25%.** See "The cleartext problem" below. This is the
finding I would act on first.

### What that conclusion rests on

Things I could actually check in this repo:

- No existing `HttpListener` usage anywhere in the app or in vendored `PKHeX.Core` — no prior
  art in-tree to learn from.
- `PkhexMobile/Platforms/Android/AndroidManifest.xml` declares `android.permission.INTERNET`
  and `ACCESS_NETWORK_STATE`. INTERNET is required to bind a listening socket on Android, so
  that box is already ticked. There is **no** `android:usesCleartextTraffic` and **no**
  `networkSecurityConfig` reference.

Reasoning, which is where the rest of the confidence (and the doubt) comes from:

- .NET carries two `HttpListener` implementations: a Windows one over `http.sys`, and a
  **managed, socket-based one** used on every non-Windows target. The managed implementation
  needs nothing more exotic than `System.Net.Sockets`, which unquestionably works on Android.
  That is the main reason to expect success.
- Mono/Xamarin.Android historically shipped that managed implementation and embedded HTTP
  servers on Android did work in that era.
- Against it: `System.Net.HttpListener` is one of the assemblies that mobile and browser
  workloads have variously stubbed out or marked unsupported across .NET versions, and I cannot
  check .NET 10's android reference assembly from here. If the BCL has annotated it
  `[UnsupportedOSPlatform("android")]`, or stubbed it to throw, it throws
  `PlatformNotSupportedException` at construction.
- Also against it: **trimming**. MAUI Release builds trim aggressively. Even if the type exists,
  reflection-free code paths inside the managed listener can be trimmed in ways that surface
  only in a Release build, not in the Debug build you will test with first.

### A free empirical test — do this before anything else

**Just build it.** `dotnet build PkhexMobile/PkhexMobile.csproj -f net10.0-android -c Debug`.

If the build emits **CA1416** (platform compatibility) on the `HttpListener` lines in
`LoopbackModelServer.StartCore`, that *is* the answer: the BCL itself declares the API
unsupported on android, and no amount of runtime hedging will change it. Stop and go to the
fallback. This project's bar is zero warnings, so this failure mode announces itself loudly and
costs nothing to find out.

If it builds clean, that is weak positive evidence — it means the API is *not declared*
unsupported — but it says nothing about what happens at runtime. `PlatformNotSupportedException`
can still be thrown by a stub implementation that compiles fine.

**Nothing was suppressed to make this build.** If CA1416 fires, it fires. Do not `#pragma`
it away; that would be throwing away the single cheapest piece of evidence available.

### The cleartext problem (read this even if `HttpListener` works)

Android apps targeting API 28+ have cleartext HTTP **denied by default**, and the WebView
respects that policy. `http://127.0.0.1:<port>/` is cleartext. This app's manifest has no
cleartext opt-in and no network security config.

I could not verify whether Android's default network security configuration exempts loopback.
My reading is that it does **not**, and that the WebView will refuse the navigation — but I am
genuinely unsure, and this is exactly the kind of "obvious" claim that turns out to have a
carve-out.

Resolving it is cheap and the fix is cheap. Either:

- add `android:usesCleartextTraffic="true"` to the `<application>` element (blunt: re-permits
  cleartext app-wide, which is a real security regression for a save-file editor that also does
  update checks over HTTPS), **or**
- add a `res/xml/network_security_config.xml` with a `<domain-config
  cleartextTrafficPermitted="true">` limited to `127.0.0.1` and `localhost`, and reference it
  from `<application android:networkSecurityConfig="...">` (correct, narrow, and the one to
  actually do).

Either is a manifest edit and therefore **the orchestrator's**, not this port's.

### If it does not work — the recommended next attempt

**`WebViewAssetLoader` (androidx.webkit), and honestly it may deserve to be tried first.**

Attach a custom `WebViewClient` to the Android platform view and override
`shouldInterceptRequest`, backing it with a `WebViewAssetLoader` whose path handlers point at the
model cache directory. It serves under `https://appassets.androidplatform.net/`, which:

- is a **real HTTPS origin** — which is the whole thing the loopback server was invented to
  provide, so it addresses the `blob:`/Worker/Draco failures identically;
- **sidesteps the cleartext problem entirely** — no manifest change, no network security config;
- needs **no listening socket at all**, so the `HttpListener`-on-Android question evaporates;
- has no port to probe, no accept loop, no lifetime to manage against page navigation.

Its costs: it is Android-specific platform code (`Platforms/Android/`), it needs a handler
customisation to reach the native `WebView`, and it drags in the `Xamarin.AndroidX.WebKit`
package — a csproj edit. That is a fair trade for removing two unknowns.

The second fallback, a hand-rolled `TcpListener` HTTP/1.1 server, is **not** worth it: it keeps
the cleartext problem, adds hand-written HTTP parsing, and only removes the least likely of the
two failure modes.

If the loopback path *is* kept, `LoopbackModelServer` is deliberately small and its public
surface (`StartAsync`, `IsAvailable`, `UnavailableReason`, `ViewerUrl`) is transport-agnostic —
an asset-loader implementation can be swapped in behind the same four members without the page
changing at all.

---

## What could not be verified — the blunt list

Nothing in this section is a maybe. These are all "not done".

**Could not be done at all in this environment (no .NET SDK, no emulator, no device, no GUI):**

1. **It has never been compiled.** Not once. Zero warnings is a goal here, not an observation.
   Expect to fix something on first build.
2. Whether the XAML compiles. `Model3D/` is a new subfolder; MAUI's default `MauiXaml` glob
   should pick up `**/*.xaml` since the csproj declares no explicit `MauiXaml` items, but that
   is an assumption, not a check.
3. Whether `HttpListener` constructs and starts on Android. **The central unknown.** See above.
4. Whether the WebView will load a cleartext loopback URL. **Probably not without a manifest
   change.** See above.
5. Whether the models render, and whether they render in the *right colours* — which was the
   original complaint that started this whole investigation. Even a perfect origin fix does not
   automatically mean correct rendering.
6. Whether the Draco decoder Worker initialises, and whether texture `blob:` URLs are created,
   under the loopback origin. This is the specific thing the approach is betting on and the
   specific thing that cannot be checked here.
7. Whether an ~8.2MB model (species 979) streams over loopback without the WebView timing out or
   the app being killed for memory.
8. Whether rotate/pinch-zoom gestures work inside the WebView on a real touchscreen.
9. Whether the server shuts down cleanly on navigation in practice — `DisposeAsync` waits a
   bounded 2s for the accept loop, but "no leaked socket after 50 open/close cycles" is an
   on-device measurement.
10. Whether any of this survives a **Release** (trimmed) build. Everything above assumes Debug.

**Not done for other reasons:**

11. **No verification harness exists.** This project's standard is a `verify/<Name>/` console
    harness (CLAUDE.md §4). One could genuinely cover `LoopbackModelServer`'s path-traversal
    guard, content-type mapping, and start/dispose lifecycle on a desktop runtime — but a
    desktop `HttpListener` passing proves *nothing* about Android, which is the only question
    that matters, and a green harness here would be actively misleading. Worth writing anyway
    for the path-traversal guard specifically; that one is generation-independent and
    platform-independent.
12. **The path-traversal guard is unexercised.** It normalises with `Path.GetFullPath` and
    checks the result is still under the cache root (ordinal comparison, because Android's
    filesystem is case-sensitive). The logic is straightforward and the approach is the right
    one — normalise-then-check beats blacklisting `..` — but no test has ever run against it.
13. **No model has ever been downloaded**, because no source URL is configured. The download
    path (`Model3DCache.EnsureFileAsync`) has never executed.

---

## Wiring the orchestrator must do

None of this was done here, by instruction:

- `Routing.RegisterRoute(nameof(Model3DViewerPage), typeof(Model3DViewerPage));` in
  `AppShell.xaml.cs`, with `using PkhexMobile.Model3D;`.
- Navigate with `await Shell.Current.GoToAsync($"{nameof(Model3DViewerPage)}?speciesId={id}");`
  — a plain number, safe with the Shell dictionary-coercion trap (CLAUDE.md §2), which only
  bites non-`IConvertible` payloads.
- Entry point: a button on `PokedexDetailPage`, next to the existing "View Shiny" toggle. It
  should be `IsVisible="{...}"`-gated on `Model3DFeature.IsEnabled` so that with the feature off
  the button does not exist rather than existing and explaining itself.
- The manifest cleartext decision, if the loopback path is pursued (see above).

---

## For the next person: do these in order

1. Build. Watch for **CA1416** on the `HttpListener` lines. If it fires, go to step 5.
2. Decide the cleartext question — read Android's network security config docs, or just add the
   narrow `127.0.0.1`/`localhost` `domain-config` and move on.
3. Supply a real `ModelSourceUrlTemplate` and `ViewerRuntimeUrl` (licence-checked, SHA-pinned),
   or vendor `model-viewer.min.js` as a `MauiAsset` and copy it into the cache on first run.
4. Flip `Model3DFeature.IsEnabled` **locally only**, deploy with
   `dotnet build ... -f net10.0-android -c Debug -t:Run`, open species 979 first, and read
   `adb logcat -d | grep chromium` — real JS console output comes through there directly, no
   bridge needed.
5. If the loopback path fails at any step: switch to `WebViewAssetLoader`. Do not spend a second
   session grinding on `HttpListener`.
6. Only after a model has rendered, in the right colours, on real hardware — commit a flip of
   `Model3DFeature.IsEnabled` to `true`, and say **on-device verified** in the commit message,
   not "harness passed". This project has a documented case (the Shell `InvalidCastException`)
   of a bug that passed every harness and appeared only on a device.
