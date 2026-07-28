# 3D model discolouration — root-cause analysis

**Status: source + asset analysis only. NOTHING here has been visually verified.**
This session had no dotnet SDK, no emulator, no device, no browser, and no network fetch
tooling. Every claim below is derived from (a) bytes inside the `.glb` files and (b) the text
of the vendored `model-viewer.min.js`. Confidence labels are attached to every candidate;
read them, they are not decoration.

Investigated on branch `claude/model-opus-jxytim`, 2026-07-28.

---

## 1. What was examined, and how

The models live only on `origin/3d-models-experimental` (933 `.glb` files under
`PkhexMobile/Resources/Raw/model3d/models/`). They were read **without merging or
cherry-picking** — the +214MB of blobs stay out of this branch's history:

```
git fetch origin 3d-models-experimental
git show origin/3d-models-experimental:PkhexMobile/Resources/Raw/model3d/models/6.glb > <scratchpad>/6.glb
git show origin/3d-models-experimental:PkhexMobile/Resources/Raw/model3d/model-viewer.min.js > <scratchpad>/
```

20 models were extracted to a scratchpad outside the repo and parsed with `python3`
(12-byte GLB header → JSON chunk → `json.loads`): species **1, 3, 6, 9, 25, 52, 94, 100, 130,
150, 249, 384, 448, 493, 658, 700, 800, 887, 906**. Embedded WebP payloads were decoded with
Pillow to sample their actual pixel colours. Nothing was written into the repo.

Renderer under analysis: **Google `<model-viewer>`**, vendored at
`PkhexMobile/Resources/Raw/model3d/model-viewer.min.js` (935,194 bytes, committed in `068c14b`).
Not raw three.js — this matters, because model-viewer bundles its own three.js *and* imposes
its own renderer settings on top. Version fingerprint: bundles three.js ≈ **r162–r168**
(contains a deprecation string *"…will be removed in r169…"*, still has `useLegacyLights`, has
`NeutralToneMapping` (r162+), lacks `KHR_materials_dispersion`) — i.e. **model-viewer v4-era**.

The wrapper page is one literal HTML file per species, all identical apart from the `src`
(`model_1.html` … `model_1025.html`):

```html
<model-viewer id="mv" src="models/1.glb" camera-controls auto-rotate shadow-intensity="1"></model-viewer>
```

No `tone-mapping`, no `exposure`, no `environment-image` attribute is set. Remember that line —
it is the crux of the top candidate.

---

## 2. Asset evidence (PROVEN — read directly from the GLB bytes)

### 2.1 Declared extensions

Identical in **all 20 sampled models**, e.g. `models/6.glb` (Charizard):

```json
"extensionsUsed":     ["EXT_texture_webp", "KHR_draco_mesh_compression"],
"extensionsRequired": ["EXT_texture_webp", "KHR_draco_mesh_compression"]
```

Aggregate over the 20 sampled files: `EXT_texture_webp` **20/20 required**,
`KHR_draco_mesh_compression` **20/20 required**. Also seen in `extensionsUsed` but never
required: `KHR_materials_unlit` (3 files), `KHR_texture_transform` (3 files),
`KHR_materials_specular` (2 files).

### 2.2 Textures carry *no* plain `source` — only the extension's

`models/6.glb`, verbatim, all five entries:

```json
"textures": [
 { "sampler": 0, "extensions": { "EXT_texture_webp": { "source": 0 } } },
 { "sampler": 0, "extensions": { "EXT_texture_webp": { "source": 1 } } },
 { "sampler": 0, "extensions": { "EXT_texture_webp": { "source": 2 } } },
 { "sampler": 0, "extensions": { "EXT_texture_webp": { "source": 3 } } },
 { "sampler": 0, "extensions": { "EXT_texture_webp": { "source": 4 } } }
]
```

Across the 20 sampled models: **66 textures, 0 with a plain `source`, 66 with
`extensions.EXT_texture_webp.source`.** There is no PNG/JPEG fallback anywhere. A consumer
without the extension sees `textureDef.source === undefined` and has nothing to fall back to —
which is precisely why the extension is listed as *required*.

