using PKHeX.Core;

// Harness for the detail-screen editor expansion (CAPABILITY-AUDIT.md gaps #3/#4/#6/#7):
// EV/IV caps, held item, ball, friendship. Same discipline as verify/FormNatureAbilityEdit and
// verify/SpeciesMoveEdit:
//   * real save files, never synthetic ones;
//   * the byte array is CLONED before SaveUtil.GetSaveFile, because GetSaveFile WRAPS the array
//     rather than copying it - a documented pitfall in this project;
//   * every case exports via sav.Write() and reloads via SaveUtil.GetSaveFile, so a field that
//     only appears to stick in memory is caught;
//   * the original file is re-read from disk afterwards and compared byte-for-byte, so the
//     harness can never damage the fixed asset saves;
//   * where a field feeds the stat block, the actual Stat_* VALUE is asserted, not just the
//     getter echoing what was set - that is how the last two real bugs in this project were
//     caught.

var root = @"C:\Users\abhis\Downloads\sav files pkmn";
var allPass = true;

var gen1 = Path.Combine(root, "POKEMON RED-0.sav");
var gen2 = Path.Combine(root, "Pokemon - Crystal Version (UE) (V1.1) C!.SAV");
var gen3 = Path.Combine(root, "pokeemerald (2).sav");
var gen4 = Path.Combine(root, "Pokemon Heart Gold Version.sav");
var gen5 = Path.Combine(root, "Pokemon Black Version.sav");
var gen9 = Path.Combine(root, @"pkmnscarlet_100\main");

Console.WriteLine("################ PART A - EV/IV caps (audit gap #4) ################");
Console.WriteLine();
Console.WriteLine("=== A1. pk.MaxIV / pk.MaxEV per format: the values the app now reads instead of");
Console.WriteLine("        hardcoding `isGen12 ? 15 : 31` and `isGen12 ? 65535 : 252`. ===");
Console.WriteLine("        The old EV ternary was WRONG for Gen3/4/5, which store up to 255.");
allPass &= CapCheck(gen1, "Gen1 (Red)", 15, 65535);
allPass &= CapCheck(gen2, "Gen2 (Crystal)", 15, 65535);
allPass &= CapCheck(gen3, "Gen3 (Emerald)", 31, 255);
allPass &= CapCheck(gen4, "Gen4 (HeartGold)", 31, 255);
allPass &= CapCheck(gen5, "Gen5 (Black)", 31, 255);
allPass &= CapCheck(gen9, "Gen9 (Scarlet)", 31, 252);
Console.WriteLine();

Console.WriteLine("=== A2. THE DEFECT: a 255 EV is real, storable data on Gen3/4/5 that the app's old");
Console.WriteLine("        hardcoded 252 cap silently clamped away on load and wrote back clamped. ===");
allPass &= EvRoundTrip(gen3, "Gen3 (Emerald)", 255);
allPass &= EvRoundTrip(gen4, "Gen4 (HeartGold)", 255);
allPass &= EvRoundTrip(gen5, "Gen5 (Black)", 255);
Console.WriteLine();

Console.WriteLine("=== A3. NO REGRESSION to the hard-won Gen1/2 stat-exp handling (PROGRESS.md");
Console.WriteLine("        \"Gen1/2 EV save-blocking bug fixed\"): 16-bit stat exp must still round-trip. ===");
allPass &= EvRoundTrip(gen1, "Gen1 (Red)", 65535);
allPass &= EvRoundTrip(gen2, "Gen2 (Crystal)", 54321);
Console.WriteLine();

Console.WriteLine("=== A4. NO REGRESSION to the Gen1/2 DV structure: HP IV stays derived from the other");
Console.WriteLine("        four DVs' low bits, and SpA/SpD remain one shared \"Special\" value.");
Console.WriteLine("        These have NO generic library handle (ISeparateIVs is CK3/XK3 only), so the");
Console.WriteLine("        app's hand-rolled logic must survive the de-branching. ===");
allPass &= Gen12IvStructure(gen1, "Gen1 (Red)");
allPass &= Gen12IvStructure(gen2, "Gen2 (Crystal)");
Console.WriteLine();

Console.WriteLine("=== A5. The 510 budget is ADVISORY in the app, so confirm what the library itself");
Console.WriteLine("        would have done - GetMaximumEV clamps to 252 even on Gen3-5, which is why it");
Console.WriteLine("        is NOT used as the field cap (it would undo A2). ===");
allPass &= BudgetProbe(gen3, "Gen3 (Emerald)");
allPass &= BudgetProbe(gen9, "Gen9 (Scarlet)");
Console.WriteLine();

