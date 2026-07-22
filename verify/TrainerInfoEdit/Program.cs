using PKHeX.Core;
using PkhexMobile;

// Determines, EMPIRICALLY AND PER GENERATION, which of SaveFile's generic trainer-level fields
// actually round-trip through Write() + SaveUtil.GetSaveFile(byte[]) - i.e. which ones a
// TrainerInfoPage may safely expose as editable, and which are silent no-ops that must be shown
// disabled instead.
//
// Why this harness exists (the recurring bug class in this project, hit three times already:
// Gen1/2 IVs, Gen3 Nature/Ability, Gen4 Nature): several of these members are `virtual` on the
// abstract SaveFile base with per-generation overrides. An override exists to satisfy the base
// contract, but a given generation may not actually store that field - so the setter compiles,
// runs, and does nothing. See PROGRESS.md "Form + Nature + Ability editing".
//
// This harness distinguishes THREE failure shapes, not two, because they need different UI
// treatments and only one of them is visible from an in-memory getter:
//
//   PERSISTS  - set, Write(), reload: the new value is there. Safe to expose as editable.
//   NO-OP     - the setter doesn't even move the in-memory getter (e.g. SAV1.Gender is
//               `get => 0; set { }`). Easy to spot.
//   PHANTOM   - the in-memory getter DOES return the new value, but Write() never serializes it,
//               so a reload silently reverts to the original. This is the dangerous one: any
//               check that only reads the field back off the same live object concludes "works".
//               Predicted for `Language` on SAV1/SAV2/SAV3, where it is a plain auto-property
//               (`public override int Language { get; set; }`) initialized from the save-detection
//               path at construction and never backed by Data[]. Verified here, not assumed.
//   NORMALIZED- persisted, but not as the exact requested value (clamped/truncated/re-encoded).
//
// Every probe reloads the file from disk into a FRESH byte[] and clones it before handing it to
// SaveUtil.GetSaveFile - documented pitfall (PROGRESS.md "Edit round-trip verification"):
// GetSaveFile WRAPS the array as Memory<byte> rather than copying it, so later edits/Write() calls
// mutate that same array in place. Comparing the post-edit array against itself would wrongly
// report the original file as unchanged/changed depending on which side you held. The original
// file on disk is re-read and compared byte-for-byte against a pre-run snapshot after every probe.

var root = @"C:\Users\abhis\Downloads\sav files pkmn";

// The four the task requires, plus every other generation this project has a real save for.
// The extras are cheap (one more run each) and each one removes a row that would otherwise have
// to be documented as source-derived inference rather than tested fact - the same reasoning the
// "Form + Nature + Ability editing" harness used when it added bonus Gen3/Gen4 cases and caught
// Gen4's Nature/Ability split, which pattern-matching from Gen3 would have gotten wrong.
// Gen2 in particular is NOT a formality: SAV2.Gender is `Version == GameVersion.C ? ... : 0`,
// i.e. editability differs WITHIN the generation (Crystal yes, Gold/Silver no).
var targets = new (string Path, string Label)[]
{
    (Path.Combine(root, "POKEMON RED-0.sav"),                            "Gen1"),
    (Path.Combine(root, "Pokemon - Crystal Version (UE) (V1.1) C!.SAV"), "Gen2C"),
    (Path.Combine(root, "pokeemerald (2).sav"),                          "Gen3"),
    (Path.Combine(root, "Pokemon Heart Gold Version.sav"),               "Gen4"),
    (Path.Combine(root, "Pokemon Black Version.sav"),                    "Gen5"),
    (Path.Combine(root, "main"),                                         "Gen7"),
    (Path.Combine(root, @"pokemonsword_100\main"),                       "Gen8"),
    (Path.Combine(root, @"pkmnscarlet_100\main"),                        "Gen9"),

    // The edge case the whole capability-probe design exists for. SAV3RSBox is the GameCube
    // "Pokemon Box: Ruby & Sapphire" storage dump. It reports Generation == 3 but overrides NONE of
    // the trainer members, so it inherits SaveFile's do-nothing defaults and reports OT "PKHeX",
    // Language -1, and 0 for every number. Any Generation-based gate would have shipped a fully
    // enabled trainer editor over values that are not in the file and can never be written back.
    // Genuinely reachable, not hypothetical: SaveUtil.GetSaveFile on the raw .gci bytes - exactly
    // the call MainPage makes on whatever the file picker hands it - detects it as a SAV3RSBox.
    (Path.Combine(root, "01-GPXP-pokemon_rs_memory_box.gci"),             "Gen3RSBox"),
};

