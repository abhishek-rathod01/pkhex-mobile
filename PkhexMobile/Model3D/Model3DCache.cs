using System.Globalization;

namespace PkhexMobile.Model3D;

/// <summary>
/// Fetch-on-demand disk cache for the 3D viewer's assets: one <c>.glb</c> per species, plus the
/// <c>model-viewer</c> web component's JavaScript bundle. Everything lands under
/// <see cref="CacheRoot"/> (a subfolder of <see cref="FileSystem.CacheDirectory"/>), and
/// <see cref="LoopbackModelServer"/> serves that folder - and only that folder - over loopback
/// HTTP.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing is bundled in the repo.</b> That is a standing decision (WAKEUP.md, 2026-07-25):
/// the full model set is ~214MB and the hi-res artwork set another ~292MB; bundling both would
/// add roughly half a gigabyte to the APK. The abandoned <c>3d-models-experimental</c> branch
/// bundled the models as <c>MauiAsset</c>s, which is exactly why that branch can never be merged.
/// </para>
/// <para>
/// Every fetch here fails <i>silently and cleanly</i>. Offline, DNS failure, 404, a truncated
/// body, a cancelled navigation - all collapse to <c>false</c>, and the caller shows the user an
/// honest "not available" message. No exception escapes, no partial file is left behind, nothing
/// is retried in a loop.
/// </para>
/// </remarks>
public static class Model3DCache
{
	// =====================================================================================
	// THE UPSTREAM SOURCE URL IS NOT DECIDED YET. BOTH CONSTANTS BELOW ARE EMPTY ON PURPOSE.
	//
	// Until someone supplies real values, EnsureModelAsync/EnsureViewerRuntimeAsync return
	// false immediately and never touch the network. That is the intended behaviour of the
	// unconfigured state, not a bug: shipping a guessed URL would mean the first thing this
	// feature does on a user's device is issue an unexplained request to a host nobody vetted.
	//
	// Whoever fills these in owns three questions, and they are licensing/policy questions,
	// not coding ones:
	//   1. Licence. WAKEUP.md records that non-official asset sources are acceptable when
	//      non-commercial, well documented and disclaimed - PokeAPI/sprites (CC0) cleared that
	//      bar for artwork. The 3D asset source the experimental branch named
	//      (github.com/Pokemon-3D-api/assets) has NOT been checked the same way. Check it.
	//   2. Stability. A raw github.com/raw.githubusercontent.com path pinned to a commit SHA
	//      survives upstream force-pushes; a branch path does not.
	//   3. Size. The largest known model is species 979 at ~8.2MB. That is the one to test
	//      against first, per WAKEUP.md - not species 1.
	// =====================================================================================

	/// <summary>
	/// Where a single species' <c>.glb</c> is fetched from. <c>{0}</c> is substituted with the
	/// decimal PKHeX species id (no zero padding - the experimental branch's convention was
	/// <c>6.glb</c>, not <c>0006.glb</c>, unlike the 2D sprite naming).
	/// <b>Empty means unconfigured; see the block comment above.</b>
	/// </summary>
	public const string ModelSourceUrlTemplate = "";

	/// <summary>
	/// Where the <c>model-viewer</c> web component bundle is fetched from. The viewer page needs
	/// this to exist alongside the model; without it the WebView loads a blank document.
	/// <b>Empty means unconfigured; see the block comment above.</b>
	/// </summary>
	/// <remarks>
	/// An alternative worth considering instead of fetching it: vendor <c>model-viewer.min.js</c>
	/// as a <c>MauiAsset</c> under <c>Resources/Raw/</c> (it is a few hundred KB of text, not a
	/// model blob, so it does not carry the size objection that killed asset bundling) and copy
	/// it into <see cref="CacheRoot"/> on first run via
	/// <see cref="FileSystem.OpenAppPackageFileAsync"/>. That removes one network dependency and
	/// makes the viewer shell work offline. It was not done here only because this port was
	/// scoped to a fixed file list that excludes the csproj and the Resources tree.
	/// </remarks>
	public const string ViewerRuntimeUrl = "";

	/// <summary>File name the viewer runtime is cached under, and served as.</summary>
	public const string ViewerRuntimeFileName = "model-viewer.min.js";

	/// <summary>
	/// How long a single asset fetch may take before it is abandoned. Generous because the
	/// largest model is ~8.2MB and a phone on mobile data is a realistic case, but bounded so a
	/// dead connection cannot leave the page waiting forever.
	/// </summary>
	static readonly TimeSpan FetchTimeout = TimeSpan.FromMinutes(2);

	// One shared client. Creating an HttpClient per fetch is the classic socket-exhaustion bug;
	// it also re-does TLS handshakes that connection reuse would have amortised.
	static readonly HttpClient Http = new()
	{
		Timeout = Timeout.InfiniteTimeSpan, // cancellation is driven by the CancellationToken below
	};

