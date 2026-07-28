using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PkhexMobile.Model3D;

/// <summary>
/// A minimal, loopback-only, read-only HTTP server over the on-disk model cache.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a server at all.</b> Two earlier on-device prototypes tried to render <c>.glb</c>
/// models in a WebView from a non-HTTP origin and both failed to render anything (WAKEUP.md,
/// "3D viewer investigation"). The failures looked like consequences of an opaque or missing
/// page origin: <c>blob:</c> URL creation for textures and Web Worker construction for the Draco
/// mesh decoder are both origin-sensitive, and both broke. Serving over
/// <c>http://127.0.0.1:port/</c> gives the page a real, ordinary, same-origin HTTP origin, which
/// is the single change this whole approach is betting on.
/// </para>
/// <para>
/// It also lets the page be a plain <c>WebView</c> with a <c>Source</c> URL. That deletes the
/// <c>HybridWebView</c> dependency entirely, and with it the real crash the experimental branch
/// hit: <c>PlatformView cannot be null here</c> thrown from
/// <c>HybridWebViewHandler.MapEvaluateJavaScriptAsync</c>, MAUI's own internal reaction to a
/// <c>DefaultFile</c> change firing before the native WebView exists. There is no
/// <c>DefaultFile</c> here, no <c>EvaluateJavaScriptAsync</c>, and no JS-to-C# bridge - the
/// experimental branch's notes record the bridge never round-tripping on that MAUI/WebView
/// version anyway.
/// </para>
/// <para>
/// <b>The central unknown: does <see cref="HttpListener"/> work on Android?</b> It is not
/// established that it does. On some .NET runtime configurations <see cref="HttpListener"/>
/// throws <see cref="PlatformNotSupportedException"/> at construction. That has not been tested
/// on a device here, and could not be - see <c>docs/3D-VIEWER-STATUS.md</c> for the reasoning
/// and the (deliberately hedged) conclusion. This class therefore treats "the server would not
/// start" as an ordinary, expected outcome: <see cref="StartAsync"/> always returns an instance,
/// <see cref="IsAvailable"/> tells you whether it is usable, and <see cref="UnavailableReason"/>
/// carries a message the page can show the user. Nothing here throws at the caller.
/// </para>
/// <para>
/// <b>Threading.</b> Everything is async and everything runs off the UI thread. The listener is
/// constructed and started inside <see cref="Task.Run(Action)"/> because socket bind can block,
/// and the accept loop lives on the thread pool for the server's whole life. This project has a
/// documented ANR-class bug from doing expensive work synchronously on the UI thread
/// (<c>RefreshLegality</c>); do not reintroduce that shape here.
/// </para>
/// <para>
/// <b>Exposure.</b> Bound to <c>127.0.0.1</c> only, so nothing outside this device can reach it,
/// and on an ephemeral port so it neither collides with nor squats on a well-known one. It
/// serves only <c>GET</c>/<c>HEAD</c>, only from the cache directory, and refuses any path that
/// resolves outside it.
/// </para>
/// </remarks>
public sealed class LoopbackModelServer : IDisposable, IAsyncDisposable
{
	/// <summary>URL path prefix under which cached files are served.</summary>
	const string CachePrefix = "/cache/";

	/// <summary>URL path prefix for the generated per-species viewer page.</summary>
	const string ViewerPrefix = "/viewer/";

	/// <summary>How many times to retry the bind when the probed port is taken before giving up.</summary>
	const int BindAttempts = 5;

	/// <summary>
	/// The generated viewer page, with <c>__RUNTIME__</c> and <c>__MODEL__</c> substituted per
	/// request. A plain (non-interpolated) raw string on purpose - the CSS below is full of
	/// braces, and brace escaping inside an interpolated raw string literal is exactly the kind
	/// of detail that is not worth being clever about in code that cannot be compiled here.
	/// </summary>
	const string ViewerPageTemplate = """
		<!DOCTYPE html>
		<html>
		<head>
		<meta charset="utf-8">
		<meta name="viewport" content="width=device-width, initial-scale=1.0">
		<script type="module" src="__RUNTIME__"></script>
		<style>
		html, body { margin:0; padding:0; height:100%; background:#F6F8FB; overflow:hidden; }
		model-viewer { width:100%; height:100%; }
		</style>
		</head>
		<body>
		<model-viewer id="mv" src="__MODEL__" camera-controls auto-rotate shadow-intensity="1"></model-viewer>
		</body>
		</html>
		""";

	readonly HttpListener? listener;
	readonly string cacheRoot;
	readonly CancellationTokenSource shutdown = new();
	readonly CancellationToken shutdownToken;
	Task acceptLoop = Task.CompletedTask;
	bool disposed;