var rows = new List<Row>();
var supportRows = new List<SupportRow>();
var allOk = true;

foreach (var (path, label) in targets)
{
    allOk &= TestSave(path, label, rows, supportRows);
    Console.WriteLine();
}

PrintMatrix(rows);
PrintSupportMatrix(supportRows);

var hadError = rows.Any(r => r.Verdict is Verdict.Error);
var hadMismatch = supportRows.Any(r => !r.Agrees);
Console.WriteLine();
Console.WriteLine(allOk && !hadError && !hadMismatch
    ? "=== HARNESS COMPLETED: no errors, production probe agrees on every field, no original file modified ==="
    : "=== HARNESS FAILED: see errors above ===");
return allOk && !hadError && !hadMismatch ? 0 : 1;

// ---------------------------------------------------------------------------------------------

static bool TestSave(string path, string gen, List<Row> rows, List<SupportRow> supportRows)
{
    Console.WriteLine($"##################### {gen}  —  {Path.GetFileName(path)} #####################");
    if (!File.Exists(path))
    {
        Console.WriteLine($"  FAIL: file not found: {path}");
        rows.Add(new Row(gen, "(file)", Verdict.Error, "file not found"));
        return false;
    }

    // Context dump: the per-save constants the UI will branch on.
    var ctxBytes = File.ReadAllBytes(path);
    var ctx = SaveUtil.GetSaveFile(ctxBytes);
    if (ctx is null)
    {
        Console.WriteLine("  FAIL: not recognized as a save file");
        rows.Add(new Row(gen, "(file)", Verdict.Error, "not recognized as a save"));
        return false;
    }

    Console.WriteLine($"  Type={ctx.GetType().Name}  Generation={ctx.Generation}  Version={ctx.Version}");
    Console.WriteLine($"  TrainerIDDisplayFormat={ctx.TrainerIDDisplayFormat}  MaxStringLengthTrainer={ctx.MaxStringLengthTrainer}  MaxMoney={ctx.MaxMoney}");
    Console.WriteLine($"  Current: OT='{ctx.OT}' Gender={ctx.Gender} Language={ctx.Language}({(LanguageID)ctx.Language})");
    Console.WriteLine($"           TrainerTID7={ctx.TrainerTID7} TrainerSID7={ctx.TrainerSID7} DisplayTID={ctx.DisplayTID} DisplaySID={ctx.DisplaySID} (TID16={ctx.TID16} SID16={ctx.SID16} ID32={ctx.ID32})");
    Console.WriteLine($"           Money={ctx.Money} Played={ctx.PlayedHours}:{ctx.PlayedMinutes:00}:{ctx.PlayedSeconds:00}");
    // Pokedex is read-only in the UI (CAPABILITY-AUDIT.md 5.3: SetSeen/SetCaught are `virtual { }`
    // on the base and overridden by only five save classes, so dex WRITING is a silent no-op on
    // Gen4/5/7/8/9). Dumped here anyway because the card's numbers are only as trustworthy as the
    // getters, and HasPokeDex is what gates the card - it is false for storage-only dumps.
    Console.WriteLine($"  Dex: HasPokeDex={ctx.HasPokeDex} Seen={ctx.SeenCount} Caught={ctx.CaughtCount} "
                      + $"MaxSpeciesID={ctx.MaxSpeciesID} PercentCaught={ctx.PercentCaught:P2}");
    Console.WriteLine();

    var ok = true;

    // --- OT ------------------------------------------------------------------------------------
    // Test name is built from this save's own MaxStringLengthTrainer so the probe can never be a
    // false "NORMALIZED" caused by the harness asking for a name that structurally cannot fit.
    ok &= Probe(path, gen, "OT", rows,
        s => s.OT,
        s => { var v = FitName("TESTOTNAME", s.MaxStringLengthTrainer, s.OT); s.OT = v; return v; });

    // --- Gender --------------------------------------------------------------------------------
    ok &= Probe(path, gen, "Gender", rows,
        s => s.Gender.ToString(),
        s => { byte v = s.Gender == 0 ? (byte)1 : (byte)0; s.Gender = v; return v.ToString(); });

    // --- Language ------------------------------------------------------------------------------
    // English <-> French: both are valid LanguageIDs in every generation tested, and neither is
    // Japanese - deliberately avoided, because on SAV1/SAV2/SAV3 the *construction-time* language
    // choice also selects the Japanese string tables / offsets / box counts. Asking for Japanese
    // would conflate "Language doesn't persist" with "Language re-encoded the whole save".
    ok &= Probe(path, gen, "Language", rows,
        s => s.Language.ToString(),
        s =>
        {
            int v = s.Language == (int)LanguageID.French ? (int)LanguageID.English : (int)LanguageID.French;
            s.Language = v;
            return v.ToString();
        });

    // --- DisplayTID / DisplaySID ---------------------------------------------------------------
    // Edited through PKHeX.Core's own display-format accessors rather than raw TID16/SID16 math,
    // because the split differs by generation (16-bit TID/SID pair pre-Gen7 vs. the 6-digit
    // TID7/SID7 decomposition of a single ID32 from Gen7 on) - see ITrainerID32.GetDisplayTID.
    ok &= Probe(path, gen, "DisplayTID", rows,
        s => s.DisplayTID.ToString(),
        s => { var v = PickTid(s); s.DisplayTID = v; return v.ToString(); });

    ok &= Probe(path, gen, "DisplaySID", rows,
        s => s.DisplaySID.ToString(),
        s => { var v = PickSid(s); s.DisplaySID = v; return v.ToString(); });

    // --- Money ---------------------------------------------------------------------------------
    // Persistence is tested with a value BELOW MaxMoney on purpose. Several generations' Money
    // setters clamp internally (SAV1: `value = Math.Min(value, MaxMoney)`), so probing persistence
    // with an over-max value would come back clamped and read as a round-trip failure when it is
    // actually correct behavior. The clamp itself is a separate probe below.
    ok &= Probe(path, gen, "Money", rows,
        s => s.Money.ToString(),
        s => { var v = PickMoney(s); s.Money = v; return v.ToString(); });

    // --- Play time -----------------------------------------------------------------------------
    // Hours deliberately small (42): SAV1.PlayedHours has a documented >=255 branch that sets a
    // "played maximum" flag AND zeroes minutes/seconds/frames, which would mask a normal
    // round-trip result for all three fields at once.
    ok &= Probe(path, gen, "PlayedHours", rows,
        s => s.PlayedHours.ToString(),
        s => { int v = s.PlayedHours == 42 ? 43 : 42; s.PlayedHours = v; return v.ToString(); });

    ok &= Probe(path, gen, "PlayedMinutes", rows,
        s => s.PlayedMinutes.ToString(),
        s => { int v = s.PlayedMinutes == 33 ? 34 : 33; s.PlayedMinutes = v; return v.ToString(); });

    ok &= Probe(path, gen, "PlayedSeconds", rows,
        s => s.PlayedSeconds.ToString(),
        s => { int v = s.PlayedSeconds == 21 ? 22 : 21; s.PlayedSeconds = v; return v.ToString(); });

    // --- Boundary / charset behavior the UI has to defend against ------------------------------
    Console.WriteLine("  --- boundary probes (inform UI validation, not editability) ---");
    ok &= ProbeMoneyClamp(path, gen);
    ok &= ProbeOtOverlength(path, gen);
    ok &= ProbeOtCharset(path, gen);

    // --- The production gate, checked against the ground truth just measured -------------------
    ok &= CrossCheckProductionProbe(path, gen, rows, supportRows);

    return ok;
}

