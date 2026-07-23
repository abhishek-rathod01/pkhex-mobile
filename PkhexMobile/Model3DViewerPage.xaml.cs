namespace PkhexMobile;

/// <summary>
/// Rotatable/zoomable 3D model viewer for the Pokedex detail screen. MAUI has no native 3D
/// control, so this hosts the standard model-viewer web component (vendored locally under
/// Resources/Raw/model3d - never loaded from a CDN, so it works fully offline/in-APK) inside a
/// HybridWebView. camera-controls gives rotate/pinch-zoom for free; no custom gesture code needed.
///
/// This deliberately uses HybridWebView, not a plain WebView with a file:// URL - a real bug
/// found during on-device verification: Android's WebView renderer process runs sandboxed and
/// gets net::ERR_ACCESS_DENIED trying to read the app's own private storage (e.g.
/// FileSystem.CacheDirectory) via file://, even though the main app process can read/write there
/// freely. HybridWebView's virtual host (HybridRoot="model3d", serving the MauiAsset-bundled
/// Resources/Raw/model3d/** files over an internal virtual origin) sidesteps that restriction
/// entirely - no cache-directory copy step needed.
///
/// Per-species selection is a distinct literal file per model - "model_{id}.html", a tiny page
/// that hardcodes &lt;model-viewer src="models/{id}.glb"&gt; - not a single parameterized page.
/// That took six attempts to land on. In order: (1) EvaluateJavaScriptAsync from
/// WebViewInitialized threw "PlatformView cannot be null here" (fires before the native view
/// exists); (2) past that, the DOM wasn't ready ("Cannot read properties of null"); (3) a JS-side
/// window.HybridWebView.sendRawMessage("ready") handshake to fix the timing never arrived, and a
/// direct C#-to-JS bridge probe returned empty with no error - the bridge itself doesn't work on
/// this MAUI/WebView version, independent of DOM timing; (4) DefaultFile = "index.html?glb=..."
/// gave net::ERR_INVALID_RESPONSE; (5) switching "?glb=" for "#glb=" (a URL fragment, which per
/// spec is never sent as part of any resource request) gave the *identical* error - proving
/// DefaultFile is matched as a literal asset filename, not parsed as a URL, so neither query nor
/// fragment syntax can carry data through it; (6) confirmed on-device that a literal filename
/// swap (DefaultFile = "s6.html", a second real bundled file with a hardcoded src) renders and
/// re-navigates correctly - the render pipeline (HybridWebView virtual host -> model-viewer ->
/// WebGL) was never the problem, only the parameterization mechanism was. Convention and
/// generation instructions for model_{id}.html are in Resources/Raw/model3d/README.md.
/// </summary>
[QueryProperty(nameof(SpeciesIdParam), "speciesId")]
public partial class Model3DViewerPage : ContentPage
{
    ushort speciesId;

    public string SpeciesIdParam
    {
        set
        {
            if (ushort.TryParse(value, out var id))
                speciesId = id;
        }
    }

    public Model3DViewerPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (speciesId == 0)
            return;

        string pageFileName = $"model_{speciesId}.html";
        if (!await AssetExistsAsync($"model3d/{pageFileName}"))
        {
            ShowFallback("No 3D model is bundled for this species yet - showing the 2D sprite instead.");
            return;
        }

        ModelWebView.DefaultFile = pageFileName;
        ModelWebView.IsVisible = true;
        FallbackPanel.IsVisible = false;
    }

    void ShowFallback(string message)
    {
        ModelWebView.IsVisible = false;
        FallbackPanel.IsVisible = true;
        FallbackSprite.Source = SpriteHelper.SpeciesSpriteFile(speciesId, shiny: false);
        FallbackLabel.Text = message;
    }

    static async Task<bool> AssetExistsAsync(string logicalName)
    {
        try
        {
            using var src = await FileSystem.OpenAppPackageFileAsync(logicalName);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }
}