	LoopbackModelServer(HttpListener? listener, string cacheRoot, string baseUrl, string? unavailableReason)
	{
		this.listener = listener;
		this.cacheRoot = cacheRoot;
		shutdownToken = shutdown.Token;
		BaseUrl = baseUrl;
		UnavailableReason = unavailableReason;
	}

	/// <summary>
	/// Whether the server actually came up. When <c>false</c>, <see cref="UnavailableReason"/>
	/// says why and no URL from this instance will resolve.
	/// </summary>
	public bool IsAvailable => listener is not null && UnavailableReason is null;

	/// <summary>
	/// A short, user-showable explanation of why the server is unusable, or <c>null</c> when it
	/// is fine. Deliberately plain text: it goes straight into a label, not a log.
	/// </summary>
	public string? UnavailableReason { get; }

	/// <summary>
	/// Root URL including the trailing slash, e.g. <c>http://127.0.0.1:49215/</c>. Empty when
	/// the server is unavailable.
	/// </summary>
	public string BaseUrl { get; }

	/// <summary>
	/// Absolute URL of the generated viewer page for a species. Returns <c>null</c> when the
	/// server is unavailable, so a caller cannot accidentally point a WebView at nothing.
	/// </summary>
	public string? ViewerUrl(ushort species) => IsAvailable
		? string.Create(CultureInfo.InvariantCulture, $"{BaseUrl}viewer/{species}.html")
		: null;

	/// <summary>
	/// Starts a server over <paramref name="cacheRoot"/>. Never throws and never returns
	/// <c>null</c>: on any failure it returns an instance with <see cref="IsAvailable"/> false
	/// and <see cref="UnavailableReason"/> set.
	/// </summary>
	public static async Task<LoopbackModelServer> StartAsync(string cacheRoot, CancellationToken cancellationToken)
	{
		if (!Model3DFeature.IsEnabled)
			return Unavailable(cacheRoot, "3D model viewing is turned off in this build.");

		var server = await Task.Run(() => StartCore(cacheRoot), cancellationToken).ConfigureAwait(false);
		if (server.IsAvailable)
			server.acceptLoop = Task.Run(server.AcceptLoopAsync, CancellationToken.None);
		return server;
	}

	static LoopbackModelServer Unavailable(string cacheRoot, string reason) =>
		new(null, cacheRoot, string.Empty, reason);

	static LoopbackModelServer StartCore(string cacheRoot)
	{
		string root;
		try
		{
			Directory.CreateDirectory(cacheRoot);
			root = Path.GetFullPath(cacheRoot);
		}
		catch (IOException)
		{
			return Unavailable(cacheRoot, "The device storage used to cache 3D models is not writable.");
		}
		catch (UnauthorizedAccessException)
		{
			return Unavailable(cacheRoot, "The device storage used to cache 3D models is not writable.");
		}

		for (var attempt = 0; attempt < BindAttempts; attempt++)
		{
			int port;
			try
			{
				port = FindFreePort();
			}
			catch (SocketException)
			{
				return Unavailable(root, "The device would not allow a local connection for the 3D viewer.");
			}

			var baseUrl = string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port}/");
			HttpListener? candidate = null;
			try
			{
				// Construction is the specific call believed most likely to throw
				// PlatformNotSupportedException on Android. Keep it inside the try.
				candidate = new HttpListener();
				candidate.Prefixes.Add(baseUrl);
				candidate.Start();
				return new LoopbackModelServer(candidate, root, baseUrl, null);
			}
			catch (PlatformNotSupportedException)
			{
				candidate?.Close();
				// The definitive negative answer to the central unknown. Do not retry - no
				// number of further attempts will make the platform support it.
				return Unavailable(root, "This device's system components do not support the local connection the 3D viewer needs.");
			}
			catch (HttpListenerException)
			{
				// Usually "address already in use": something grabbed the probed port in the
				// gap between the probe closing and this bind. Probe again.
				candidate?.Close();
			}
			catch (SocketException)
			{
				candidate?.Close();
			}
			catch (ObjectDisposedException)
			{
				candidate?.Close();
			}
		}

