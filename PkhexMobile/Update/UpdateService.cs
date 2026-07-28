using System.Globalization;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace PkhexMobile.Update;

/// <summary>
/// Asks GitHub whether a newer release of the app exists.
/// </summary>
/// <remarks>
/// Design constraints this class exists to enforce:
/// <list type="bullet">
/// <item>
/// It NEVER surfaces a failure. Offline, DNS dead, timed out, rate-limited, no releases
/// published yet, garbage JSON - every one of those becomes
/// <see cref="UpdateCheckResult.Unknown"/> and the UI shows nothing. An update check is a
/// courtesy; an error toast about one is pure noise.
/// </item>
/// <item>
/// It NEVER blocks. Everything is async all the way down - no <c>.Result</c>, no
/// <c>.Wait()</c>, no <c>Task.Run</c> wrapping sync work. This project already shipped one
/// ANR-class bug from synchronous work on the UI thread; nothing here touches UI at all.
/// </item>
/// <item>
/// It hits the network at most once per 24h on its own. GitHub's unauthenticated API allows
/// 60 requests/hour per IP, and a check on every app launch would burn that (and the user's
/// data) for no benefit.
/// </item>
/// </list>
/// </remarks>
public sealed class UpdateService
{
	private const string LatestReleaseUrl =
		"https://api.github.com/repos/abhishek-rathod01/pkhex-mobile/releases/latest";

	// GitHub returns 403 Forbidden for any API request without a User-Agent. This is not
	// optional and there is no useful error message when it's missing.
	private const string UserAgentValue = "PkhexMobile-UpdateCheck";

	private const string PrefLastCheckUtc = "update_last_check_utc";
	private const string PrefChecksEnabled = "update_checks_enabled";
	private const string PrefSkippedTag = "update_skipped_tag";

	private static readonly TimeSpan MinimumCheckInterval = TimeSpan.FromHours(24);
	private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

	// One shared client for the process. A per-call HttpClient leaks sockets in TIME_WAIT and
	// eventually exhausts them. Declared after the timeouts it reads - static field
	// initialisers run in textual order.
	private static readonly HttpClient Http = CreateClient();

	private readonly string currentVersion;

	/// <summary>
	/// Creates the service.
	/// </summary>
	/// <param name="currentVersionOverride">
	/// The running build's version string. Leave null in the app - it is read from
	/// <c>AppInfo.Current.VersionString</c>. Supplying it explicitly is what lets the
	/// comparison logic be exercised without a MAUI app host.
	/// </param>
	public UpdateService(string? currentVersionOverride = null)
	{
		currentVersion = currentVersionOverride ?? ReadAppVersion();
	}

	/// <summary>The version string this service compares releases against.</summary>
	public string CurrentVersion => currentVersion;

	/// <summary>
	/// User preference: may the app check for updates on its own? Default true. When false,
	/// <see cref="CheckAutomaticAsync"/> makes no network call at all - but
	/// <see cref="CheckNowAsync"/> still works, because a manual tap is an explicit request.
	/// </summary>
	public bool UpdateChecksEnabled
	{
		get => GetPreference(PrefChecksEnabled, true);
		set => SetPreference(PrefChecksEnabled, value);
	}

	/// <summary>
	/// The release tag the user chose to skip, or null if none. Kept readable so a Settings
	/// screen can show "Skipping v1.2.3" and offer to clear it.
	/// </summary>
	public string? SkippedTag
	{
		get
		{
			string tag = GetPreference(PrefSkippedTag, string.Empty);
			return string.IsNullOrWhiteSpace(tag) ? null : tag;
		}
	}

	/// <summary>
	/// When the last network check was attempted (successful or not), or null if never.
	/// </summary>
	public DateTimeOffset? LastCheckUtc
	{
		get
		{
			string raw = GetPreference(PrefLastCheckUtc, string.Empty);
			if (string.IsNullOrWhiteSpace(raw))
				return null;
			return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
				DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;
		}
	}