// The point of the whole harness. TrainerFieldSupport is the class the app actually ships (linked
// into this project via <Compile Include>, not copied), and it decides on its own - from ONE batched
// Write()+reload of a throwaway Clone - which controls TrainerInfoPage enables. Here that verdict is
// held against the nine independent single-field round trips measured above.
//
// Two distinct things get proven, and neither is decoration:
//   1. The gate is CORRECT. The disable direction is the safety-critical one (enabling a field that
//      silently discards edits is the exact bug class this project keeps hitting), and no amount of
//      source reading substitutes for measuring it on a real file of each generation.
//   2. The BATCHING is sound. The production probe sets all nine sentinels before a single Write(),
//      which is ~9x cheaper on a multi-megabyte Gen9 save but only valid if no sentinel disturbs
//      another's field. The per-field probes above set exactly one field each, so any cross-talk
//      shows up here as a disagreement rather than being assumed absent.
static bool CrossCheckProductionProbe(string path, string gen, List<Row> rows, List<SupportRow> supportRows)
{
    var bytes = File.ReadAllBytes(path);
    var diskSnapshot = (byte[])bytes.Clone();
    var sav = SaveUtil.GetSaveFile(bytes);
    if (sav is null)
        return false;

    var support = TrainerFieldSupport.Probe(sav);

    Console.WriteLine("  --- production TrainerFieldSupport probe vs. the ground truth above ---");
    Console.WriteLine($"  {"probe",-14} AnySupported={support.AnySupported} HoursCeiling={support.HoursCeiling}"
                      + (support.ProbeError is null ? "" : $" ProbeError='{support.ProbeError}'"));

    var ok = true;
    foreach (var field in TrainerFieldSupport.AllFields)
    {
        var truthRow = rows.FirstOrDefault(r => r.Gen == gen && r.Field == field.ToString());
        if (truthRow is null)
        {
            Console.WriteLine($"  {field,-14} SKIP  (no ground-truth row)");
            continue;
        }

        // Anything short of a clean round-trip must read as "don't enable this control": NoOp and
        // Phantom obviously, but Normalized too - a value that comes back altered is not one the
        // user asked for, and an editor that quietly rewrites input is its own bug.
        var expected = truthRow.Verdict is Verdict.Persists;
        var actual = support[field];
        var agrees = expected == actual;
        ok &= agrees;
        supportRows.Add(new SupportRow(gen, field.ToString(), expected, actual));

        Console.WriteLine($"  {field,-14} {(agrees ? "agree " : "MISMATCH")}  groundTruth={truthRow.Verdict}(editable={expected}) productionProbe={actual}");
    }

    // The probe clones before it mutates; prove that claim rather than trusting it. If Clone() ever
    // shared its buffer, the sentinel junk would be sitting in the live save right now.
    var savUntouched = sav.OT == ctxOtOf(diskSnapshot) ;
    if (!savUntouched)
    {
        Console.WriteLine($"  {"probe(isolation)",-14} FAIL: the live SaveFile's OT was mutated by the probe (now '{sav.OT}')");
        ok = false;
    }
    else
    {
        Console.WriteLine($"  {"probe(isolation)",-14} live SaveFile untouched by the probe (OT still '{sav.OT}')");
    }

    if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(diskSnapshot))
    {
        Console.WriteLine($"  {"probe(disk)",-14} WARNING: ORIGINAL FILE ON DISK CHANGED - this must never happen.");
        ok = false;
    }
    return ok;

    static string ctxOtOf(byte[] snapshot) => SaveUtil.GetSaveFile((byte[])snapshot.Clone())?.OT ?? "<unreadable>";
}

