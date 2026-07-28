using PkhexMobile.Update;

namespace PkhexMobile;

/// <summary>
/// Update preferences, a manual check, and the download/install hand-off.
/// </summary>
/// <remarks>
/// Nothing here installs anything on its own. The user has to press Download, and
/// then Android's own package installer asks again before anything is replaced.
/// That double confirmation is deliberate.
/// </remarks>
public partial class UpdateSettingsPage : ContentPage
{
	private readonly UpdateService updateService = new();
	private readonly UpdateDownloader downloader = new();
	private readonly IApkInstaller installer = UpdateInstallerFactory.Create();

	/// <summary>Guards the Switch handler while we populate it programmatically.</summary>
	private bool isLoading;

	private ReleaseInfo? pendingRelease;
	private CancellationTokenSource? downloadCts;
	private string? downloadedApkPath;

	public UpdateSettingsPage()
	{
		InitializeComponent();
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		LoadPreferences();
	}

	private void LoadPreferences()
	{
		isLoading = true;
		try
		{
			CurrentVersionLabel.Text = updateService.CurrentVersion is { Length: > 0 } v
				? v
				: "unknown";
			AutoCheckSwitch.IsToggled = updateService.UpdateChecksEnabled;

			var last = updateService.LastCheckUtc;
			LastCheckedLabel.Text = last is null
				? "Not checked yet."
				: $"Last checked {last.Value.ToLocalTime():yyyy-MM-dd HH:mm}.";
		}
		finally
		{
			isLoading = false;
		}
	}

	private void OnAutoCheckToggled(object? sender, ToggledEventArgs e)
	{
		// Populating the switch in LoadPreferences must not be mistaken for a user edit.
		if (isLoading)
			return;

		updateService.UpdateChecksEnabled = e.Value;
	}

	private async void OnCheckNowClicked(object? sender, EventArgs e)
	{
		SetChecking(true);
		HideStatus();

		// A manual check deliberately ignores the 24h gate, the enabled preference,
		// and any previously skipped version.
		var result = await updateService.CheckNowAsync().ConfigureAwait(true);

		SetChecking(false);
		LoadPreferences();
		PresentResult(result);
	}

	/// <summary>
	/// Renders a check result. Called from the manual check and, once wired, from the
	/// silent automatic check on startup.
	/// </summary>
	internal void PresentResult(UpdateCheckResult result)
	{
		switch (result.Availability)
		{
			case UpdateAvailability.UpdateAvailable when result.Release is not null:
				ShowUpdate(result.Release);
				break;

			case UpdateAvailability.UpToDate:
				ShowStatus("You are on the latest version.");
				break;

			case UpdateAvailability.Ahead:
				ShowStatus("This build is newer than the latest published release.");
				break;

			default:
				// Unknown: offline, rate-limited, malformed, or no releases yet. On a
				// manual check the user pressed a button and deserves an acknowledgement,
				// so this is the ONE place Unknown is surfaced at all. An automatic check
				// must stay completely silent - see PresentAutomaticResult.
				ShowStatus("Could not check for updates. Check your connection and try again.");
				break;
		}
	}

	/// <summary>
	/// Renders an automatic (startup) check result. Silent unless there is genuinely
	/// an update - an offline user must see nothing at all.
	/// </summary>
	internal void PresentAutomaticResult(UpdateCheckResult result)
	{
		if (result.Availability == UpdateAvailability.UpdateAvailable && result.Release is not null)
			ShowUpdate(result.Release);
	}

	private void ShowUpdate(ReleaseInfo release)
	{
		pendingRelease = release;

		NewVersionLabel.Text = $"Version {release.DisplayVersion}";
		ReleaseNotesLabel.Text = string.IsNullOrWhiteSpace(release.Notes)
			? "No release notes were published."
			: release.Notes.Trim();

		// A release with no .apk asset cannot be installed from here.
		if (release.ApkDownloadUrl is null)
		{
			DownloadSizeLabel.Text = "This release has no APK attached.";
			UpdateBtn.IsEnabled = false;
		}
		else
		{
			DownloadSizeLabel.Text = release.ApkSizeBytes > 0
				? $"Download size: {FormatBytes(release.ApkSizeBytes)}"
				: "Download size: unknown";
			UpdateBtn.IsEnabled = true;
		}

		// Warn before replacing the app if edits are in flight and unexported.
		UnsavedWarning.IsVisible = NavigationState.HasUnsavedChanges;

		UpdateCard.IsVisible = true;
	}

