using PKHeX.Core;

namespace PkhexMobile;

/// <summary>
/// The save-level trainer fields <see cref="TrainerInfoPage"/> can offer for editing.
/// </summary>
public enum TrainerField
{
    OT,
    Gender,
    Language,
    DisplayTID,
    DisplaySID,
    Money,
    PlayedHours,
    PlayedMinutes,
    PlayedSeconds,
}

/// <summary>
/// Answers, for one concrete loaded save, which trainer fields actually survive an export -
/// determined by round-tripping a throwaway <see cref="SaveFile.Clone"/> through
/// <see cref="SaveFile.Write"/> + <c>SaveUtil.GetSaveFile(byte[])</c>, not by branching on
/// <see cref="SaveFile.Generation"/>.
///
/// WHY A PROBE AND NOT A GENERATION SWITCH. Every one of these members is `virtual` on the abstract
/// SaveFile base with a plain auto-property or an outright empty body as the default
/// (SaveFile.cs:125-138: `Gender { get; set; }`, `Language { get => -1; set { } }`,
/// `OT { get; set; } = TrainerName.ProgramINT`, `Money { get; set; }`, `PlayedHours { get; set; }`).
/// A save class that doesn't override them therefore compiles, accepts writes, reads them back
/// happily, and serializes none of it. `SAV3RSBox` - the GameCube "Pokemon Box: Ruby &amp; Sapphire"
/// dump, which IS reachable here: SaveUtil.GetSaveFile on the raw .gci returns a SAV3RSBox through
/// exactly the byte[] path MainPage uses - overrides none of them. It reports OT="PKHeX",
/// Language=-1, TID/SID/Money/PlayTime all 0. Those are base-class sentinels, not that trainer's
/// data. It is `Generation == 3`, so any Gen-number gate would have shipped a fully enabled trainer
/// editor over values that don't exist and can never be written back.
///
/// This is the project's signature bug class (Gen1/2 IVs, Gen3 Nature/Ability, Gen4 Nature - see
/// PROGRESS.md "Form + Nature + Ability editing"), and it has three distinct shapes that a probe
/// has to survive all of:
///   - NO-OP   : the setter does nothing at all (SAV1.Gender is `get => 0; set { }`,
///               SAV1/SAV2.SID16 is `get => 0; set { }`).
///   - PHANTOM : the in-memory getter returns the new value, but Write() never serializes it, so a
///               reload silently reverts. SAV1/SAV2/SAV3's `Language` is a plain
///               `public override int Language { get; set; }` populated at construction from the
///               save-detection path and never backed by Data[]. This is the dangerous shape:
///               anything that checks by reading the field back off the same live object concludes
///               "works".
///   - MISSING : the whole class never overrides the member (SAV3RSBox, above).
/// Only a real Write() + reload distinguishes PHANTOM from working, which is why the probe pays for
/// an actual serialize round-trip instead of inspecting the live object or reflecting on overrides.
///
/// Verified against ground truth in verify/TrainerInfoEdit/Program.cs, which compiles THIS file in
/// and asserts every verdict it produces matches an independent per-field Write()+reload probe on
/// eight real saves spanning Gen1-Gen9 plus the SAV3RSBox .gci.
/// </summary>
public sealed class TrainerFieldSupport
{
    public static readonly TrainerField[] AllFields = Enum.GetValues<TrainerField>();

    /// <summary>Play-time hours ceiling to use when the probe can't establish one.</summary>
    public const int DefaultHoursCeiling = 999;

    private readonly Dictionary<TrainerField, bool> supported;

    private TrainerFieldSupport(Dictionary<TrainerField, bool> supported, int hoursCeiling, string? probeError)
    {
        this.supported = supported;
        HoursCeiling = hoursCeiling;
        ProbeError = probeError;
    }

    /// <summary>True only if the field was observed surviving a real export round-trip.</summary>
    public bool this[TrainerField field] => supported.TryGetValue(field, out var v) && v;

    /// <summary>True if at least one trainer field is editable - i.e. the page has anything to offer.</summary>
    public bool AnySupported => supported.Values.Any(static v => v);

    /// <summary>
    /// Largest play-time hour value this save's own setter will keep. SAV1 stores hours in a single
    /// byte and its setter has a `value >= 255` branch that pins it to 255, sets a "played maximum"
    /// flag and zeroes minutes/seconds (SAV1.cs:326-339) - so the ceiling is discovered from the
    /// save rather than assumed uniform.
    /// </summary>
    public int HoursCeiling { get; }