// Core probe: fresh load -> read -> mutate -> read (in-memory) -> Write() -> reload -> read.
static bool Probe(string path, string gen, string field, List<Row> rows,
                  Func<SaveFile, string> read, Func<SaveFile, string> mutate)
{
    var bytes = File.ReadAllBytes(path);
    var diskSnapshot = (byte[])bytes.Clone(); // GetSaveFile wraps `bytes`; keep an untouched copy
    var sav = SaveUtil.GetSaveFile(bytes);
    if (sav is null)
    {
        Console.WriteLine($"  {field,-14} ERROR: not recognized as a save");
        rows.Add(new Row(gen, field, Verdict.Error, "not recognized"));
        return false;
    }

    var before = read(sav);
    string requested;
    try
    {
        requested = mutate(sav);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  {field,-14} ERROR: setter threw {ex.GetType().Name}: {ex.Message}");
        rows.Add(new Row(gen, field, Verdict.Error, $"setter threw {ex.GetType().Name}"));
        return false;
    }

    var inMemory = read(sav);

    byte[] exported;
    try
    {
        exported = sav.Write().ToArray();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  {field,-14} ERROR: Write() threw {ex.GetType().Name}: {ex.Message}");
        rows.Add(new Row(gen, field, Verdict.Error, $"Write() threw {ex.GetType().Name}"));
        return false;
    }

    var reloaded = SaveUtil.GetSaveFile(exported);
    if (reloaded is null)
    {
        Console.WriteLine($"  {field,-14} ERROR: exported bytes not recognized on reload");
        rows.Add(new Row(gen, field, Verdict.Error, "exported bytes not recognized"));
        return false;
    }

    var after = read(reloaded);

    Verdict verdict;
    string detail;
    if (requested == before)
    {
        verdict = Verdict.Inconclusive;
        detail = "could not choose a value distinct from the current one";
    }
    else if (after == requested)
    {
        verdict = Verdict.Persists;
        detail = string.Empty;
    }
    else if (inMemory == before)
    {
        verdict = Verdict.NoOp;
        detail = "setter did not move even the in-memory getter";
    }
    else if (after == before)
    {
        verdict = Verdict.Phantom;
        detail = "in-memory getter reflects the edit, but Write()/reload reverts it";
    }
    else
    {
        verdict = Verdict.Normalized;
        detail = "persisted, but not as the exact requested value";
    }

    Console.WriteLine($"  {field,-14} {verdict,-11} before='{before}' requested='{requested}' inMemory='{inMemory}' afterReload='{after}'"
                      + (detail.Length > 0 ? $"  <- {detail}" : string.Empty));
    rows.Add(new Row(gen, field, verdict, detail));

    var untouched = File.ReadAllBytes(path).AsSpan().SequenceEqual(diskSnapshot);
    if (!untouched)
    {
        Console.WriteLine($"  {field,-14} WARNING: ORIGINAL FILE ON DISK CHANGED - this must never happen.");
        return false;
    }
    return true;
}