	private async void OnUpdateClicked(object? sender, EventArgs e)
	{
		if (pendingRelease?.ApkDownloadUrl is not { } url)
			return;

		// Last chance to back out while unsaved edits exist.
		if (NavigationState.HasUnsavedChanges)
		{
			var proceed = await DisplayAlert(
				"Unsaved changes",
				"You have unsaved changes that have not been exported. Installing the update replaces the app. Continue anyway?",
				"Continue",
				"Cancel").ConfigureAwait(true);

			if (!proceed)
				return;
		}

		downloadCts?.Dispose();
		downloadCts = new CancellationTokenSource();

		SetDownloading(true);

		var progress = new Progress<DownloadProgress>(p =>
		{
			if (p.Fraction is { } f)
			{
				DownloadProgress.Progress = f;
				DownloadStatusLabel.Text = $"{FormatBytes(p.BytesReceived)} of {FormatBytes(p.TotalBytes)}";
			}
			else
			{
				DownloadStatusLabel.Text = FormatBytes(p.BytesReceived);
			}
		});

		var result = await downloader
			.DownloadAsync(url, pendingRelease.DisplayVersion, pendingRelease.ApkSizeBytes, progress, downloadCts.Token)
			.ConfigureAwait(true);

		SetDownloading(false);

		if (result.Outcome == DownloadOutcome.Cancelled)
		{
			DownloadStatusLabel.IsVisible = true;
			DownloadStatusLabel.Text = "Download cancelled.";
			return;
		}

		if (!result.IsSuccess || result.FilePath is null)
		{
			DownloadStatusLabel.IsVisible = true;
			DownloadStatusLabel.Text = result.ErrorMessage ?? "Download failed.";
			return;
		}

		downloadedApkPath = result.FilePath;
		await HandOffToInstallerAsync().ConfigureAwait(true);
	}

	private async Task HandOffToInstallerAsync()
	{
		if (downloadedApkPath is null)
			return;

		if (!installer.IsSupported)
		{
			DownloadStatusLabel.IsVisible = true;
			DownloadStatusLabel.Text = "Installing from inside the app is not supported on this platform.";
			return;
		}

		// Android blocks the install outright until the user allows this app to
		// request package installs. Explain it; never dead-end.
		if (!installer.CanRequestInstall())
		{
			PermissionCard.IsVisible = true;
			await DisplayAlert(
				"Permission needed",
				"Android needs your permission to install apps from this app. The update is downloaded and ready - open settings, allow it, then press Download update again.",
				"OK").ConfigureAwait(true);
			return;
		}

		PermissionCard.IsVisible = false;

		if (!installer.Install(downloadedApkPath))
		{
			DownloadStatusLabel.IsVisible = true;
			DownloadStatusLabel.Text = "Could not open the installer. The downloaded file may have been removed.";
		}
	}

	private void OnOpenPermissionSettingsClicked(object? sender, EventArgs e)
		=> installer.OpenInstallPermissionSettings();

	private void OnCancelDownloadClicked(object? sender, EventArgs e) => downloadCts?.Cancel();

	private void OnLaterClicked(object? sender, EventArgs e)
	{
		// Deliberately persists nothing: "Later" means ask me again next time.
		UpdateCard.IsVisible = false;
		pendingRelease = null;
	}

	private void OnSkipClicked(object? sender, EventArgs e)
	{
		// SkipVersion compares the raw tag_name, not the display version.
		updateService.SkipVersion(pendingRelease?.TagName);
		UpdateCard.IsVisible = false;
		pendingRelease = null;
		ShowStatus("This version will be skipped. A newer one will still be offered.");
	}

	private void SetChecking(bool checking)
	{
		CheckSpinner.IsVisible = checking;
		CheckSpinner.IsRunning = checking;
		CheckNowBtn.IsEnabled = !checking;
	}

	private void SetDownloading(bool downloading)
	{
		DownloadProgress.IsVisible = downloading;
		DownloadProgress.Progress = 0;
		DownloadStatusLabel.IsVisible = downloading;
		CancelDownloadBtn.IsVisible = downloading;
		UpdateBtn.IsEnabled = !downloading;
		LaterBtn.IsEnabled = !downloading;
		SkipBtn.IsEnabled = !downloading;
		CheckNowBtn.IsEnabled = !downloading;
	}

	private void ShowStatus(string message)
	{
		StatusLabel.Text = message;
		StatusLabel.IsVisible = true;
	}

	private void HideStatus()
	{
		StatusLabel.IsVisible = false;
		UpdateCard.IsVisible = false;
		PermissionCard.IsVisible = false;
	}

	private static string FormatBytes(long bytes)
	{
		if (bytes <= 0)
			return "unknown";
		if (bytes < 1024)
			return $"{bytes} B";
		if (bytes < 1024 * 1024)
			return $"{bytes / 1024.0:0.#} KB";
		return $"{bytes / (1024.0 * 1024.0):0.#} MB";
	}
}