	/// <summary>
	/// Absolute path of the cache folder. Computed on each access rather than cached in a static
	/// field: <see cref="FileSystem.CacheDirectory"/> touches platform state, and a static
	/// initialiser would pin whatever it returned the very first time any member of this class
	/// was touched.
	/// </summary>
	public static string CacheRoot => Path.Combine(FileSystem.CacheDirectory, "model3d");

	/// <summary>File name a species' model is cached under, and served as.</summary>
	public static string ModelFileName(ushort species) =>
		string.Create(CultureInfo.InvariantCulture, $"model_{species}.glb");

	/// <summary>Absolute on-disk path a species' model would occupy.</summary>
	public static string ModelPath(ushort species) => Path.Combine(CacheRoot, ModelFileName(species));

	/// <summary>Absolute on-disk path the viewer runtime would occupy.</summary>
	public static string ViewerRuntimePath() => Path.Combine(CacheRoot, ViewerRuntimeFileName);

	/// <summary>
	/// Whether this species' model is already on disk. Cheap enough to call from the UI thread;
	/// it is one <c>stat</c>. A zero-byte file counts as absent - that is the shape a fetch
	/// interrupted at exactly the wrong moment would leave behind, and treating it as present
	/// would hand the WebView a corrupt model.
	/// </summary>
	public static bool IsModelCached(ushort species) => IsUsableFile(ModelPath(species));

	/// <summary>Whether the viewer runtime bundle is already on disk.</summary>
	public static bool IsViewerRuntimeCached() => IsUsableFile(ViewerRuntimePath());

	/// <summary>
	/// Ensures this species' model is on disk, fetching it if it is not.
	/// </summary>
	/// <returns><c>true</c> if the model is present and usable when this returns.</returns>
	public static Task<bool> EnsureModelAsync(ushort species, CancellationToken cancellationToken)
	{
		if (species == 0)
			return Task.FromResult(false);

		var url = ModelSourceUrlTemplate.Length == 0
			? string.Empty
			: string.Format(CultureInfo.InvariantCulture, ModelSourceUrlTemplate, species);

		return EnsureFileAsync(url, ModelPath(species), cancellationToken);
	}

	/// <summary>
	/// Ensures the <c>model-viewer</c> runtime bundle is on disk, fetching it if it is not.
	/// </summary>
	/// <returns><c>true</c> if the bundle is present and usable when this returns.</returns>
	public static Task<bool> EnsureViewerRuntimeAsync(CancellationToken cancellationToken) =>
		EnsureFileAsync(ViewerRuntimeUrl, ViewerRuntimePath(), cancellationToken);

	/// <summary>
	/// Deletes everything this class has cached. Not wired to any UI; present because the cache
	/// is unbounded (one file per species the user ever looks at) and whoever adds a "clear
	/// downloaded models" affordance will want it.
	/// </summary>
	public static void Clear()
	{
		try
		{
			var root = CacheRoot;
			if (Directory.Exists(root))
				Directory.Delete(root, recursive: true);
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	static bool IsUsableFile(string path)
	{
		try
		{
			var info = new FileInfo(path);
			return info.Exists && info.Length > 0;
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	/// <summary>
	/// The one download path. Downloads to a temp file next to the destination and moves it into
	/// place only on success, so a cancelled or failed fetch can never leave a half-written file
	/// that <see cref="IsUsableFile"/> would later accept.
	/// </summary>
	static async Task<bool> EnsureFileAsync(string url, string destination, CancellationToken cancellationToken)
	{
		if (!Model3DFeature.IsEnabled)
			return false;

		if (IsUsableFile(destination))
			return true;

		// Unconfigured source (see the block comment at the top of this class), or a value that
		// is not a well-formed absolute URL. Either way: no network, no exception, no work.
		if (url.Length == 0 || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
			return false;

		string? temp = null;
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? CacheRoot);

			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(FetchTimeout);
			var token = timeout.Token;

			using var response = await Http
				.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token)
				.ConfigureAwait(false);

			if (!response.IsSuccessStatusCode)
				return false;

			temp = destination + ".part-" + Guid.NewGuid().ToString("N");

			await using (var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
			await using (var target = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
			{
				await source.CopyToAsync(target, token).ConfigureAwait(false);
			}

			if (!IsUsableFile(temp))
				return false;

			// Overwrite: a previously-rejected zero-byte file may be sitting at the destination.
			File.Move(temp, destination, overwrite: true);
			temp = null;
			return true;
		}
		catch (HttpRequestException)
		{
			return false;
		}
		catch (OperationCanceledException)
		{
			return false;
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
		catch (NotSupportedException)
		{
			return false;
		}
		finally
		{
			if (temp is not null)
			{
				try
				{
					File.Delete(temp);
				}
				catch (IOException)
				{
				}
				catch (UnauthorizedAccessException)
				{
				}
			}
		}
	}
}
