using Android.Content;
using Android.Provider;

namespace PkhexMobile.Update;

/// <summary>
/// Android hand-off to the system package installer.
/// </summary>
/// <remarks>
/// <para>
/// This never installs anything itself. It only starts the OS installer UI with a
/// content:// URI; the user's confirmation on that screen is the install. That is
/// the correct and only sideload flow for a non-system app.
/// </para>
/// <para>
/// Lives under Platforms/Android so it is compiled for Android only - no #if guards needed.
/// </para>
/// </remarks>
public sealed class ApkInstaller : IApkInstaller
{
	/// <summary>
	/// Must match android:authorities on the FileProvider in AndroidManifest.xml
	/// exactly. A mismatch throws at GetUriForFile with a message that names neither
	/// side, so it is worth keeping these two literals visually next to each other.
	/// </summary>
	private const string FileProviderAuthority = "com.companyname.pkhexmobile.fileprovider";

	/// <summary>MIME type the package installer registers for; anything else opens the wrong app.</summary>
	private const string ApkMimeType = "application/vnd.android.package-archive";

	/// <inheritdoc/>
	public bool IsSupported => true;

	/// <inheritdoc/>
	/// <remarks>
	/// REQUEST_INSTALL_PACKAGES became a per-app user grant in API 26 (Oreo). Below
	/// that it is a manifest-only permission, so the answer is always yes.
	/// <c>OperatingSystem.IsAndroidVersionAtLeast(26)</c> is used rather than a raw
	/// <c>Build.VERSION.SdkInt</c> comparison because it is the form the platform
	/// compatibility analyzer recognises - a raw comparison compiles with a CA1416
	/// warning, and this project's bar is zero warnings.
	/// </remarks>
	public bool CanRequestInstall()
	{
		try
		{
			if (!OperatingSystem.IsAndroidVersionAtLeast(26))
				return true;

			var manager = Android.App.Application.Context.PackageManager;

			// A null PackageManager should not happen in a live app; treat it as
			// "cannot", so the caller offers the settings screen instead of failing later.
			return manager?.CanRequestPackageInstalls() ?? false;
		}
		catch (Exception ex)
		{
			// Documented to never throw - some OEM builds have surprised callers here.
			System.Diagnostics.Debug.WriteLine($"[ApkInstaller] CanRequestInstall failed: {ex.Message}");
			return false;
		}
	}

	/// <inheritdoc/>
	public void OpenInstallPermissionSettings()
	{
		var context = Android.App.Application.Context;

		if (OperatingSystem.IsAndroidVersionAtLeast(26))
		{
			try
			{
				// The package: URI takes the user straight to OUR entry in the
				// "install unknown apps" list rather than the full app list.
				var uri = Android.Net.Uri.Parse("package:" + context.PackageName);
				using var intent = new Intent(Settings.ActionManageUnknownAppSources, uri);

				// Started from a Context that is not an Activity, so NewTask is mandatory.
				intent.AddFlags(ActivityFlags.NewTask);
				context.StartActivity(intent);
				return;
			}
			catch (Exception ex)
			{
				// Some OEM ROMs ship without this screen. Fall through rather than crash.
				System.Diagnostics.Debug.WriteLine($"[ApkInstaller] Unknown-sources screen unavailable: {ex.Message}");
			}
		}

		try
		{
			using var fallback = new Intent(Settings.ActionManageApplicationsSettings);
			fallback.AddFlags(ActivityFlags.NewTask);
			context.StartActivity(fallback);
		}
		catch (Exception ex)
		{
			// Nothing left to try. The caller's instructions on screen are the last resort.
			System.Diagnostics.Debug.WriteLine($"[ApkInstaller] Settings fallback failed: {ex.Message}");
		}
	}

	/// <inheritdoc/>
	public bool Install(string apkPath)
	{
		if (string.IsNullOrWhiteSpace(apkPath))
			return false;

		try
		{
			// Checked here rather than trusting the caller: a missing file produces a
			// far more confusing failure once it reaches the installer.
			if (!System.IO.File.Exists(apkPath))
				return false;

			var context = Android.App.Application.Context;
			var file = new Java.IO.File(apkPath);

			// A file:// URI throws FileUriExposedException from API 24 onward. The
			// FileProvider content:// URI is the only supported way to hand a file to
			// another app, and it is why file_paths.xml exposes the cache directory.
			var contentUri = AndroidX.Core.Content.FileProvider.GetUriForFile(context, FileProviderAuthority, file);

			using var intent = new Intent(Intent.ActionView);
			intent.SetDataAndType(contentUri, ApkMimeType);

			// NewTask: we hold a non-Activity Context.
			// GrantReadUriPermission: the installer is a different process and cannot
			// read our cache without a transient grant on this URI.
			intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);

			context.StartActivity(intent);
			return true;
		}
		catch (Exception ex)
		{
			// No installer activity, revoked permission, provider misconfiguration -
			// all become a plain false so the caller can explain rather than crash.
			System.Diagnostics.Debug.WriteLine($"[ApkInstaller] Install hand-off failed: {ex.Message}");
			return false;
		}
	}
}
