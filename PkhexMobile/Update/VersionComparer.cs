using System.Globalization;

namespace PkhexMobile.Update;

/// <summary>
/// Compares a GitHub release tag against the running build's version string.
/// </summary>
/// <remarks>
/// Deliberately a pure static function of two strings with no MAUI dependency: the update
/// check is the one place where a wrong answer is user-visible and unrecoverable (nagging a
/// user who is already up to date, or silently hiding a real update), and this class is the
/// only part of it that can be exercised without an app host.
/// <para>
/// The comparison is component-wise and numeric. A plain string compare gets "1.10.0" vs
/// "1.9.0" backwards, which is exactly the case that shows up the first time a project ships
/// ten minor releases.
/// </para>
/// <para>
/// NOTHING here throws. Every unparseable input degrades to
/// <see cref="UpdateAvailability.Unknown"/>, which the caller renders as "show the user
/// nothing" - a malformed tag must never be able to crash app startup.
/// </para>
/// </remarks>
public static class VersionComparer
{
	// Guards against pathological input (a hostile or corrupt tag). Anything past these bounds
	// is treated as malformed rather than parsed - there is no legitimate version string that
	// needs 5 components or 256 characters.
	private const int MaxComponents = 4;
	private const int MaxInputLength = 256;

	/// <summary>
	/// Compares the latest published release tag against the running build's version.
	/// </summary>
	/// <param name="latestTag">
	/// The release tag as GitHub reports it, e.g. "v1.2.3", "1.2.3", "v1.3.0-beta.1".
	/// A leading "v"/"V" is optional.
	/// </param>
	/// <param name="runningVersion">
	/// The running build's version, typically <c>AppInfo.Current.VersionString</c>. May carry
	/// SemVer build metadata ("1.2.3+42"), which is ignored.
	/// </param>
	/// <returns>
	/// How <paramref name="latestTag"/> relates to <paramref name="runningVersion"/>, or
	/// <see cref="UpdateAvailability.Unknown"/> if either side cannot be parsed.
	/// </returns>
	public static UpdateAvailability Compare(string? latestTag, string? runningVersion)
	{
		if (!TryParse(latestTag, out int[] latestCore, out string latestPre))
			return UpdateAvailability.Unknown;
		if (!TryParse(runningVersion, out int[] runningCore, out string runningPre))
			return UpdateAvailability.Unknown;

		// SemVer precedence: the core version decides first, and only when the cores are
		// identical does the pre-release suffix break the tie. That ordering is what makes
		// 1.3.0-beta correctly newer than 1.2.9 while 1.2.3-beta is older than 1.2.3.
		int cmp = CompareCore(latestCore, runningCore);
		if (cmp == 0)
			cmp = ComparePreRelease(latestPre, runningPre);

		if (cmp > 0)
			return UpdateAvailability.UpdateAvailable;
		if (cmp < 0)
			return UpdateAvailability.Ahead;
		return UpdateAvailability.UpToDate;
	}

	/// <summary>
	/// Strips the tag decoration a user should never see - the leading "v" and any SemVer
	/// build metadata - leaving something fit for a label. Returns the trimmed input unchanged
	/// if it doesn't look like a version at all.
	/// </summary>
	public static string ToDisplayVersion(string? tag)
	{
		if (string.IsNullOrWhiteSpace(tag))
			return string.Empty;

		string s = tag.Trim();
		if (s.Length > MaxInputLength)
			return s;
		if (s[0] is 'v' or 'V')
			s = s[1..];

		int plus = s.IndexOf('+');
		if (plus >= 0)
			s = s[..plus];

		return s.Length == 0 ? tag.Trim() : s;
	}

	/// <summary>
	/// Splits a version string into numeric core components plus a raw pre-release suffix.
	/// Returns false for anything that isn't cleanly numeric-dotted.
	/// </summary>
	private static bool TryParse(string? raw, out int[] core, out string preRelease)
	{
		core = [];
		preRelease = string.Empty;

		if (string.IsNullOrWhiteSpace(raw))
			return false;

		string s = raw.Trim();
		if (s.Length > MaxInputLength)
			return false;

		if (s[0] is 'v' or 'V')
			s = s[1..];

		// Build metadata is ignored entirely for precedence (SemVer 2.0.0 #10), and MAUI's
		// AppInfo.VersionString can carry it as "1.2.3+42". It sits after the pre-release
		// suffix, so it has to come off first.
		int plus = s.IndexOf('+');
		if (plus >= 0)
			s = s[..plus];

		int dash = s.IndexOf('-');
		if (dash >= 0)
		{
			preRelease = s[(dash + 1)..];
			s = s[..dash];
			if (preRelease.Length == 0)
				return false; // a bare trailing "-" is malformed, not "no suffix"
		}

		if (s.Length == 0)
			return false;

		string[] parts = s.Split('.');
		if (parts.Length > MaxComponents)
			return false;

		int[] parsed = new int[parts.Length];
		for (int i = 0; i < parts.Length; i++)
		{
			// NumberStyles.None rejects signs, whitespace and separators, so "1. 2.3" and
			// "1.-2.3" are malformed rather than quietly accepted. TryParse also turns an
			// overflowing component into a clean false instead of an exception.
			if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out parsed[i]))
				return false;
		}

		core = parsed;
		return true;
	}

	/// <summary>
	/// Component-wise numeric compare, zero-padding the shorter side so "1.2" and "1.2.0"
	/// come out equal.
	/// </summary>
	private static int CompareCore(int[] left, int[] right)
	{
		int n = Math.Max(left.Length, right.Length);
		for (int i = 0; i < n; i++)
		{
			int a = i < left.Length ? left[i] : 0;
			int b = i < right.Length ? right[i] : 0;
			if (a != b)
				return a < b ? -1 : 1;
		}
		return 0;
	}

	/// <summary>
	/// SemVer 2.0.0 #11 pre-release precedence. Only meaningful when the core versions match.
	/// </summary>
	private static int ComparePreRelease(string left, string right)
	{
		// "A pre-release version has lower precedence than a normal version" - so an absent
		// suffix outranks any present one.
		if (left.Length == 0 && right.Length == 0)
			return 0;
		if (left.Length == 0)
			return 1;
		if (right.Length == 0)
			return -1;

		string[] a = left.Split('.');
		string[] b = right.Split('.');
		int n = Math.Max(a.Length, b.Length);
		for (int i = 0; i < n; i++)
		{
			// A larger set of fields wins when everything preceding is equal:
			// 1.0.0-beta < 1.0.0-beta.1.
			if (i >= a.Length)
				return -1;
			if (i >= b.Length)
				return 1;

			// An identifier too large for int falls through to the ASCII path rather than
			// throwing; it is not a case any real tag hits, and being slightly wrong there
			// beats crashing.
			bool aNum = int.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out int aVal);
			bool bNum = int.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out int bVal);

			if (aNum && bNum)
			{
				if (aVal != bVal)
					return aVal < bVal ? -1 : 1;
				continue;
			}

			// Numeric identifiers always have lower precedence than alphanumeric ones.
			if (aNum)
				return -1;
			if (bNum)
				return 1;

			int cmp = string.CompareOrdinal(a[i], b[i]);
			if (cmp != 0)
				return cmp < 0 ? -1 : 1;
		}
		return 0;
	}
}
