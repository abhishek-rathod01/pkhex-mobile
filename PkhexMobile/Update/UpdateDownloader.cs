using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Maui.Storage;

namespace PkhexMobile.Update;

/// <summary>
/// How a download ended. Cancellation is deliberately distinct from failure: the
/// user asking to stop is not an error and must not be surfaced as one.
/// </summary>
public enum DownloadOutcome
{
	/// <summary>The APK is on disk and its size matches the published asset size.</summary>
	Success,

	/// <summary>The caller's <see cref="CancellationToken"/> fired. The partial file was deleted.</summary>
	Cancelled,

	/// <summary>Network error, bad status, or a size mismatch. Nothing usable was left on disk.</summary>
	Failed,
}

/// <summary>
/// Outcome of an <see cref="UpdateDownloader"/> download.
/// </summary>
/// <remarks>
/// Lives here rather than in UpdateContracts.cs because it is an implementation
/// detail of the downloader, not part of the platform-facing contract surface.
/// </remarks>
public sealed record DownloadResult(
	DownloadOutcome Outcome,
	string? FilePath,
	string? ErrorMessage)
{
	/// <summary>True only when an APK of the expected size is sitting at <see cref="FilePath"/>.</summary>
	public bool IsSuccess => Outcome == DownloadOutcome.Success && !string.IsNullOrEmpty(FilePath);

	/// <summary>A verified APK at <paramref name="path"/>.</summary>
	public static DownloadResult Ok(string path) => new(DownloadOutcome.Success, path, null);

	/// <summary>The user cancelled; any partial file has already been removed.</summary>
	public static DownloadResult Cancelled { get; } = new(DownloadOutcome.Cancelled, null, null);

	/// <summary>A failure the caller may show verbatim - <paramref name="message"/> is never null.</summary>
	public static DownloadResult Failure(string message) => new(DownloadOutcome.Failed, null, message);
}

/// <summary>
/// Streams a release APK into the app's private cache directory.
/// </summary>
/// <remarks>
/// <para>
/// The cache directory is used on purpose: it needs no storage permission, the OS
/// may reclaim it, and it is the only location a FileProvider is wired to expose
/// (see Platforms/Android/Resources/xml/file_paths.xml). Never write the APK to
/// app data or to shared storage.
/// </para>
/// <para>
/// Nothing here touches the UI. Progress is handed back through
/// <see cref="IProgress{T}"/> so the caller decides which thread renders it.
/// </para>
/// </remarks>
public sealed class UpdateDownloader
{
	/// <summary>Copy buffer. Release APKs are tens of MB - they are never buffered whole in memory.</summary>
	private const int BufferSize = 81920;

	/// <summary>Minimum gap between progress callbacks, so a fast link cannot flood the UI.</summary>
	private const long ProgressIntervalMs = 100;

	/// <summary>
	/// Shared client with NO timeout: <see cref="HttpClient.Timeout"/> covers the
	/// whole exchange including the body, so the default 100s would abort any
	/// large APK mid-stream. Cancellation is the caller's job via the token.
	/// A static field also keeps this class free of disposable instance state.
	/// </summary>
	private static readonly HttpClient SharedClient = new()
	{
		Timeout = System.Threading.Timeout.InfiniteTimeSpan,
	};

	/// <summary>
	/// Deterministic cache filename for a version, e.g. <c>PkhexMobile-1.2.3.apk</c>.
	/// Deterministic so a re-download simply overwrites the previous attempt instead
	/// of littering the cache with one file per try.
	/// </summary>
	/// <param name="version">Display version or tag. Anything outside [A-Za-z0-9._-] is replaced.</param>
	public static string FileNameForVersion(string version)
	{
		if (string.IsNullOrWhiteSpace(version))
			return "PkhexMobile-update.apk";

		char[] cleaned = new char[version.Length];
		for (int i = 0; i < version.Length; i++)
		{
			char c = version[i];
			cleaned[i] = char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-';
		}

		return $"PkhexMobile-{new string(cleaned)}.apk";
	}

	/// <summary>Full path the APK for <paramref name="version"/> would be written to.</summary>
	public static string CachePathForVersion(string version) =>
		Path.Combine(FileSystem.CacheDirectory, FileNameForVersion(version));

	/// <summary>
	/// Downloads the APK asset of <paramref name="release"/>.
	/// </summary>
	/// <param name="release">Release to fetch; must carry an APK URL.</param>
	/// <param name="progress">Optional progress sink. Reported off the UI thread.</param>
	/// <param name="cancellationToken">Cancels the download and deletes the partial file.</param>
	public Task<DownloadResult> DownloadAsync(
		ReleaseInfo release,
		IProgress<DownloadProgress>? progress = null,
		CancellationToken cancellationToken = default)
	{
		if (release is null)
			return Task.FromResult(DownloadResult.Failure("No release to download."));

		if (string.IsNullOrWhiteSpace(release.ApkDownloadUrl))
			return Task.FromResult(DownloadResult.Failure("This release has no APK attached."));

		string version = string.IsNullOrWhiteSpace(release.DisplayVersion) ? release.TagName : release.DisplayVersion;
		return DownloadAsync(release.ApkDownloadUrl, version, release.ApkSizeBytes, progress, cancellationToken);
	}

