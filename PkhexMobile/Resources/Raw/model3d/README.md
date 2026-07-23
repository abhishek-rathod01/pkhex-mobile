# Bundled 3D models

`Model3DViewerPage` (`PkhexMobile/Model3DViewerPage.xaml.cs`) shows a rotatable/zoomable `.glb`
model for a species, falling back to the existing 2D sprite when no model is bundled. Nothing
here is loaded from a CDN or the network - `model-viewer.min.js` is vendored in this folder so
the viewer works fully offline/in-APK.

## Adding a model for a species

Two files per species, both committed to this folder:

1. `models/{speciesId}.glb` - the model itself (e.g. `models/6.glb` for Charizard).
2. `model_{speciesId}.html` - a tiny wrapper page, copy-paste this template and fill in the id:

   ```html
   <!DOCTYPE html>
   <html>
   <head>
   <meta charset="utf-8">
   <meta name="viewport" content="width=device-width, initial-scale=1.0">
   <script type="module" src="model-viewer.min.js"></script>
   <style>
     html, body { margin:0; padding:0; height:100%; background:#F6F8FB; overflow:hidden; }
     model-viewer { width:100%; height:100%; }
   </style>
   </head>
   <body>
   <model-viewer id="mv" src="models/{speciesId}.glb" camera-controls auto-rotate shadow-intensity="1"></model-viewer>
   </body>
   </html>
   ```

`Model3DViewerPage.OnAppearing` checks whether `model3d/model_{speciesId}.html` exists
(`FileSystem.OpenAppPackageFileAsync`) and only then points `HybridWebView.DefaultFile` at it;
otherwise it shows the 2D sprite fallback. Use decimal PKHeX species IDs (`SpeciesId`), matching
the sprite convention (`spr_{dex:D4}.png` elsewhere in the app uses zero-padding - this does not;
`6.glb`/`model_6.html`, not `0006.glb`).

`speciesId` is the base species/form-0 only - this does not (yet) address alternate forms
(Mega/regional/Gmax) getting their own model.

## Why one HTML file per model, not one parameterized page

The obvious design - a single `index.html` that reads which model to load from the URL
(`?glb=...` or `#glb=...`) and sets `<model-viewer src>` via JS - does not work with
`HybridWebView`. On-device testing showed `HybridWebView.DefaultFile` is matched against the
bundled asset set as a **literal filename**, not parsed as a URL: both a query string
(`"index.html?glb=models/6.glb"`) and a URL fragment (`"index.html#glb=models/6.glb"`) produced
the identical `net::ERR_INVALID_RESPONSE` - the resolver looked for (and failed to find) a file
literally named that whole string, fragment included, even though URL fragments are never sent
as part of any real HTTP/asset request in any browser. A JS↔C# messaging bridge
(`RawMessageReceived`/`EvaluateJavaScriptAsync`) was also tried first and abandoned - it
consistently failed to round-trip on this MAUI/WebView version, independent of the above. Only
swapping `DefaultFile` to a second real, distinct, literal filename was confirmed on-device to
work (it re-navigates correctly, and the underlying HybridWebView → model-viewer → WebGL
pipeline itself renders fine). Hence: one real file per model, generated ahead of time rather
than parameterized at request time.

## Source

Models are intended to be sourced from `github.com/Pokemon-3D-api/assets` (not yet fetched into
this repo as of 2026-07-23 - no `.glb` files are currently committed here).