// Money clamp: what does the library actually do with an over-MaxMoney value? The UI clamps to
// MaxMoney live (ClampEntryToMax precedent) rather than letting the user type a number that gets
// silently reduced on save - this probe records what "silently reduced" would have looked like.
static bool ProbeMoneyClamp(string path, string gen)
{
    var bytes = File.ReadAllBytes(path);
    var diskSnapshot = (byte[])bytes.Clone();
    var sav = SaveUtil.GetSaveFile(bytes);
    if (sav is null) return false;

    var max = (uint)sav.MaxMoney;
    var over = max + 12345u;
    sav.Money = over;
    var inMemory = sav.Money;
    var reloaded = SaveUtil.GetSaveFile(sav.Write().ToArray());
    var after = reloaded?.Money ?? uint.MaxValue;
    Console.WriteLine($"  {"Money(clamp)",-14} MaxMoney={max} requested={over} inMemory={inMemory} afterReload={after}"
                      + (after <= max ? "  <- clamped at or below MaxMoney" : "  <- NOT clamped (exceeds MaxMoney)"));

    return File.ReadAllBytes(path).AsSpan().SequenceEqual(diskSnapshot);
}

// OT length: what happens to a name longer than MaxStringLengthTrainer? Silent truncation is the
// benign case - but this probe found that it is NOT universally benign. SAV2.OT's setter passes a
// hardcoded maxLength of 8 into a destination buffer only MaxStringLengthTrainer (7) bytes wide
// (SAV2.cs:319), so an 8+ character name makes StringConverter2.SetString write past the end and
// throw IndexOutOfRangeException out of a plain property setter. The UI's defense (setting
// Entry.MaxLength = sav.MaxStringLengthTrainer, so an over-length name can't be typed in the first
// place) therefore isn't just cosmetic truncation-avoidance - on Gen2 it prevents a crash.
// The exact first-throwing length is scanned per save rather than assumed.
static bool ProbeOtOverlength(string path, string gen)
{
    var bytes = File.ReadAllBytes(path);
    var diskSnapshot = (byte[])bytes.Clone();
    var sav = SaveUtil.GetSaveFile(bytes);
    if (sav is null) return false;

    var max = sav.MaxStringLengthTrainer;

    // Scan upward for the first length whose setter throws (if any) within a generous margin.
    int firstThrow = -1;
    string? throwType = null;
    for (int len = 1; len <= max + 8 && firstThrow < 0; len++)
    {
        try { sav.OT = new string('A', len); }
        catch (Exception ex) { firstThrow = len; throwType = ex.GetType().Name; }
    }

    // Reload the file for the truncation half of the probe - the scan above may have left the
    // buffer mid-write if it threw.
    var bytes2 = File.ReadAllBytes(path);
    var sav2 = SaveUtil.GetSaveFile(bytes2);
    if (sav2 is null) return false;

    var safeOver = new string('A', max); // longest name the UI will ever allow through
    sav2.OT = safeOver;
    var inMemory = sav2.OT;
    var reloaded = SaveUtil.GetSaveFile(sav2.Write().ToArray());
    var after = reloaded?.OT ?? "<reload failed>";

    Console.WriteLine($"  {"OT(length)",-14} max={max}: at max '{inMemory}'({inMemory.Length}) -> afterReload '{after}'({after.Length}); "
        + (firstThrow < 0
            ? $"no length up to {max + 8} throws (over-length is silently truncated)"
            : $"*** setter THROWS {throwType} at length {firstThrow} *** (max+{firstThrow - max})"));

    return File.ReadAllBytes(path).AsSpan().SequenceEqual(diskSnapshot);
}

