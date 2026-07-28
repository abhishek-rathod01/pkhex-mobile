using PkhexMobile.Update;

namespace PkhexMobile;

/// <summary>
/// Resolves the platform's <see cref="IApkInstaller"/>.
/// </summary>
/// <remarks>
/// A plain factory rather than DI registration: the update UI is the only consumer,
/// and MauiProgram currently registers no services at all. Adding a container purely
/// for one interface would be more moving parts than the problem needs.
/// </remarks>
internal static class UpdateInstallerFactory
{
	public static IApkInstaller Create()
	{
#if ANDROID
		return new ApkInstaller();
#else
		// Everything else gets the no-op, so the update UI still loads and simply
		// reports that installing from inside the app is unsupported.
		return new UnsupportedApkInstaller();
#endif
	}
}