### 2.3 Image mimeType

`models/6.glb`, verbatim:

```json
"images": [
 { "name": "Image_3", "mimeType": "image/webp", "bufferView": 0 },
 { "name": "Image_4", "mimeType": "image/webp", "bufferView": 1 },
 { "name": "Image_0", "mimeType": "image/webp", "bufferView": 2 },
 { "name": "Image_1", "mimeType": "image/webp", "bufferView": 3 },
 { "name": "Image_2", "mimeType": "image/webp", "bufferView": 4 }
]
```

**66/66 sampled images are `image/webp`, all `bufferView`-embedded, none external.** Confirms
the already-established finding: textures are present, not missing.

Decoding the raw bytes of each `bufferView`: every one is a well-formed RIFF/WEBP container
with a **`VP8 ` (lossy, no alpha)** chunk, power-of-two dimensions (64×128 up to 1024×512), and
`RIFF size + 8 == bufferView byteLength` exactly (no padding, no truncation). They decode
cleanly in Pillow. **The image payloads are valid and are not the problem.**

Incidentally, three.js's own WebP feature-detection probe
(`data:image/webp;base64,UklGRiIAAABXRUJQVlA4IBYAAAAwAQCdASoBAAEADsD+JaQAA3AAAAAA`) is also a
1×1 **`VP8 ` lossy** image — the same codec variant as the real textures. So the probe cannot
pass while the real textures fail to decode, or vice versa.

### 2.4 Materials

`models/6.glb`, verbatim (first two of five; the other three are structurally identical):

```json
"materials": [
 { "name": "Material_15", "doubleSided": true,
   "pbrMetallicRoughness": { "roughnessFactor": 0.5, "metallicFactor": 0,
                             "baseColorTexture": { "index": 0 } } },
 { "name": "Material_16", "doubleSided": true,
   "pbrMetallicRoughness": { "roughnessFactor": 0.5, "metallicFactor": 0,
                             "baseColorTexture": { "index": 1 } } }
]
```

Aggregate over 20 files / **70 materials**:

| Property | Count |
|---|---|
| `pbrMetallicRoughness.baseColorTexture` present | 70/70 |
| `baseColorFactor` present | **2/70** (so 68 default to `[1,1,1,1]` = white) |
| `KHR_materials_unlit` | 14/70 materials, concentrated in **3/20 files** (150, 249, 658 — all-or-nothing per file) |
| `normalTexture` / `occlusionTexture` / `metallicRoughnessTexture` / `emissiveTexture` | **none, anywhere** |
| `alphaMode` | never set (all opaque) |
| `metallicFactor` | 0 |
| `roughnessFactor` | 0.5, or absent → three.js default 1.0 |

The only material extension in play is `KHR_materials_specular` (`"specularFactor": 0` on a
single face material in `25.glb`), which is *used* but not *required*.

**Base colour is the only shading input these assets have.** There is nothing else — no normal
map, no AO, no roughness map — so if the base colour texture is not applied, or is
post-processed, there is nothing left to make the model look like anything.

### 2.5 Vertex colours

Primitive attribute sets (counting Draco-compressed attribute lists too), across 87 sampled
primitives:

```
POSITION 87, TEXCOORD_0 87, JOINTS_0 86, WEIGHTS_0 86, NORMAL 66, COLOR_0 3
```

**`COLOR_0` exists in only 3 primitives, all in one file (`52.glb`, Meowth).** So vertex-colour
multiplication is *not* a general explanation — but it is a live per-species variable worth
remembering if one specific Pokémon looks wrong while its neighbours look right.

### 2.6 What the textures actually look like (decoded pixels)

This turned out to be the single most informative measurement. Sampling each decoded WebP down
to 48×48 and taking mean / modal colour:

| Model | Texture | Size | Mean | Modal colour | Mean HSV sat |
|---|---|---|---|---|---|
| `6.glb` Charizard | `Image_1` (body) | 512×256 | `#DFAB70` | **`#EFAB62`** (742/2304 px) | 0.49 |
| `6.glb` Charizard | `Image_2` | 512×256 | `#A28965` | `#EFAB62` (535), `#000000` (132), `#399DB0` (122) | 0.53 |
| `1.glb` Bulbasaur | `Image_0` (body) | 512×512 | `#6E9E70` | **`#8CC38C`** (683/2304 px) | 0.30 |
| `25.glb` Pikachu | `Image_2` | 128×256 | `#F3CE07` | **`#FFDF00`** (451/2304 px) | 0.96 |

`#EFAB62` **is literally a flat tan.** That is Charizard's body albedo *as authored*. Bulbasaur's
`#8CC38C` is a comparably pale, desaturated green. Pikachu's `#FFDF00` is fully saturated.

This matters enormously, because the on-device symptom recorded in `PROGRESS.md` (commit
`ff1d53b`) was *"flat tan, no real coloring"* — observed **on Charizard**, whose body texture is
`#EFAB62`. See §4.

---

## 3. Renderer evidence (PROVEN — read from the vendored `model-viewer.min.js`)

### 3.1 The leading hypothesis is REFUTED for this bundle

> *Hypothesis under test: three.js's GLTFLoader does not decode `EXT_texture_webp` unless a
> plugin is registered, so the texture is silently dropped.*

**False for this bundle.** The WebP extension class is present *and* registered by default in
the GLTFLoader constructor. Minified, from the bundle (class `Su`, constants object `cu`):

```js
EXT_TEXTURE_WEBP:"EXT_texture_webp", EXT_TEXTURE_AVIF:"EXT_texture_avif", …

class Su{ constructor(t){ this.parser=t, this.name=cu.EXT_TEXTURE_WEBP, this.isSupported=null }
  loadTexture(t){ … return this.detectSupport().then(function(r){
      if(r) return i.loadTextureImage(t,s.source,o);
      if(n.extensionsRequired && n.extensionsRequired.indexOf(e)>=0)
        throw new Error("THREE.GLTFLoader: WebP required by asset but unsupported.");
      return i.loadTexture(t) }) }
  detectSupport(){ … const e=new Image;
      e.src="data:image/webp;base64,UklGRiIAAABXRUJQVlA4IBYAAAAwAQCdASoBAAEADsD+JaQAA3AAAAAA";
      e.onload=e.onerror=function(){ t(1===e.height) } … } }
```

and in the loader constructor, registered third:

```js
this.pluginCallbacks=[],
  this.register(t=>new Au(t)),   // clearcoat
  this.register(t=>new xu(t)),   // KHR_texture_basisu   (returns null for our textures)
  this.register(t=>new Su(t)),   // EXT_texture_webp  ← registered
  this.register(t=>new Cu(t)),   // EXT_texture_avif
  …
```

`_invokeOne` returns the first truthy plugin result; the only earlier plugin with a
`loadTexture` is the KTX2/basisu one, which returns `null` for a texture with no
`KHR_texture_basisu` extension. So **`Su` handles our textures.** The "no WebP plugin
registered" story does not apply to `model-viewer.min.js` as vendored.

> It *would* apply if the port to `master` ever swaps model-viewer for a hand-assembled
> three.js bundle, or tree-shakes GLTFLoader. Keep this in mind: with
> `EXT_texture_webp` in `extensionsRequired` and **no plain `source` fallback**, any loader
> lacking the plugin gets nothing at all.

### 3.2 sRGB / linear plumbing is CORRECT — also refuted