// OT charset: Gen1/2 use a bespoke single-byte character table (StringConverter1/2), not Unicode -
// most non-ASCII input has no representation at all and comes back as something else. Rather than
// hardcoding a per-generation allowed-character list in the UI, the page does a set-then-get
// round-trip through PKHeX.Core's own converter and refuses to save when the readback differs.
// This probe establishes that such a mismatch is actually detectable that way on every generation.
static bool ProbeOtCharset(string path, string gen)
{
    var bytes = File.ReadAllBytes(path);
    var diskSnapshot = (byte[])bytes.Clone();
    var sav = SaveUtil.GetSaveFile(bytes);
    if (sav is null) return false;

    foreach (var candidate in new[] { "TEST", "Test", "Tést", "テスト", "тест" })
    {
        var fitted = candidate.Length > sav.MaxStringLengthTrainer ? candidate[..sav.MaxStringLengthTrainer] : candidate;
        sav.OT = fitted;
        var readback = sav.OT;
        var lossless = readback == fitted;
        Console.WriteLine($"  {"OT(charset)",-14} '{fitted}' -> readback '{readback}'  {(lossless ? "lossless" : "LOSSY (UI must reject)")}");
    }

    return File.ReadAllBytes(path).AsSpan().SequenceEqual(diskSnapshot);
}

// ---------------------------------------------------------------------------------------------

static string FitName(string candidate, int maxLength, string current)
{
    var name = candidate.Length > maxLength ? candidate[..maxLength] : candidate;
    if (name != current)
        return name;
    var alt = "ZZTESTZZTEST";
    return alt.Length > maxLength ? alt[..maxLength] : alt;
}