    /// <summary>Non-null if the probe could not complete; every field is reported unsupported in that case.</summary>
    public string? ProbeError { get; }

    /// <summary>
    /// A save with no editable trainer fields at all - the SAV3RSBox case. Distinct from "some
    /// fields disabled": here the values on screen would not be the trainer's at all, so the page
    /// shows an explanation instead of a form full of base-class sentinels.
    /// </summary>
    public static string NotStoredExplanation(SaveFile sav) =>
        $"This save type ({sav.GetType().Name}) does not store a trainer block - it is a storage-only " +
        "dump, so there is no OT name, ID, gender, language, money or play time in the file. PKHeX.Core " +
        "still exposes those properties (they are inherited defaults, not this save's data), and writing " +
        "to them would be silently discarded on export, so they are not offered here.";

    /// <summary>
    /// Runs the probe. Never throws: any failure reports every field unsupported, which is the safe
    /// direction - a wrongly-disabled control shows a value the user can't change, a wrongly-enabled
    /// one accepts an edit that vanishes.
    /// </summary>
    public static TrainerFieldSupport Probe(SaveFile sav)
    {
        // Default-deny. Every path below can only turn entries on.
        var supported = AllFields.ToDictionary(static f => f, static _ => false);
        var hoursCeiling = DefaultHoursCeiling;
        string? error = null;

        try
        {
            // Clone(), not the live save: the probe writes sentinel junk into every trainer field and
            // must never let that reach the SaveFile the rest of the app is holding. Clone routes
            // through CloneInternal -> GetFinalData() -> Data.ToArray(), a genuine copy (confirmed:
            // mutating the clone's OT leaves the original's untouched). GetFinalData does call
            // SetChecksums() on the original as a side effect, which is exactly what a normal
            // Write() does anyway and touches no user-visible field.
            var clone = sav.Clone();

            // One batched round-trip rather than nine: each sentinel targets a different field, and
            // verify/TrainerInfoEdit asserts the batched verdicts match nine independent per-field
            // probes, so cross-talk between them would be caught rather than assumed absent.
            var sentinels = new Dictionary<TrainerField, string?>();
            foreach (var field in AllFields)
                sentinels[field] = TryApplySentinel(clone, field);

            var written = clone.Write().ToArray();
            var reloaded = SaveUtil.GetSaveFile(written);

            if (reloaded is null)
                error = "the save could not be re-read after a test export";
            else if (reloaded.GetType() != sav.GetType())
                error = $"a test export re-read as {reloaded.GetType().Name}, not {sav.GetType().Name}";
            else
            {
                foreach (var field in AllFields)
                {
                    var sentinel = sentinels[field];
                    if (sentinel is not null)
                        supported[field] = Read(reloaded, field) == sentinel;
                }
            }

            // Ceiling probe runs on the already-written clone so it can't disturb the round-trip
            // above. In-memory only: the clamp that matters lives in the setter, and asking for a
            // deliberately absurd value costs nothing here.
            hoursCeiling = ProbeHoursCeiling(clone);
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            foreach (var field in AllFields)
                supported[field] = false;
        }

        return new TrainerFieldSupport(supported, hoursCeiling, error);
    }

    private static int ProbeHoursCeiling(SaveFile clone)
    {
        try
        {
            clone.PlayedHours = DefaultHoursCeiling;
            var readBack = clone.PlayedHours;
            return readBack is > 0 and <= DefaultHoursCeiling ? readBack : DefaultHoursCeiling;
        }
        catch
        {
            return DefaultHoursCeiling;
        }
    }

