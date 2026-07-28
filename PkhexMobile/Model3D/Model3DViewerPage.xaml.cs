namespace PkhexMobile.Model3D;

/// <summary>
/// Rotatable/zoomable 3D model viewer for a single species.
/// </summary>
/// <remarks>
/// <para>
/// MAUI has no native 3D control, so this hosts the <c>model-viewer</c> web component in a
/// WebView. What is new here versus the two prototypes on the abandoned
/// <c>3d-models-experimental</c> branch is <i>how the page is served</i>: a loopback HTTP server
/// (<see cref="LoopbackModelServer"/>) over a fetch-on-demand disk cache
/// (<see cref="Model3DCache"/>), rather than <c>HybridWebView</c>'s virtual host or a
/// <c>file://</c> URL. See <c>docs/3D-VIEWER-STATUS.md</c> for why, and for what remains
/// unproven.
/// </para>
/// <para>
/// <b>None of this has been seen working.</b> The whole namespace is gated behind
/// <see cref="Model3DFeature.IsEnabled"/>, which ships <c>false</c>. With the gate off this page
/// still renders correctly - it shows the "turned off" state - so a stray navigation to it can
/// never produce a blank screen.
/// </para>
/// <para>
/// Every path through <see cref="PrepareAsync"/> ends in exactly one of two visible outcomes: a
/// WebView showing a model, or the status card explaining in plain words why there isn't one.
/// There is no third state where the user is left looking at nothing, and no spinner that can
/// fail to resolve - the transient "preparing" text is itself a resolved state that is always
/// replaced.
/// </para>
/// <para>
/// The species id arrives as a plain query-string number. That is safe with the Shell
/// dictionary-coercion trap documented in CLAUDE.md, which only bites non-<c>IConvertible</c>
/// payloads such as <c>SaveFile</c>/<c>PKM</c>.
/// </para>
/// </remarks>
[QueryProperty(nameof(SpeciesIdParam), "speciesId")]
public partial class Model3DViewerPage : ContentPage
{
	ushort speciesId;
	LoopbackModelServer? server;
	CancellationTokenSource? work;

	public Model3DViewerPage()
	{
		InitializeComponent();
	}

	public string SpeciesIdParam
	{
		set
		{
			if (ushort.TryParse(value, out var id))
				speciesId = id;
		}
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await PrepareAsync().ConfigureAwait(true);
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		// Tear the server down with the page. Leaving a listening socket alive behind a screen
		// the user has left is both a needless open port and a needless wake-lock-ish cost.
		_ = TeardownAsync();
	}

	async void OnRetryClicked(object? sender, EventArgs e)
	{
		await TeardownAsync().ConfigureAwait(true);
		await PrepareAsync().ConfigureAwait(true);
	}

	async Task PrepareAsync()
	{
		// 1. The gate. Nothing below this line runs in a shipping build today.
		if (!Model3DFeature.IsEnabled)
		{
			ShowStatus(
				"3D view is turned off",
				"This build ships with the 3D model viewer disabled.",
				detail: "It has never been confirmed to render on a real device, so it is off rather than shipped broken.",
				warning: null,
				canRetry: false);
			return;
		}

		if (speciesId == 0)
		{
			ShowStatus(
				"No species selected",
				"This screen was opened without a species, so there is nothing to show.",
				detail: null,
				warning: null,
				canRetry: false);
			return;
		}

		Title = $"3D Model #{speciesId:D4}";
		ShowStatus("Preparing the 3D view", "One moment.", detail: null, warning: null, canRetry: false);

		var cts = new CancellationTokenSource();
		work = cts;
		var token = cts.Token;

		// 2. The loopback server. StartAsync does its socket work on the thread pool and never
		// throws - an Android runtime that refuses HttpListener comes back as an unavailable
		// server carrying a reason, not as an exception.
		var started = await LoopbackModelServer.StartAsync(Model3DCache.CacheRoot, token).ConfigureAwait(true);
		if (token.IsCancellationRequested)
		{
			await started.DisposeAsync().ConfigureAwait(true);
			return;
		}

		server = started;
		if (!started.IsAvailable)
		{
			ShowStatus(
				"3D view is unavailable",
				started.UnavailableReason ?? "The 3D viewer could not start on this device.",
				detail: "The 2D sprite is shown instead.",
				warning: "3D view is an unverified feature. This message is the expected outcome on a device whose system components do not support it.",
				canRetry: true);
			return;
		}

		// 3. The assets. Both must be on disk before the WebView is pointed anywhere - a
		// navigation that races the download is exactly how the earlier prototypes ended up
		// showing an empty page with no explanation.
		var runtimeReady = await Model3DCache.EnsureViewerRuntimeAsync(token).ConfigureAwait(true);
		if (token.IsCancellationRequested)
			return;

		var modelReady = runtimeReady && await Model3DCache.EnsureModelAsync(speciesId, token).ConfigureAwait(true);
		if (token.IsCancellationRequested)
			return;

		if (!runtimeReady || !modelReady)
		{
			var unconfigured = Model3DCache.ModelSourceUrlTemplate.Length == 0
				|| Model3DCache.ViewerRuntimeUrl.Length == 0;

			ShowStatus(
				"No 3D model available",
				unconfigured
					? "This build has no download source configured for 3D models, so none can be fetched."
					: "This model could not be downloaded. Check the connection and try again.",
				detail: "The 2D sprite is shown instead.",
				warning: null,
				canRetry: !unconfigured);
			return;
		}

		var url = started.ViewerUrl(speciesId);
		if (url is null)
		{
			ShowStatus(
				"3D view is unavailable",
				"The 3D viewer stopped before the model could be shown.",
				detail: null,
				warning: null,
				canRetry: true);
			return;
		}

		ShowWebView(url);
	}

	async Task TeardownAsync()
	{
		var cts = work;
		work = null;
		if (cts is not null)
		{
			await cts.CancelAsync().ConfigureAwait(true);
			cts.Dispose();
		}

		var current = server;
		server = null;
		if (current is not null)
			await current.DisposeAsync().ConfigureAwait(true);

		ModelWebView.IsVisible = false;
		StatusPanel.IsVisible = true;
	}

	void ShowWebView(string url)
	{
		StatusPanel.IsVisible = false;
		ModelWebView.IsVisible = true;
		ModelWebView.Source = url;
		FooterLabel.Text = "Drag to rotate, pinch to zoom. Downloaded on demand and cached on this device.";
	}

	void ShowStatus(string title, string body, string? detail, string? warning, bool canRetry)
	{
		ModelWebView.IsVisible = false;
		StatusPanel.IsVisible = true;
		FooterLabel.Text = "Models are downloaded on demand and cached on this device. Nothing is bundled with the app.";

		// Left unassigned (rather than assigned null) when there is no species: a MAUI Image
		// whose Source never resolves simply renders nothing, which is this app's standing
		// missing-asset convention.
		if (speciesId != 0)
			FallbackSprite.Source = SpriteHelper.SpeciesSpriteFile(speciesId, shiny: false);
		FallbackSprite.IsVisible = speciesId != 0;

		StatusTitleLabel.Text = title;
		StatusBodyLabel.Text = body;

		StatusDetailLabel.Text = detail ?? string.Empty;
		StatusDetailLabel.IsVisible = detail is not null;

		StatusWarningLabel.Text = warning ?? string.Empty;
		StatusWarningLabel.IsVisible = warning is not null;

		RetryButton.IsVisible = canRetry;
	}
}
