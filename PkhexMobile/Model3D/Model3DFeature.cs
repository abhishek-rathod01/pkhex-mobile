namespace PkhexMobile.Model3D;

/// <summary>
/// The single on/off gate for the 3D model viewer. Everything else in
/// <c>PkhexMobile.Model3D</c> must check <see cref="IsEnabled"/> before doing any work -
/// before starting the loopback server, before touching the network, before showing a
/// <c>WebView</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>It ships OFF, deliberately.</b> Nothing in this namespace has ever rendered a model on a
/// real device or an emulator. The port was written and reviewed at source level only: there is
/// no .NET SDK in the environment it was authored in, so it has not even been compiled, let
/// alone run. Three separate things are unverified and any one of them alone is enough to give
/// the user a blank screen:
/// </para>
/// <list type="number">
///   <item><description>
///   Whether <see cref="System.Net.HttpListener"/> can even be constructed and started under
///   .NET for Android. On some runtime configurations it throws
///   <see cref="PlatformNotSupportedException"/> at construction.
///   <see cref="LoopbackModelServer"/> contains that failure rather than crashing, but
///   containing it is not the same as it working.
///   </description></item>
///   <item><description>
///   Whether Android's WebView will load a cleartext <c>http://127.0.0.1:port/</c> URL at all.
///   Apps targeting API 28+ get cleartext traffic denied by default unless the manifest opts in
///   (a network security config with a loopback exception). This app's manifest currently has
///   no such opt-in.
///   </description></item>
///   <item><description>
///   Whether the models render correctly once loaded. The two prior WebView prototypes
///   (documented in WAKEUP.md) both failed to render for origin-related reasons; a real origin
///   is the whole point of the loopback approach, but "should fix it" is a hypothesis, not a
///   result.
///   </description></item>
/// </list>
/// <para>
/// On top of that, <see cref="Model3DCache"/> has no upstream model URL yet - see the
/// clearly-marked constants there. With none supplied, a fetch can never succeed.
/// </para>
/// <para>
/// This project has a documented precedent (the Shell <c>InvalidCastException</c>) of a bug that
/// passed every harness and only appeared on a device. Flipping this to <c>true</c> is therefore
/// an on-device decision, not a code-review one: build it, run it on real hardware, watch
/// <c>adb logcat -d | grep chromium</c>, and only then change this value. See
/// <c>docs/3D-VIEWER-STATUS.md</c> for the full state of the feature and what to try first.
/// </para>
/// </remarks>
public static class Model3DFeature
{
	/// <summary>
	/// Whether the 3D viewer is available. Defaults to <c>false</c>; see the remarks on
	/// <see cref="Model3DFeature"/> for why, and for what must be verified before flipping it.
	/// </summary>
	/// <remarks>
	/// Intentionally a read-only property and not a <c>const</c>. A <c>const false</c> would let
	/// the compiler constant-fold every <c>if (!Model3DFeature.IsEnabled)</c> guard and report
	/// the guarded code as unreachable (CS0162) - which would break this project's zero-warning
	/// build bar the moment the gate was added.
	/// </remarks>
	public static bool IsEnabled { get; } = false;
}