    /// <summary>
    /// Writes a value guaranteed to differ from the current one, and returns it in the same string
    /// form <see cref="Read"/> produces. Returns null if the setter throws - SAV2's OT setter passes
    /// a hardcoded maxLength of 8 into a 7-byte destination and throws IndexOutOfRangeException, so
    /// a setter throwing is a real possibility, not defensive padding.
    /// </summary>
    private static string? TryApplySentinel(SaveFile sav, TrainerField field)
    {
        try
        {
            switch (field)
            {
                case TrainerField.OT:
                {
                    // Kept short and ASCII on purpose: long enough to differ, never long enough to
                    // hit the Gen2 over-length throw, and inside every generation's character table
                    // so a lossy re-encode can't be mistaken for "doesn't persist".
                    var value = Fit("ZTESTZ", sav.MaxStringLengthTrainer);
                    if (value == sav.OT)
                        value = Fit("QPROBEQ", sav.MaxStringLengthTrainer);
                    sav.OT = value;
                    return value;
                }
                case TrainerField.Gender:
                {
                    var value = sav.Gender == 0 ? (byte)1 : (byte)0;
                    sav.Gender = value;
                    return value.ToString();
                }
                case TrainerField.Language:
                {
                    // English/French only - both exist in every generation's language table, and
                    // neither is Japanese, whose selection also drives Gen1-3 string tables and box
                    // layout at construction time.
                    var value = sav.Language == (int)LanguageID.French ? (int)LanguageID.English : (int)LanguageID.French;
                    sav.Language = value;
                    return value.ToString();
                }
                case TrainerField.DisplayTID:
                {
                    var value = MaxDisplayTID(sav) >= 999999 ? 123456u : 12345u;
                    if (sav.DisplayTID == value)
                        value = MaxDisplayTID(sav) >= 999999 ? 654321u : 54321u;
                    sav.DisplayTID = value;
                    return value.ToString();
                }
                case TrainerField.DisplaySID:
                {
                    var value = 1234u;
                    if (sav.DisplaySID == value)
                        value = 2345u;
                    sav.DisplaySID = value;
                    return value.ToString();
                }
                case TrainerField.Money:
                {
                    // Deliberately under MaxMoney: several generations' Money setters clamp
                    // internally, so an over-max sentinel would come back reduced and read as a
                    // failed round-trip when the behavior is correct.
                    var max = (uint)sav.MaxMoney;
                    var value = Math.Min(123456u, max);
                    if (sav.Money == value)
                        value = Math.Min(654321u, max);
                    if (sav.Money == value)
                        value = max / 2;
                    sav.Money = value;
                    return value.ToString();
                }
                case TrainerField.PlayedHours:
                {
                    // Small on purpose: SAV1's setter treats >= 255 as "played maximum" and zeroes
                    // minutes and seconds, which would sink all three fields' probes at once.
                    var value = sav.PlayedHours == 42 ? 43 : 42;
                    sav.PlayedHours = value;
                    return value.ToString();
                }
                case TrainerField.PlayedMinutes:
                {
                    var value = sav.PlayedMinutes == 33 ? 34 : 33;
                    sav.PlayedMinutes = value;
                    return value.ToString();
                }
                case TrainerField.PlayedSeconds:
                {
                    var value = sav.PlayedSeconds == 21 ? 22 : 21;
                    sav.PlayedSeconds = value;
                    return value.ToString();
                }
                default:
                    return null;
            }
        }
        catch
        {
            return null;
        }
    }

    private static string Read(SaveFile sav, TrainerField field) => field switch
    {
        TrainerField.OT => sav.OT,
        TrainerField.Gender => sav.Gender.ToString(),
        TrainerField.Language => sav.Language.ToString(),
        TrainerField.DisplayTID => sav.DisplayTID.ToString(),
        TrainerField.DisplaySID => sav.DisplaySID.ToString(),
        TrainerField.Money => sav.Money.ToString(),
        TrainerField.PlayedHours => sav.PlayedHours.ToString(),
        TrainerField.PlayedMinutes => sav.PlayedMinutes.ToString(),
        TrainerField.PlayedSeconds => sav.PlayedSeconds.ToString(),
        _ => string.Empty,
    };

    /// <summary>
    /// Upper bound for the displayed Trainer ID. Driven by the save's own
    /// <see cref="SaveFile.TrainerIDDisplayFormat"/> rather than a generation number: pre-Gen7 the
    /// displayed TID is the raw 16-bit TID16, from Gen7 on it is the 6-digit low half of ID32.
    /// </summary>
    public static uint MaxDisplayTID(SaveFile sav) =>
        sav.TrainerIDDisplayFormat == TrainerIDFormat.SixDigit ? 999999u : ushort.MaxValue;

    /// <summary>
    /// Upper bound for the displayed Secret ID. NOT 999999 in the six-digit case: ID32 is
    /// reconstructed as (sid7 * 1_000_000) + tid7 and has to fit in a uint, so sid7 tops out at
    /// 4294 (ITrainerID32.IsValidTrainerID7). SetTrainerID7's own comment notes a bad pair
    /// "overflow[s] back to sid:0" rather than throwing, so this bound is load-bearing, not cosmetic.
    /// </summary>
    public static uint MaxDisplaySID(SaveFile sav) =>
        sav.TrainerIDDisplayFormat == TrainerIDFormat.SixDigit ? 4294u : ushort.MaxValue;

    private static string Fit(string candidate, int maxLength) =>
        candidate.Length > maxLength ? candidate[..maxLength] : candidate;
}