	/// <summary>
	/// Downloads <paramref name="url"/> into the cache directory and verifies its size.
	/// </summary>
	/// <param name="url">Absolute http/https URL of the APK asset.</param>
	/// <param name="version">Version string used to build the cache filename.</param>
	/// <param name="expectedSizeBytes">
	/// Published asset size. When positive it is enforced against the bytes on disk;
	/// a mismatch deletes the file and fails. Zero means "unknown" - only emptiness is checked.
	/// </param>
	/// <param name="progress">Optional progress sink.</param>
	/// <param name="cancellationToken">Cancels the download and deletes the partial file.</param>
	public async Task<DownloadResult> DownloadAsync(
		string url,
		string version,
		long expectedSizeBytes,
		IProgress<DownloadProgress>? progress = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(url) ||
			!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
			(uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
		{
			return DownloadResult.Failure("The download link for this release is not valid.");
		}

		string destination;
		try
		{
			// CacheDirectory exists on a healthy install, but a cleared cache can race us.
			Directory.CreateDirectory(FileSystem.CacheDirectory);
			destination = CachePathForVersion(version);

			// A leftover file from an interrupted attempt must never be reused: we cannot
			// tell a complete one from a truncated one until the size check below passes.
			TryDelete(destination);
		}
		catch (Exception ex)
		{
			return DownloadResult.Failure($"Could not prepare the download folder: {ex.Message}");
		}

		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, uri);

			// GitHub rejects UA-less requests on some endpoints; asset redirects are
			// happier with an explicit octet-stream Accept.
			request.Headers.UserAgent.ParseAdd("PkhexMobile-Updater");
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

			using HttpResponseMessage response = await SharedClient
				.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
				.ConfigureAwait(false);

			if (!response.IsSuccessStatusCode)
				return DownloadResult.Failure($"Download failed ({(int)response.StatusCode} {response.ReasonPhrase}).");

			// Prefer the server's own length for the progress bar; fall back to the
			// release metadata so the user still gets a percentage.
			long total = response.Content.Headers.ContentLength ?? (expectedSizeBytes > 0 ? expectedSizeBytes : 0);
			long received = 0;

			// Scoped so both streams are closed (and the file flushed) before the size
			// check and any delete below - Android will not let us delete an open file.
			{
				await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
				await using var sink = new FileStream(
					destination,
					FileMode.Create,
					FileAccess.Write,
					FileShare.None,
					BufferSize,
					useAsync: true);

				byte[] buffer = new byte[BufferSize];
				long lastReport = 0;

				int read;
				while ((read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false)) > 0)
				{
					await sink.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
					received += read;

					long now = Environment.TickCount64;
					if (progress is not null && now - lastReport >= ProgressIntervalMs)
					{
						lastReport = now;
						progress.Report(new DownloadProgress(received, total));
					}
				}

				await sink.FlushAsync(cancellationToken).ConfigureAwait(false);
			}

			// Always end on a truthful final tick, even if the throttle swallowed the last one.
			progress?.Report(new DownloadProgress(received, total > 0 ? total : received));

			long onDisk = new FileInfo(destination).Length;

			if (onDisk == 0)
			{
				TryDelete(destination);
				return DownloadResult.Failure("The download was empty.");
			}

			// The whole point of this check: a silently truncated APK must never reach
			// the package installer, where it surfaces as an unhelpful "parse error".
			if (expectedSizeBytes > 0 && onDisk != expectedSizeBytes)
			{
				TryDelete(destination);
				return DownloadResult.Failure(
					$"The downloaded file is incomplete ({onDisk:N0} of {expectedSizeBytes:N0} bytes).");
			}

			return DownloadResult.Ok(destination);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			TryDelete(destination);
			return DownloadResult.Cancelled;
		}
		catch (Exception ex)
		{
			// Offline, DNS failure, TLS failure, disk full, mid-stream reset - the user
			// gets one calm message and no crash.
			TryDelete(destination);
			return DownloadResult.Failure($"Download failed: {ex.Message}");
		}
	}

	/// <summary>
	/// Removes a cached/partial APK, swallowing anything that goes wrong. A file we
	/// cannot delete is still never returned as a success, so failing quietly is safe.
	/// </summary>
	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
				File.Delete(path);
		}
		catch (Exception)
		{
			// Intentionally ignored - see summary.
		}
	}
}