- `assignTexture(a,"map", i.baseColorTexture, Lt)` — `Lt` is `SRGBColorSpace` (identified via
  `case Lt: case Pt: return [n,"sRGBTransferOETF"]` in the shader-generation switch, and via
  `KHR_materials_unlit`'s `assignTexture(t,"map",r.baseColorTexture,Lt)`). Base colour is
  correctly tagged sRGB.
- Renderer default `this._outputColorSpace = Lt` — sRGB output. Correct.
- `ImageBitmapLoader` explicitly forces `createImageBitmap(t, Object.assign(r.options,
  {colorSpaceConversion:"none"}))` — no double conversion. Correct.
- No normal / metalRough maps exist in these assets, so the "linear map wrongly tagged sRGB"
  failure mode has nothing to act on.

**The classic "wrong output colour space" explanation is ruled out.**

### 3.3 model-viewer's default tone mapping is ACES Filmic — PROVEN

Three independent places in the bundle, all setting `P` (= `ACESFilmicToneMapping`, identified
via `case P: i="ACESFilmic"` in the shader switch):

```js
this.threeRenderer.toneMapping = P                              // Renderer construction
… this.exposure=1, this.toneMapping=P, …                        // ModelScene constructor
this.toneMapping="auto"                                          // element default attribute
t.has("toneMapping") && (this[xy].toneMapping =
    "commerce"===this.toneMapping || "neutral"===this.toneMapping ? Q :
    "agx"===this.toneMapping ? F : P, …)                          // "auto" → P (ACES)
```

The wrapper HTML sets no `tone-mapping` attribute → `"auto"` → **ACES Filmic**.

And there is an exposure asymmetry that compounds it:

```js
preRender(t,e,i){ const {element:n, exposure:r, toneMapping:s}=t; …
  const o=n.environmentImage, l=n.skyboxImage,
        c = s===Q && ("neutral"===o||"legacy"===o||!o&&!l);
  this.threeRenderer.toneMappingExposure = (a?r:1) * (c?1.3:1) }
```

`Q` is `NeutralToneMapping`. The page sets neither `environment-image` nor `skybox-image`, so
`!o && !l` is true — meaning **the built-in ×1.3 exposure compensation is applied only on the
Neutral path.** On the current ACES default the scene renders both desaturated *and* ~23%
darker than model-viewer's own compensated baseline.

Lighting is the built-in generated environment (`new Cf("legacy")`), a small IBL room of pure
white lights (`new PointLight(16777215, …)`) and neutral grey boxes — i.e. neutral white, not
warm.

### 3.4 A texture load failure would be COMPLETELY SILENT — PROVEN mechanism

```js
loadTextureImage(t,e,i){ …
  const l = this.loadImageSource(e,i).then(function(e){ … }).catch(function(){ return null });
  return this.textureCache[o]=l }

assignTexture(t,e,i,n){ return this.getDependency("texture",i.index).then(function(s){
    if(!s) return null;                        // ← texture dropped, material.map never set
    … void 0!==n && (s.colorSpace=n), t[e]=s, s }) }
```

So: **any** texture failure resolves to `null`, `material.map` is simply never assigned, the
material keeps `a.color = new Color(1,1,1)` (white, because 68/70 materials have no
`baseColorFactor`), `metalness = 0`, `roughness = 0.5`-or-`1.0`, and the model renders as a
featureless white PBR blob. The only trace is one
`console.error("THREE.GLTFLoader: Couldn't load texture", url)` — and `PROGRESS.md` records
that this app has **no JS-console capture wired into `HybridWebView`**, and that `adb logcat`
did not surface it.

This is the mechanism by which "renders fine, looks wrong, no error anywhere" is possible. It
is proven to exist. Whether it *fired* is a separate question — see §4.

### 3.5 The texture path depends on `blob:` URLs — and so does Draco

Textures (bufferView-backed) go through:

```js
o = i.getDependency("bufferView", s.bufferView).then(t => {
      const e = new Blob([t], {type: s.mimeType});
      return o = a.createObjectURL(e) })
…
a.credentials = "anonymous"===this.crossOrigin ? "same-origin" : "include";
const o = fetch(t,a).then(t=>t.blob())
                    .then(t=>createImageBitmap(t, Object.assign(r.options,{colorSpaceConversion:"none"})))
```

(`ImageBitmapLoader` is selected on Chromium/Android; `TextureLoader` only on Safari/old Firefox.)

**Draco uses the same primitive:**

```js
this.workerSourceURL = URL.createObjectURL(new Blob([r]))
…
const t = new Worker(this.workerSourceURL)
```

That coupling is the key inferential lever. `PROGRESS.md` (`ff1d53b`) records that on-device,
**the geometry decoded and rendered correctly** — *"the shape rendered perfectly - wings, tail,
head all correctly proportioned"*. Draco geometry cannot decode unless `URL.createObjectURL`
worked, `new Worker(blob:…)` was allowed (both fail from an opaque/`null` origin), and the
decoder was fetched successfully. So in *that* prototype, `blob:` URLs demonstrably worked.

> Important scoping note: the `blob:null` / Worker failures described in `WAKEUP.md` are from
> the **two later WebView prototypes that failed to render at all** — different code paths.
> They are not evidence about the model-viewer/`HybridWebView` run that actually produced the
> off-colour render. Do not merge those two observations.

### 3.6 Bonus finding: the "fully offline, no CDN" claim is false

```js
const e = self.ModelViewerElement || {},
      h = e.dracoDecoderLocation || "https://www.gstatic.com/draco/versioned/decoders/1.5.6/";
hA.setDRACODecoderLocation(h);
const u = e.ktx2TranscoderLocation || "https://www.gstatic.com/basis-universal/versioned/2021-04-15-ba1c3e4/";
```

`git ls-tree` confirms `Resources/Raw/model3d/` contains **only** `README.md`,
`model-viewer.min.js`, the 933 `.glb`s and the per-species HTML — **no `draco_wasm_wrapper.js`,
no `draco_decoder.wasm`**. Every one of the 933 models declares
`KHR_draco_mesh_compression` as *required*. Therefore **every model needs a live fetch to
`gstatic.com` to render at all**, contradicting `Resources/Raw/model3d/README.md`'s claim that
*"Nothing here is loaded from a CDN or the network"*. This is not the discolouration cause (it
is all-or-nothing), but it is a real defect on the same page and it must be fixed before any
offline-capable release.

---

## 4. Ranked candidate root causes

### #1 — model-viewer's default ACES Filmic tone mapping washing out already-pale game-rip albedo
**STRONG HYPOTHESIS** (every component proven; the *combination* is what is unverified)

Proven components: ACES is the active default (§3.3); the ×1.3 exposure compensation is
withheld on that path (§3.3); the assets have *only* base colour and no other shading inputs
(§2.4); Charizard's authored body albedo is `#EFAB62`, a flat tan, and Bulbasaur's is a pale
`#8CC38C` (§2.6).

The argument for ranking it first:

1. **The reported symptom matches the texture's own colour.** The on-device observation was
   "flat tan, no real coloring" **on Charizard** — and Charizard's dominant body albedo *is*
   `#EFAB62`, flat tan. If textures had been *dropped*, Charizard would render **white/grey**
   (68/70 materials have no `baseColorFactor`, so they default to `[1,1,1,1]` under a neutral
   white IBL), not tan.
2. **The blob-URL coupling (§3.5) argues the textures loaded.** Geometry decoded on-device,
   which proves `createObjectURL` + `Worker(blob:)` + network all worked in that page. The
   texture path needs strictly less than that.
3. ACES is specifically notorious for desaturating and lifting mid-to-high values. Applied to
   an albedo that is *already* pale and unlit-by-design, and rendered as PBR diffuse under an
   IBL, the result is exactly "flat, washed, wrong-hue" rather than the saturated flat colours
   the 2D sprite sets the expectation for.
4. These are game-rip albedo maps authored for the games' own toon/cel shader. Feeding them to
   a physically-based renderer as diffuse albedo is a category error independent of any bug.

### #2 — Textures silently dropped; models rendering as untextured white PBR
**STRONG HYPOTHESIS for the mechanism, SPECULATIVE for the trigger**

The mechanism is *proven* (§3.4): a texture failure yields `null`, is never assigned, and
produces no visible error. What is entirely unproven is any reason for it to fail — the WebP
plugin is registered (§3.1), the WebP bytes are valid lossy VP8 (§2.3), `createImageBitmap`
supports WebP on Android WebView, and `blob:` demonstrably worked in that page (§3.5).

Kept at #2 rather than dismissed because the observer's phrase "no real coloring" does read as
*uniform*, and because #1 and #2 are not mutually exclusive — some species could be dropping
textures while all species are being ACES-washed.

### #3 — Assets authored for an unlit/toon pipeline being PBR-shaded at all
**PROVEN FROM ASSET, partial explanation**

`3/20` sampled files (150 Mewtwo, 249 Lugia, 658 Greninja) mark **every** material
`KHR_materials_unlit`; the other 17 do not. The upstream set is internally inconsistent. The 17
lit ones get IBL diffuse + specular sheen from a `roughness 0.5, metalness 0` dielectric with no
roughness/normal/AO map to break it up — inherently flatter and shinier than the games' look.
This is a genuine fidelity gap, not a bug, and it stacks on top of #1.

### #4 — `KHR_materials_specular { "specularFactor": 0 }`
**SPECULATIVE, narrow**

Present on isolated face materials in 2/20 files. The bundle *does* support the extension. Could
make one material on one species read differently. Not a global explanation.

### #5 — `COLOR_0` vertex colours multiplied into base colour
**SPECULATIVE, single-species**

Only 3 primitives in 1 of 20 files (`52.glb`). three.js sets `vertexColors` and multiplies. If
one particular Pokémon looks wrong while others look fine, check this — otherwise irrelevant.

### REFUTED and closed
- ~~Textures missing / external `uri`~~ — closed previously; re-confirmed here, 66/66 embedded.
- ~~`EXT_texture_webp` unsupported by the loader~~ — §3.1, the plugin is registered.
- ~~Wrong `outputColorSpace` / texture encoding~~ — §3.2, all correct.
- ~~Corrupt or undecodable WebP payloads~~ — §2.3, all decode cleanly in Pillow.

---

## 5. The concrete fix for candidate #1

**Renderer-side, asset-side untouched, one line per wrapper page.** Change the `<model-viewer>`
tag in the `model_{id}.html` template (`Resources/Raw/model3d/README.md` holds the canonical
template, and the 933 generated copies) from:

```html
<model-viewer id="mv" src="models/1.glb" camera-controls auto-rotate shadow-intensity="1"></model-viewer>
```

to:

```html
<model-viewer id="mv" src="models/1.glb"
              camera-controls auto-rotate
              shadow-intensity="1"
              tone-mapping="neutral"
              environment-image="neutral"
              exposure="1"></model-viewer>
```

Why exactly these:

- `tone-mapping="neutral"` → `KhronosNeutralToneMapping` (`Q` in the bundle). It preserves hue
  and saturation in the mid range instead of ACES's filmic desaturation. Verified present and
  wired in this bundle version (`"commerce"===this.toneMapping||"neutral"===this.toneMapping ? Q : …`).
- It **also silently restores the ×1.3 exposure compensation** (§3.3) — `s===Q &&
  ("neutral"===o||"legacy"===o||!o&&!l)`. Two problems, one attribute.
- `environment-image="neutral"` keeps that compensation branch satisfied explicitly rather than
  by accident, and pins the IBL so a future edit adding a skybox doesn't silently halve the
  brightness.
- `exposure="1"` is the default; state it so the value is visible when tuning.

If that is still too washed for a cel-shaded look, the second-line fix is **asset-side**: mark
every material `KHR_materials_unlit`, which this bundle fully supports (`case
cu.KHR_MATERIALS_UNLIT: s[e]=new uu` — it maps to `MeshBasicMaterial` and applies the base colour
texture directly with no lighting). This is a **JSON-chunk-only rewrite**: add
`"KHR_materials_unlit"` to `extensionsUsed` and `"extensions":{"KHR_materials_unlit":{}}` to each
material — roughly **+40 bytes per material, ~+200 bytes per file, ~+0.2 MB over all 933
models**, with *no* re-encoding of the Draco geometry or the WebP textures. Note 3/20 upstream
files already do exactly this, so it matches upstream intent. Also set
`material.toneMapped = false` via the scene-graph API if the tone curve still shifts the
unlit colours. Do **not** do this before the diagnostic in §6 — it is a 933-file transform in
service of an unproven diagnosis.

**Explicitly NOT recommended: transcoding the WebP textures to PNG.** It fixes nothing that is
actually broken (§3.1, §2.3) and would inflate the asset set several-fold — these are lossy VP8
at 500 B – 25 KB per texture; PNG equivalents of the same 512×512 art run 5–20× larger, turning
a 214 MB fetch-on-demand corpus into something far worse.

---

## 6. The one-deploy experiment that discriminates #1 from #2

Do this **before** shipping any fix. The wrapper pages are real HTML files, so they can run
their own inline JavaScript — the `HybridWebView` bridge problem documented in
`Model3DViewerPage.xaml.cs` was only ever about *parameterising* the page from C#, never about
in-page scripts. So no bridge, no `EvaluateJavaScriptAsync`, no logcat is needed:

```html
<div id="dbg" style="position:absolute;top:0;left:0;font:12px monospace;
     background:#fff;color:#000;z-index:9;white-space:pre;padding:4px"></div>
<script>
  const log = m => document.getElementById('dbg').textContent += m + "\n";
  window.onerror = (m,s,l) => log("ERR " + m + " @" + l);
  const ce = console.error; console.error = (...a) => { log("CE " + a.join(" ")); ce(...a); };
  document.getElementById('mv').addEventListener('load', () => {
    const mats = document.getElementById('mv').model.materials;
    log("materials=" + mats.length);
    mats.forEach((m,i) => log(i + " tex=" +
      (m.pbrMetallicRoughness.baseColorTexture ? "YES" : "NULL") +
      " base=" + JSON.stringify(m.pbrMetallicRoughness.baseColorFactor)));
  });
</script>
```

`model.materials[i].pbrMetallicRoughness.baseColorTexture` is confirmed present in this bundle
(`get baseColorTexture(){return this[vv]}`). Read the overlay from a screenshot or
`uiautomator dump` — no console access required, which is exactly the blocker that stopped the
previous pass.

- All `tex=YES` → textures loaded → **candidate #1**, apply §5.
- Any `tex=NULL` (plus a `CE THREE.GLTFLoader: Couldn't load texture …` line) → **candidate #2**,
  and the console line names the failing URL, which finally makes it debuggable.

**Second, near-free discriminator:** open **Mewtwo (150), Lugia (249) or Greninja (658)** — the
sampled models whose materials are already fully `KHR_materials_unlit` — next to
**Charizard (6)** or **Bulbasaur (1)**. Unlit materials bypass IBL entirely but use the *same*
texture-loading path. If the unlit ones show recognisable colour and the lit ones look tan, that
is #1 and #3, not #2.

**Third, unrelated but same page:** verify whether the device had network when the "geometry
rendered fine" observation was made. If Draco geometry ever decodes with the device offline,
§3.6 is wrong and needs re-examining.

---

## 7. Honest limits of this analysis

**None of this has been visually verified.** This session had no device, no emulator, no
browser, no dotnet SDK and no way to fetch anything — so no render was ever produced or seen.
Everything above is inference from asset bytes and from reading a minified JavaScript bundle.

What could **not** be determined here, and needs real hardware:

1. **Whether the textures actually load on-device.** §6 settles it in one deploy; nothing short
   of that does.
2. **What "off-colour" looks like now.** The whole ranking leans on one prose description
   ("flat tan, no real coloring", Charizard) written in `ff1d53b`. No screenshot of the 3D
   viewer exists on either branch — I checked; the only screenshots committed are
   `verify/OnDeviceBoxPartyMove/`, which is unrelated. A single screenshot would probably settle
   #1 vs #2 immediately.
3. **Whether `tone-mapping="neutral"` looks right.** It is a defensible, evidence-backed change,
   but "less washed out" is a judgement call that requires eyes on the render.
4. **Whether the discolouration is uniform across all 933 models.** 20 were sampled. The
   upstream set is provably inconsistent (§2.4 unlit, §2.5 COLOR_0), so per-species variation
   should be expected.

This project's own standard (`CLAUDE.md` §4.6) applies: *"library-harness only" is not
"on-device verified."* This is weaker than either — it is static analysis. Treat every candidate
here as a hypothesis to test on real hardware, not as a diagnosis to act on blind.