// Valid TID range depends on the display format: 5-digit 16-bit (<=65535) pre-Gen7, 6-digit
// TID7 (<=999999) from Gen7 on. Chosen below the ceiling in both cases so a "did not persist"
// result can't be an out-of-range wrap in disguise.
static uint PickTid(SaveFile s)
{
    uint v = s.TrainerIDDisplayFormat == TrainerIDFormat.SixDigit ? 123456u : 12345u;
    if (s.DisplayTID == v)
        v = s.TrainerIDDisplayFormat == TrainerIDFormat.SixDigit ? 654321u : 54321u;
    return v;
}

// SID7's ceiling is NOT 999999: ID32 = (sid7 * 1_000_000) + tid7 must fit in a uint, so sid7 tops
// out at 4294 (IsValidTrainerID7). SetTrainerID7's own comment notes it "overflow[s] back to sid:0
// on bad combination" - i.e. a too-large pair silently wraps rather than throwing.
static uint PickSid(SaveFile s)
{
    uint v = s.TrainerIDDisplayFormat == TrainerIDFormat.SixDigit ? 1234u : 4321u;
    if (s.DisplaySID == v)
        v = s.TrainerIDDisplayFormat == TrainerIDFormat.SixDigit ? 2345u : 5432u;
    return v;
}

static uint PickMoney(SaveFile s)
{
    var max = (uint)s.MaxMoney;
    uint v = Math.Min(123456u, max);
    if (s.Money == v)
        v = Math.Min(654321u, max);
    if (s.Money == v)
        v = max / 2;
    return v;
}

static void PrintMatrix(List<Row> rows)
{
    var fields = rows.Select(r => r.Field).Distinct().ToList();
    var gens = rows.Select(r => r.Gen).Distinct().ToList();

    Console.WriteLine("=================================== SUMMARY MATRIX ===================================");
    Console.WriteLine("| Field | " + string.Join(" | ", gens) + " |");
    Console.WriteLine("|---|" + string.Concat(gens.Select(_ => "---|")));
    foreach (var f in fields)
    {
        var cells = gens.Select(g => rows.FirstOrDefault(r => r.Gen == g && r.Field == f)?.Verdict.ToString() ?? "-");
        Console.WriteLine($"| {f} | " + string.Join(" | ", cells) + " |");
    }
    Console.WriteLine();
    Console.WriteLine("PERSISTS   = safe to expose as editable");
    Console.WriteLine("NO-OP      = setter does nothing at all; UI must disable the control");
    Console.WriteLine("PHANTOM    = in-memory getter lies; reverts on reload; UI must disable the control");
    Console.WriteLine("NORMALIZED = persisted but altered (clamped/truncated/re-encoded)");
}

static void PrintSupportMatrix(List<SupportRow> rows)
{
    var fields = rows.Select(r => r.Field).Distinct().ToList();
    var gens = rows.Select(r => r.Gen).Distinct().ToList();

    Console.WriteLine();
    Console.WriteLine("======================= PRODUCTION GATE (TrainerFieldSupport) =======================");
    Console.WriteLine("What TrainerInfoPage actually enables. 'on'/'off' = the shipped probe's verdict;");
    Console.WriteLine("a MISMATCH cell means it disagrees with the measured round-trip and the gate is wrong.");
    Console.WriteLine("| Field | " + string.Join(" | ", gens) + " |");
    Console.WriteLine("|---|" + string.Concat(gens.Select(_ => "---|")));
    foreach (var f in fields)
    {
        var cells = gens.Select(g =>
        {
            var r = rows.FirstOrDefault(x => x.Gen == g && x.Field == f);
            if (r is null)
                return "-";
            return r.Agrees ? (r.Actual ? "on" : "off") : $"MISMATCH(exp {r.Expected}, got {r.Actual})";
        });
        Console.WriteLine($"| {f} | " + string.Join(" | ", cells) + " |");
    }
}

enum Verdict { Persists, NoOp, Phantom, Normalized, Inconclusive, Error }

record Row(string Gen, string Field, Verdict Verdict, string Detail);

record SupportRow(string Gen, string Field, bool Expected, bool Actual)
{
    public bool Agrees => Expected == Actual;
}