	/// <summary>
	/// True when an automatic check would actually reach the network right now (checks
	/// enabled and the 24h gate elapsed). Purely informational - callers do not need to
	/// consult it before calling <see cref="CheckAutomaticAsync"/>.
	/// </summary>
	public bool IsAutomaticCheckDue => UpdateChecksEnabled && IsIntervalElapsed();

	/// <summary>
	/// The background check, safe to fire on app start. Respects the enabled preference, the
	/// 24h interval gate and the skipped tag; any of those returning early costs nothing.
	/// </summary>
	public Task<UpdateCheckResult> CheckAutomaticAsync(CancellationToken cancellationToken = default) =>
		CheckAsync(force: false, cancellationToken);

	/// <summary>
	/// The Settings screen's "Check now". Bypasses the enabled preference, the 24h gate AND
	/// the skipped tag - the user asked, so an answer they previously dismissed is still an
	/// answer they should see.
	/// </summary>
	public Task<UpdateCheckResult> CheckNowAsync(CancellationToken cancellationToken = default) =>
		CheckAsync(force: true, cancellationToken);

	/// <summary>
	/// Suppresses automatic prompts for <paramref name="tagName"/>. Only one tag is remembered -
	/// skipping a newer release replaces the older skip, so a user who skips v1.2.3 is still
	/// told about v1.2.4.
	/// </summary>
	public void SkipVersion(string? tagName)
	{
		if (string.IsNullOrWhiteSpace(tagName))
			return;
		SetPreference(PrefSkippedTag, tagName.Trim());
	}

	/// <summary>Forgets any skipped tag, so the next automatic check prompts again.</summary>
	public void ClearSkippedVersion() => SetPreference(PrefSkippedTag, string.Empty);

	private async Task<UpdateCheckResult> CheckAsync(bool force, CancellationToken cancellationToken)
	{
		// This catch-all is deliberate and is the whole contract of the class: an update check
		// has no failure mode worth showing a user, so every exception - HttpRequestException,
		// TaskCanceledException from the timeout, JsonException, a platform Preferences failure,
		// anything at all - collapses into Unknown. Cancellation is swallowed for the same
		// reason: a caller that cancelled is navigating away and has nothing to handle.
		try
		{
			if (!force)
			{
				if (!UpdateChecksEnabled)
					return UpdateCheckResult.Unknown;
				if (!IsIntervalElapsed())
					return UpdateCheckResult.Unknown;
			}

			ReleaseInfo? release;
			try
			{
				release = await FetchLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
			}
			finally
			{
				// Stamped on attempt, not on success. A device that is offline or rate-limited
				// would otherwise retry on every single launch, which is exactly the traffic
				// the gate exists to prevent.
				RecordCheckAttempt();
			}

			if (release is null)
				return UpdateCheckResult.Unknown;

			if (!force && string.Equals(release.TagName, SkippedTag, StringComparison.OrdinalIgnoreCase))
				return new UpdateCheckResult(UpdateAvailability.UpToDate, null);

			var availability = VersionComparer.Compare(release.TagName, currentVersion);

			// ReleaseInfo rides along only for UpdateAvailable - that is the documented
			// contract on UpdateCheckResult, and it keeps the UI from rendering release notes
			// for a build the user already has.
			return new UpdateCheckResult(
				availability,
				availability == UpdateAvailability.UpdateAvailable ? release : null);
		}
		catch
		{
			return UpdateCheckResult.Unknown;
		}
	}

	/// <summary>
	/// Fetches and parses the latest release. Returns null for any non-success status
	/// (404 = no releases published yet, 403 = rate limited) or unusable payload.
	/// </summary>
	private static async Task<ReleaseInfo?> FetchLatestReleaseAsync(CancellationToken cancellationToken)
	{
		// A linked source rather than relying on HttpClient.Timeout alone: the timeout must
		// also bound reading and parsing the response body, not just getting the headers.
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(RequestTimeout);

		using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
		using var response = await Http
			.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
			.ConfigureAwait(false);

		if (!response.IsSuccessStatusCode)
			return null;

		await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
		using var document = await JsonDocument
			.ParseAsync(stream, cancellationToken: timeout.Token)
			.ConfigureAwait(false);

		return ReadRelease(document.RootElement);
	}

