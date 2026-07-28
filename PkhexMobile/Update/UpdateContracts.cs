namespace PkhexMobile.Update;

/// <summary>
/// How the latest published release compares to the running build.
/// </summary>
public enum UpdateAvailability
{
	/// <summary>A newer release exists.</summary>
	UpdateAvailable,

	/// <summary>The running build matches the latest release.</summary>
	UpToDate,

	/// <summary>The running build is newer than the latest release (a local/test build).</summary>
	Ahead,

	/// <summary>
	/// No usable answer. Offline, rate-limited, malformed payload, missing tag, no
	/// releases yet - all collapse to this. The caller shows the user NOTHING.
	/// </summary>
	Unknown,
}

/// <summary>
/// One published GitHub release, reduced to only what the updater needs.
/// </summary>
public sealed record ReleaseInfo(
	string TagName,
	string DisplayVersion,
	string Notes,
	string? ApkDownloadUrl,
	long ApkSizeBytes,
	bool IsPreRelease);

/// <summary>
/// Result of an update check. <see cref="Release"/> is non-null only when
/// <see cref="Availability"/> is <see cref="UpdateAvailability.UpdateAvailable"/>.
/// </summary>
public sealed record UpdateCheckResult(
	UpdateAvailability Availability,
	ReleaseInfo? Release)
{
	public static UpdateCheckResult Unknown { get; } = new(UpdateAvailability.Unknown, null);
}

/// <summary>
/// Progress of an in-flight APK download. <see cref="TotalBytes"/> is 0 when the
/// server did not report a length.
/// </summary>
public readonly record struct DownloadProgress(long BytesReceived, long TotalBytes)
{
	public double? Fraction => TotalBytes > 0 ? (double)BytesReceived / TotalBytes : null;
}

/// <summary>
/// Platform hand-off to the system package installer.
/// </summary>
/// <remarks>
/// Android-only in practice. The default implementation is a no-op so the app
/// still builds and runs on any other target.
/// </remarks>
public interface IApkInstaller
{
	/// <summary>True when this platform can hand an APK to a package installer.</summary>
	bool IsSupported { get; }

	/// <summary>
	/// True when the user has granted this app permission to request package
	/// installs. False means <see cref="Install"/> will be refused by the OS.
	/// </summary>
	bool CanRequestInstall();

	/// <summary>Opens the OS screen where the user grants install permission.</summary>
	void OpenInstallPermissionSettings();

	/// <summary>
	/// Hands <paramref name="apkPath"/> to the system package installer via a
	/// FileProvider content:// URI. Returns false if the hand-off could not be
	/// started. NEVER installs silently - the OS always shows its own prompt.
	/// </summary>
	bool Install(string apkPath);
}

/// <summary>
/// No-op installer for platforms without a package-installer hand-off.
/// </summary>
public sealed class UnsupportedApkInstaller : IApkInstaller
{
	public bool IsSupported => false;
	public bool CanRequestInstall() => false;
	public void OpenInstallPermissionSettings() { }
	public bool Install(string apkPath) => false;
}