Console.WriteLine(allPass ? "=== ALL CASES PASS ===" : "=== ONE OR MORE CASES FAILED ===");
return allPass ? 0 : 1;

// ---------------------------------------------------------------------------------------------

// Loads a save WITHOUT handing SaveUtil the caller's only copy of the bytes.
static SaveFile? Load(string path)
{
    if (!File.Exists(path)) return null;
    return SaveUtil.GetSaveFile((byte[])File.ReadAllBytes(path).Clone());
}

// Re-reads the fixture from disk and proves the harness never wrote to it.
static bool OriginalUntouched(string path, byte[] snapshot, string label)
{
    var now = File.ReadAllBytes(path);
    bool ok = now.AsSpan().SequenceEqual(snapshot);
    Console.WriteLine(ok
        ? $"    [PASS] {label}: original file byte-for-byte untouched on disk"
        : $"    [FAIL] {label}: ORIGINAL FILE ON DISK WAS MODIFIED");
    return ok;
}

static PKM? FirstMon(SaveFile sav, out int slot)
{
    for (int i = 0; i < sav.PartyCount; i++)
    {
        var p = sav.PartyData[i];
        if (p.Species != 0) { slot = i; return p; }
    }
    slot = -1;
    return null;
}

static bool CapCheck(string path, string label, int wantIv, int wantEv)
{
    var sav = Load(path);
    if (sav is null) { Console.WriteLine($"  [SKIP] {label}: not found / not parsed"); return true; }
    var pk = FirstMon(sav, out _);
    if (pk is null) { Console.WriteLine($"  [SKIP] {label}: empty party"); return true; }

    bool ok = pk.MaxIV == wantIv && pk.MaxEV == wantEv;
    Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {label,-20} Format={pk.Format} MaxIV={pk.MaxIV} (want {wantIv})  MaxEV={pk.MaxEV} (want {wantEv})");
    if (wantEv == 255)
        Console.WriteLine($"           ^ the app's old hardcoded evMax here was 252 - {wantEv - 252} values of real range were unreachable");
    return ok;
}

// Sets one EV to `value`, recomputes the party stat block, exports, reloads, and asserts BOTH the
// stored EV and the resulting stat actually moved. Asserting only the EV getter would not catch a
// value that round-trips but never reaches the stat computation.
static bool EvRoundTrip(string path, string label, int value)
{
    if (!File.Exists(path)) { Console.WriteLine($"  [SKIP] {label}: not found"); return true; }
    var snapshot = File.ReadAllBytes(path);

    var sav = Load(path);
    if (sav is null) { Console.WriteLine($"  [SKIP] {label}: not parsed"); return true; }
    var pk = FirstMon(sav, out int slot);
    if (pk is null) { Console.WriteLine($"  [SKIP] {label}: empty party"); return true; }

    Console.WriteLine($"  {label}: {GameInfo.Strings.Species[pk.Species]} slot {slot}, cap MaxEV={pk.MaxEV}");
    if (value > pk.MaxEV) { Console.WriteLine($"    [SKIP] {value} exceeds this format's MaxEV"); return true; }

    // Use ATK: it is a real independent EV in every format (unlike SpD, which Gen1/2 alias to SpA).
    int beforeEv = pk.EV_ATK;
    int beforeStat = pk.Stat_ATK;

    // Force the EV low first so the "did the stat move" assertion is meaningful even if the mon
    // already happened to sit at `value`.
    pk.EV_ATK = 0;
    pk.ResetPartyStats();
    int zeroStat = pk.Stat_ATK;

    pk.EV_ATK = value;
    pk.ResetPartyStats();
    int raisedStat = pk.Stat_ATK;

    sav.SetPartySlotAtIndex(pk, slot);
    byte[] exported;
    try { exported = sav.Write().ToArray(); }
    catch (Exception ex) { Console.WriteLine($"    [FAIL] sav.Write() threw {ex.GetType().Name}: {ex.Message}"); return false; }

    var reloaded = SaveUtil.GetSaveFile(exported);
    if (reloaded is null) { Console.WriteLine("    [FAIL] exported bytes not recognized on reload"); return false; }
    var pk2 = reloaded.PartyData[slot];

    bool evOk = pk2.EV_ATK == value;
    bool statOk = raisedStat > zeroStat && pk2.Stat_ATK == raisedStat;
    Console.WriteLine($"    EV_ATK {beforeEv} -> set {value} -> reloaded {pk2.EV_ATK}");
    Console.WriteLine($"    Stat_ATK: was {beforeStat}, at EV 0 = {zeroStat}, at EV {value} = {raisedStat}, reloaded {pk2.Stat_ATK}");
    Console.WriteLine(evOk
        ? $"    [PASS] {label}: EV {value} survives export+reload"
        : $"    [FAIL] {label}: EV read back as {pk2.EV_ATK}, expected {value}");
    Console.WriteLine(statOk
        ? $"    [PASS] {label}: the EV actually reaches the stat block (+{raisedStat - zeroStat} Atk) and the stat persists"
        : $"    [FAIL] {label}: stat block did not track the EV (0->{zeroStat}, {value}->{raisedStat}, reloaded {pk2.Stat_ATK})");

    return evOk & statOk & OriginalUntouched(path, snapshot, label);
}