		return Unavailable(root, "Could not open a local connection for the 3D viewer. Try again.");
	}

	/// <summary>
	/// Finds a free loopback port by binding one with the OS-assigned port 0 and releasing it.
	/// </summary>
	/// <remarks>
	/// <see cref="HttpListener"/> prefixes require a literal port, so port 0 cannot be handed to
	/// it directly - hence the probe. There is a small window between releasing the probe and
	/// binding the listener in which another process could take the port; that is what the
	/// retry loop in <see cref="StartCore"/> is for. Hard-coding a port (8080 and friends) is
	/// the alternative and is worse: it collides with whatever else is on the device and makes
	/// the server trivially predictable.
	/// </remarks>
	static int FindFreePort()
	{
		var probe = new TcpListener(IPAddress.Loopback, 0);
		probe.Start();
		try
		{
			return ((IPEndPoint)probe.LocalEndpoint).Port;
		}
		finally
		{
			probe.Stop();
		}
	}

	async Task AcceptLoopAsync()
	{
		var local = listener;
		if (local is null)
			return;

		while (!shutdownToken.IsCancellationRequested)
		{
			HttpListenerContext context;
			try
			{
				context = await local.GetContextAsync().ConfigureAwait(false);
			}
			catch (HttpListenerException)
			{
				return; // listener stopped
			}
			catch (ObjectDisposedException)
			{
				return;
			}
			catch (InvalidOperationException)
			{
				return;
			}

			// Deliberately not awaited: one slow response (an 8MB model over a throttled
			// emulator loopback) must not stall the accept of the next request. HandleAsync
			// swallows everything, so this cannot produce an unobserved faulted task.
			_ = HandleAsync(context);
		}
	}

	async Task HandleAsync(HttpListenerContext context)
	{
		try
		{
			var request = context.Request;
			var response = context.Response;

			var isHead = string.Equals(request.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase);
			if (!isHead && !string.Equals(request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
			{
				await WriteStatusAsync(response, HttpStatusCode.MethodNotAllowed).ConfigureAwait(false);
				return;
			}

			var path = request.Url?.AbsolutePath ?? "/";

			if (path.StartsWith(ViewerPrefix, StringComparison.Ordinal))
			{
				await ServeViewerPageAsync(response, path[ViewerPrefix.Length..], isHead).ConfigureAwait(false);
				return;
			}

			if (path.StartsWith(CachePrefix, StringComparison.Ordinal))
			{
				await ServeCacheFileAsync(response, path[CachePrefix.Length..], isHead).ConfigureAwait(false);
				return;
			}

			await WriteStatusAsync(context.Response, HttpStatusCode.NotFound).ConfigureAwait(false);
		}
		catch (HttpListenerException)
		{
			// Client (the WebView) went away mid-response - navigating away does this routinely.
		}
		catch (ObjectDisposedException)
		{
		}
		catch (IOException)
		{
		}
		catch (InvalidOperationException)
		{
		}
	}

	/// <summary>
	/// Serves the tiny generated wrapper page for a species: a <c>model-viewer</c> element
	/// pointed at that species' cached <c>.glb</c>.
	/// </summary>
	/// <remarks>
	/// Generated in memory rather than written to disk, and parameterised by URL path rather
	/// than by file name. The experimental branch was forced into one pre-generated HTML file
	/// per species (1000+ files) purely because <c>HybridWebView.DefaultFile</c> is matched as a
	/// literal asset filename - neither <c>?query</c> nor <c>#fragment</c> survived it. Over
	/// real HTTP that constraint simply does not exist, which is the second reason this approach
	/// is worth trying.
	/// </remarks>
	async Task ServeViewerPageAsync(HttpListenerResponse response, string tail, bool isHead)
	{
		if (!tail.EndsWith(".html", StringComparison.Ordinal)
			|| !ushort.TryParse(tail[..^5], NumberStyles.None, CultureInfo.InvariantCulture, out var species)
			|| species == 0)
		{
			await WriteStatusAsync(response, HttpStatusCode.NotFound).ConfigureAwait(false);
			return;
		}

		// Root-relative URLs, not document-relative: the page itself lives at "/viewer/25.html",
		// so a bare "cache/..." would resolve to "/viewer/cache/..." and 404.
		// Both names are plain generated strings ("model_25.glb", "model-viewer.min.js") with no
		// user-controlled text in them, so there is nothing here to HTML-escape.
		var html = ViewerPageTemplate
			.Replace("__RUNTIME__", CachePrefix + Model3DCache.ViewerRuntimeFileName, StringComparison.Ordinal)
			.Replace("__MODEL__", CachePrefix + Model3DCache.ModelFileName(species), StringComparison.Ordinal);

		var bytes = Encoding.UTF8.GetBytes(html);
		response.StatusCode = (int)HttpStatusCode.OK;
		response.ContentType = "text/html; charset=utf-8";
		response.ContentLength64 = bytes.Length;
		if (!isHead)
			await response.OutputStream.WriteAsync(bytes, shutdownToken).ConfigureAwait(false);
		response.Close();
	}

	async Task ServeCacheFileAsync(HttpListenerResponse response, string relative, bool isHead)
	{
		var resolved = ResolveCachePath(relative);
		if (resolved is null || !File.Exists(resolved))
		{
			await WriteStatusAsync(response, HttpStatusCode.NotFound).ConfigureAwait(false);
			return;
		}

		FileStream stream;
		try
		{
			stream = new FileStream(resolved, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
		}
		catch (IOException)
		{
			await WriteStatusAsync(response, HttpStatusCode.NotFound).ConfigureAwait(false);
			return;
		}
		catch (UnauthorizedAccessException)
		{
			await WriteStatusAsync(response, HttpStatusCode.Forbidden).ConfigureAwait(false);
			return;
		}

		await using (stream)
		{
			response.StatusCode = (int)HttpStatusCode.OK;
			response.ContentType = ContentTypeFor(resolved);
			response.ContentLength64 = stream.Length;
			if (!isHead)
				await stream.CopyToAsync(response.OutputStream, shutdownToken).ConfigureAwait(false);
		}

		response.Close();
	}

	/// <summary>
	/// Maps a request path onto a file inside the cache root, or returns <c>null</c> if it
	/// escapes.
	/// </summary>
	/// <remarks>
	/// The guard is "normalise, then check the result is still under the root", not a blacklist
	/// of <c>..</c> and friends. Blacklists lose to encoding tricks; normalisation does not,
	/// because it answers the only question that matters - where does this actually point. The
	/// root comparison is <see cref="StringComparison.Ordinal"/> deliberately: Android's
	/// filesystem is case-sensitive, so a case-insensitive comparison would be answering a
	/// different question than the one the OS will answer when it opens the file.
	/// </remarks>
	string? ResolveCachePath(string relative)
	{
		if (relative.Length == 0)
			return null;

		string decoded;
		try
		{
			decoded = Uri.UnescapeDataString(relative);
		}
		catch (UriFormatException)
		{
			return null;
		}

		if (decoded.Contains('\0', StringComparison.Ordinal))
			return null;

		// A rooted path would make Path.Combine discard the cache root entirely.
		if (Path.IsPathRooted(decoded))
			return null;

		string full;
		try
		{
			full = Path.GetFullPath(Path.Combine(cacheRoot, decoded));
		}
		catch (ArgumentException)
		{
			return null;
		}
		catch (PathTooLongException)
		{
			return null;
		}
		catch (NotSupportedException)
		{
			return null;
		}

		var rootWithSeparator = cacheRoot.EndsWith(Path.DirectorySeparatorChar)
			? cacheRoot
			: cacheRoot + Path.DirectorySeparatorChar;

		return full.StartsWith(rootWithSeparator, StringComparison.Ordinal) ? full : null;
	}

	static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
	{
		".glb" => "model/gltf-binary",
		".gltf" => "model/gltf+json",
		".bin" => "application/octet-stream",
		".html" or ".htm" => "text/html; charset=utf-8",
		".js" or ".mjs" => "application/javascript; charset=utf-8",
		".css" => "text/css; charset=utf-8",
		".json" => "application/json; charset=utf-8",
		".wasm" => "application/wasm",
		".png" => "image/png",
		".jpg" or ".jpeg" => "image/jpeg",
		".webp" => "image/webp",
		".ktx2" => "image/ktx2",
		_ => "application/octet-stream",
	};

	static Task WriteStatusAsync(HttpListenerResponse response, HttpStatusCode status)
	{
		response.StatusCode = (int)status;
		response.ContentLength64 = 0;
		response.Close();
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public void Dispose()
	{
		if (disposed)
			return;
		disposed = true;

		shutdown.Cancel();
		try
		{
			listener?.Stop();
			listener?.Close();
		}
		catch (ObjectDisposedException)
		{
		}

		// The CancellationTokenSource is deliberately NOT disposed. In-flight responses still
		// hold shutdownToken; registering a callback on a token whose source has been disposed
		// throws, and the only thing disposing would buy back is a handful of bytes.
	}

	/// <summary>
	/// Shuts down and waits (briefly, bounded) for the accept loop to unwind, so a page that
	/// disposes on navigation does not leave a thread-pool task holding a dead socket.
	/// </summary>
	public async ValueTask DisposeAsync()
	{
		if (disposed)
			return;

		var loop = acceptLoop;
		Dispose();

		try
		{
			await loop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
		}
		catch (OperationCanceledException)
		{
		}
	}
}