	/// <summary>
	/// Reads the handful of fields the updater needs out of GitHub's release payload.
	/// </summary>
	/// <remarks>
	/// Hand-read via <see cref="JsonDocument"/> rather than deserialised into a DTO: the
	/// reflection-based <c>JsonSerializer</c> binding is exactly what the Android linker strips
	/// in a trimmed Release build, and the failure mode there is silently-null properties on
	/// device while every desktop test passes. Manual reads have no such trimming dependency.
	/// </remarks>
	private static ReleaseInfo? ReadRelease(JsonElement root)
	{
		if (root.ValueKind != JsonValueKind.Object)
			return null;

		if (!root.TryGetProperty("tag_name", out var tagElement) || tagElement.ValueKind != JsonValueKind.String)
			return null;

		string tag = tagElement.GetString() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(tag))
			return null;

		string notes = root.TryGetProperty("body", out var bodyElement) && bodyElement.ValueKind == JsonValueKind.String
			? bodyElement.GetString() ?? string.Empty
			: string.Empty;

		bool isPreRelease = root.TryGetProperty("prerelease", out var preElement)
			&& preElement.ValueKind == JsonValueKind.True;

		string? apkUrl = null;
		long apkSize = 0;

		if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
		{
			foreach (var asset in assets.EnumerateArray())
			{
				if (asset.ValueKind != JsonValueKind.Object)
					continue;
				if (!asset.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
					continue;

				string name = nameElement.GetString() ?? string.Empty;
				if (!name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
					continue;

				if (!asset.TryGetProperty("browser_download_url", out var urlElement)
					|| urlElement.ValueKind != JsonValueKind.String)
					continue;

				string url = urlElement.GetString() ?? string.Empty;
				if (string.IsNullOrWhiteSpace(url))
					continue;

				apkUrl = url;
				if (asset.TryGetProperty("size", out var sizeElement)
					&& sizeElement.ValueKind == JsonValueKind.Number
					&& sizeElement.TryGetInt64(out long size)
					&& size > 0)
				{
					apkSize = size;
				}
				break; // first .apk wins; releases here publish exactly one
			}
		}

		// A release with no .apk asset is still a real release worth reporting - the caller
		// checks ApkDownloadUrl for null and offers "view on GitHub" instead of a download.
		return new ReleaseInfo(
			TagName: tag.Trim(),
			DisplayVersion: VersionComparer.ToDisplayVersion(tag),
			Notes: notes,
			ApkDownloadUrl: apkUrl,
			ApkSizeBytes: apkSize,
			IsPreRelease: isPreRelease);
	}

	private static HttpClient CreateClient()
	{
		var client = new HttpClient { Timeout = RequestTimeout };
		client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgentValue);
		client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
		client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
		return client;
	}

	private bool IsIntervalElapsed()
	{
		var last = LastCheckUtc;
		if (last is null)
			return true;

		var now = DateTimeOffset.UtcNow;

		// A stamp in the future means the device clock moved backwards. Treat it as due
		// rather than locking out checks until the clock catches up.
		if (last.Value > now)
			return true;

		return now - last.Value >= MinimumCheckInterval;
	}

	private static void RecordCheckAttempt() =>
		SetPreference(PrefLastCheckUtc, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));

	private static string ReadAppVersion()
	{
		try
		{
			return AppInfo.Current.VersionString;
		}
		catch
		{
			// No MAUI app host (unit test, or a platform where AppInfo is unavailable). An
			// empty version makes VersionComparer return Unknown, which is the right answer.
			return string.Empty;
		}
	}

	// Preferences is platform storage and can throw on a broken/locked shared-prefs file.
	// Nothing about an update check is worth propagating that, so both accessors are total.
	private static T GetPreference<T>(string key, T fallback)
	{
		try
		{
			return Preferences.Default.Get(key, fallback);
		}
		catch
		{
			return fallback;
		}
	}

	private static void SetPreference<T>(string key, T value)
	{
		try
		{
			Preferences.Default.Set(key, value);
		}
		catch
		{
			// Losing a preference write only means the gate re-fires or a skip doesn't stick.
		}
	}
}