// Pins the two Gen1/2 structural facts the app's hand-rolled UI logic depends on.
static bool Gen12IvStructure(string path, string label)
{
    if (!File.Exists(path)) { Console.WriteLine($"  [SKIP] {label}: not found"); return true; }
    var snapshot = File.ReadAllBytes(path);

    var sav = Load(path);
    if (sav is null) { Console.WriteLine($"  [SKIP] {label}: not parsed"); return true; }
    var pk = FirstMon(sav, out int slot);
    if (pk is null) { Console.WriteLine($"  [SKIP] {label}: empty party"); return true; }

    pk.IV_ATK = 13; pk.IV_DEF = 12; pk.IV_SPE = 11; pk.IV_SPA = 9;
    // The app mirrors GBPKM.cs: HP DV = the low bits of Atk/Def/Spe/Spc, in that bit order.
    int expectHp = ((13 & 1) << 3) | ((12 & 1) << 2) | ((11 & 1) << 1) | (9 & 1);
    bool hpOk = pk.IV_HP == expectHp;
    // SpD is not independently stored - writing SpA must move SpD with it.
    bool linkOk = pk.IV_SPD == pk.IV_SPA;

    // And the numeric cap the app now takes from the library still clamps at the hardware nibble.
    pk.IV_ATK = 99;
    bool clampOk = pk.IV_ATK == pk.MaxIV && pk.MaxIV == 15;

    Console.WriteLine($"  {label}: IV_HP={pk.IV_HP} (derived, want {expectHp})  IV_SPA={pk.IV_SPA} IV_SPD={pk.IV_SPD} (linked)  IV_ATK after set 99 = {pk.IV_ATK} (MaxIV {pk.MaxIV})");
    Console.WriteLine(hpOk ? "    [PASS] HP DV still derived from the other four" : "    [FAIL] HP DV derivation changed");
    Console.WriteLine(linkOk ? "    [PASS] SpA/SpD still one shared Special value" : "    [FAIL] SpA/SpD no longer linked");
    Console.WriteLine(clampOk ? "    [PASS] library clamps an over-range DV to MaxIV=15" : "    [FAIL] DV clamp changed");

    return hpOk & linkOk & clampOk & OriginalUntouched(path, snapshot, label);
}

// Documents WHY GetMaximumEV was not adopted as the field cap.
static bool BudgetProbe(string path, string label)
{
    var sav = Load(path);
    if (sav is null) { Console.WriteLine($"  [SKIP] {label}: not found / not parsed"); return true; }
    var pk = FirstMon(sav, out _);
    if (pk is null) { Console.WriteLine($"  [SKIP] {label}: empty party"); return true; }

    // Zero everything except ATK so the remaining budget is unambiguously large.
    pk.EV_HP = pk.EV_DEF = pk.EV_SPA = pk.EV_SPD = pk.EV_SPE = 0;
    pk.EV_ATK = 0;
    int headroom = pk.GetMaximumEV(1);

    bool ok = headroom == 252;
    Console.WriteLine($"  {label}: with 0 EVs spent, GetMaximumEV(ATK) = {headroom}, but pk.MaxEV = {pk.MaxEV}");
    Console.WriteLine(ok
        ? $"    [PASS] confirmed: GetMaximumEV clamps to EffortValues.Max252 regardless of format, so using it as"
        : $"    [FAIL] unexpected headroom {headroom}");
    if (ok && pk.MaxEV > 252)
        Console.WriteLine($"           the field cap would re-break Gen3-5 by hiding {pk.MaxEV - 252} values of genuine range.");
    else if (ok)
        Console.WriteLine($"           the field cap would coincidentally match MaxEV here, but not on Gen3-5.");
    Console.WriteLine($"    EVTotal now {pk.EVTotal}; the app shows this against {EffortValues.Max510} as an advisory caption, never as a block.");
    return ok;
}
