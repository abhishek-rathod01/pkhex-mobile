using PKHeX.Core;
using PkhexMobile;

// Proves the four single-Pokemon interchange paths in PkhexMobile/EntityTransferService.cs
// (compile-linked into this project, so this tests the exact code the app runs):
//
//   1. .pkX entity EXPORT  - PKM -> stored-size bytes
//   2. Showdown EXPORT     - PKM -> text
//   3. .pkX entity IMPORT  - bytes -> PKM, incl. cross-generation conversion accept/refuse
//   4. Showdown IMPORT     - text -> applied onto a PKM
//
// and, specifically, the four ordering/staleness bug classes this project has already been bitten
// by (PROGRESS.md), each asserted on REAL VALUES rather than "the getter returned what I set" -
// that discipline is what caught the last two real bugs:
//
//   A. party stat block staleness   -> assert Stat_HPMax/Stat_ATK actually move
//   B. species-before-level ordering -> CurrentLevel is EXP reinterpreted through the species'
//                                       growth rate, so a species write after a level write
//                                       silently changes the level. Assert the level survives.
//   C. move PP staleness             -> assert each PP equals the new move's max, not the old
//                                       move's leftover value
//   D. Gen8+ Mint (StatAlignment)    -> PKM.LoadStats reads StatAlignment, not Nature, for the
//                                       stat-boost calc. Assert BOTH fields AND the stat delta.
//
// Every save is loaded from a CLONE of the file bytes (SaveUtil.GetSaveFile wraps, it does not
// copy - documented pitfall), and every case re-reads the original file afterwards to prove it is
// byte-for-byte untouched on disk.

var root = @"C:\Users\abhis\Downloads\sav files pkmn";
var gen1 = Path.Combine(root, "POKEMON RED-0.sav");
var gen3 = Path.Combine(root, "pokeemerald (2).sav");
var gen5 = Path.Combine(root, "Pokemon Black Version.sav");
var gen9 = Path.Combine(root, @"pkmnscarlet_100\main");

var allPass = true;

Console.WriteLine("############ 1. .pkX ENTITY EXPORT -> READ BACK -> IDENTICAL ############\n");
allPass &= TestEntityRoundTrip(gen1, "Gen1 (Red)");
allPass &= TestEntityRoundTrip(gen3, "Gen3 (Emerald)");
allPass &= TestEntityRoundTrip(gen5, "Gen5 (Black)");
allPass &= TestEntityRoundTrip(gen9, "Gen9 (Scarlet)");

Console.WriteLine("############ 2. SHOWDOWN EXPORT -> PARSE -> APPLY: WHAT SURVIVES ############\n");
allPass &= TestShowdownFidelity(gen5, "Gen5 (Black)");
allPass &= TestShowdownFidelity(gen9, "Gen9 (Scarlet)");

Console.WriteLine("############ 3. CROSS-GENERATION .pkX IMPORT: ACCEPT / REFUSE ############\n");
allPass &= TestCrossGenImport(gen5, gen9, "PK5 -> Gen9 save (forward)");
allPass &= TestCrossGenImport(gen9, gen5, "PK9 -> Gen5 save (backward - expected refusal)");
allPass &= TestCrossGenImport(gen1, gen5, "PK1 -> Gen5 save (expected refusal: GB only transfers to Gen7+)");
allPass &= TestCrossGenImport(gen1, gen9, "PK1 -> Gen9 save (Virtual Console route)");
allPass &= TestCrossGenImport(gen3, gen5, "PK3 -> Gen5 save (forward)");
allPass &= TestImportGarbageRefused(gen9);

Console.WriteLine("############ 4. SHOWDOWN IMPORT INTO A PARTY SLOT: BUG CLASSES A-D ############\n");
allPass &= TestShowdownImportIntoParty(gen9, "Gen9 (Scarlet)", "Garchomp", 78, minFormat: 8);
allPass &= TestShowdownImportIntoParty(gen5, "Gen5 (Black)", "Volcarona", 73, minFormat: 0);
allPass &= TestShowdownImportIntoParty(gen3, "Gen3 (Emerald)", "Salamence", 55, minFormat: 0);
allPass &= TestShowdownImportIntoParty(gen1, "Gen1 (Red)", "Alakazam", 62, minFormat: 0);

Console.WriteLine("############ 5. SHOWDOWN PARSE ERRORS MUST SURFACE, NOT BE SWALLOWED ############\n");
allPass &= TestShowdownParseErrors(gen9);

Console.WriteLine(allPass ? "=== ALL CASES PASS ===" : "=== ONE OR MORE CASES FAILED ===");
return allPass ? 0 : 1;

// ============================================================================================
// Case 5 - ShowdownSet.InvalidLines must reach the caller as readable text.
// ============================================================================================
static bool TestShowdownParseErrors(string path)
{
    Console.WriteLine("=== Malformed / unimportable sets ===");
    var snapshot = File.ReadAllBytes(path);
    var sav = SaveUtil.GetSaveFile((byte[])snapshot.Clone());
    if (sav is null) { Fail("save not recognized"); return false; }
    var ok = true;

    // (a) A set with junk lines: still applied (PKHeX desktop behaviour), errors reported.
    var messy = """
                Garchomp @ Nonexistent Berry
                Ability: Fake Ability
                Timid Nature
                - Earthquake
                - Not A Real Move
                """;
    var r1 = EntityTransferService.ImportShowdown(messy, sav.BlankPKM);
    Console.WriteLine($"  messy set -> Applied={r1.Applied}, {r1.ParseErrors.Count} parse error(s): \"{r1.Message}\"");
    foreach (var e in r1.ParseErrors)
        Console.WriteLine($"      - {e}");
    if (!r1.Applied)
        ok &= Fail("a set with junk lines should still apply (matching PKHeX desktop), with errors reported");
    else if (r1.ParseErrors.Count == 0)
        ok &= Fail("junk lines produced NO parse errors - they were swallowed");
    else if (r1.ParseErrors.Any(string.IsNullOrWhiteSpace))
        ok &= Fail("a parse error came back as an empty string - the user would see nothing");
    else
        Console.WriteLine("  PASS: junk lines are reported as readable text, and the set still applies");

    // (b) Text with no species at all: refused, with a reason.
    foreach (var (name, text) in new[]
    {
        ("empty text", ""),
        ("whitespace only", "   \n  \n"),
        ("prose, no species", "hello there, this is not a pokemon set at all"),
    })
    {
        var r = EntityTransferService.ImportShowdown(text, sav.BlankPKM);
        if (r.Applied)
            ok &= Fail($"{name}: was APPLIED - it should have been refused");
        else if (string.IsNullOrWhiteSpace(r.Message))
            ok &= Fail($"{name}: refused with no reason");
        else
            Console.WriteLine($"  PASS: {name} -> refused: \"{r.Message}\"");
    }

    // (c) A species that postdates the destination save must be refused rather than clamped to a
    // different species by ApplySetDetails' Math.Min(pk.MaxSpeciesID, set.Species).
    var oldSnap = File.ReadAllBytes(Path.Combine(@"C:\Users\abhis\Downloads\sav files pkmn", "POKEMON RED-0.sav"));
    var oldSav = SaveUtil.GetSaveFile((byte[])oldSnap.Clone());
    if (oldSav is not null)
    {
        var r3 = EntityTransferService.ImportShowdown("Garchomp\n- Earthquake", oldSav.BlankPKM);
        if (r3.Applied)
            ok &= Fail("Garchomp (#445) was applied to a Gen1 save - ApplySetDetails would clamp it to a different species");
        else
            Console.WriteLine($"  PASS: out-of-generation species refused: \"{r3.Message}\"");
    }

    ok &= AssertFileUntouched(path, snapshot);
    Console.WriteLine();
    return ok;
}

// ============================================================================================
// Case 1 - .pkX export round trip.
// ============================================================================================
static bool TestEntityRoundTrip(string path, string label)
{
    Console.WriteLine($"=== {label}: export party slot 0 to .pkX, read it back ===");
    if (!File.Exists(path)) { Fail($"file not found: {path}"); return false; }

    var snapshot = File.ReadAllBytes(path);
    var sav = SaveUtil.GetSaveFile((byte[])snapshot.Clone());
    if (sav is null) { Fail("not recognized as a save"); return false; }

    var pk = sav.PartyData[0];
    var ok = true;

    var export = EntityTransferService.ExportEntity(pk);
    Console.WriteLine($"  file name : {export.FileName}");
    Console.WriteLine($"  bytes     : {export.Data.Length} (SIZE_STORED = {pk.SIZE_STORED}, SIZE_PARTY = {pk.SIZE_PARTY})");

    // The name must carry the format extension the save itself would accept.
    var ext = Path.GetExtension(export.FileName).TrimStart('.');
    if (!sav.PKMExtensions.Contains(ext))
        ok &= Fail($"extension '{ext}' is not in sav.PKMExtensions [{string.Join(", ", sav.PKMExtensions)}]");
    else
        Console.WriteLine($"  PASS: extension '{ext}' is accepted by this save (sav.PKMExtensions)");

    foreach (var c in Path.GetInvalidFileNameChars())
    {
        if (export.FileName.Contains(c))
            ok &= Fail($"file name contains an invalid path character U+{(int)c:X4}");
    }

    // Read back through the real import path.
    var back = EntityTransferService.ImportEntity(export.Data, sav);
    if (!back.Success || back.Entity is null)
        return Fail($"read-back refused: {back.Message}");
    var re = back.Entity;

    if (re.GetType() != pk.GetType())
        ok &= Fail($"read back as {re.GetType().Name}, expected {pk.GetType().Name}");
    if (back.Converted)
        ok &= Fail("read-back reported a conversion; a same-format file must import directly");

    // Byte-identity is checked by re-exporting the entity we just read back and comparing the two
    // FILES, rather than by slicing the in-memory buffers.
    //
    // Worth spelling out, because it is a real trap: PKM.SIZE_STORED is the size of the EXPORTED
    // FILE, which is not always the length of the in-memory Data buffer. On Gen1/2 they differ
    // outright - PK1.SIZE_STORED is PokeCrypto.SIZE_1ULIST (69) because a .pk1 file is a one-entry
    // PokeList wrapper (PK1.cs:13, :48), while PK1's own Data buffer is SIZE_1PARTY (44). Slicing
    // pk.Data[..pk.SIZE_STORED] therefore throws on Gen1 (it did, on the first run of this
    // harness). The exporter is fine - it allocates new byte[pk.SIZE_STORED] and lets
    // WriteDecryptedDataStored wrap into it - but any *consumer* that assumes
    // SIZE_STORED <= Data.Length is wrong. Comparing exported files is format-agnostic and is
    // also the property that actually matters: the same mon must always produce the same file.
    var reExport = EntityTransferService.ExportEntity(re);
    if (!export.Data.AsSpan().SequenceEqual(reExport.Data))
    {
        var n = Math.Min(export.Data.Length, reExport.Data.Length);
        var diff = export.Data.Length != reExport.Data.Length ? -1 : 0;
        for (int i = 0; i < n; i++) if (export.Data[i] != reExport.Data[i]) diff++;
        ok &= Fail($"file bytes changed across export -> import -> export " +
                   $"({export.Data.Length} vs {reExport.Data.Length} bytes, {diff} differing)");
    }
    else
    {
        Console.WriteLine($"  PASS: {export.Data.Length} file bytes identical across export -> import -> export");
    }

    if (export.FileName != reExport.FileName)
        ok &= Fail($"file name changed across the round trip: '{export.FileName}' -> '{reExport.FileName}'");

    // And the in-memory buffers - but only over the STORED region, which is all a .pkX file
    // carries. The trailing SIZE_STORED..SIZE_PARTY bytes are the party stat block: a derived
    // cache that is deliberately absent from the file, so a freshly-imported entity has a blank
    // one. (Measured on the first run of this harness: 13 bytes on Gen3, 57 on Gen5, 9 on Gen9.)
    // That is precisely why the .pkX import path has to call ResetPartyStats() itself before
    // writing into a party slot - unlike Showdown import, nothing in the .pkX path does it for
    // you. Comparing whole buffers here would flag that correct behaviour as a bug.
    var storedLen = Math.Min(pk.SIZE_STORED, pk.Data.Length);
    if (Math.Min(re.SIZE_STORED, re.Data.Length) != storedLen)
    {
        ok &= Fail($"stored-region length changed: {storedLen} -> {Math.Min(re.SIZE_STORED, re.Data.Length)}");
    }
    else
    {
        var srcData = pk.Data[..storedLen].ToArray();
        var dstData = re.Data[..storedLen].ToArray();
        if (!srcData.AsSpan().SequenceEqual(dstData))
        {
            var diff = 0;
            for (int i = 0; i < storedLen; i++) if (srcData[i] != dstData[i]) diff++;
            ok &= Fail($"in-memory stored data differs after round trip ({diff} of {storedLen} byte(s))");
        }
        else
        {
            Console.WriteLine($"  PASS: {storedLen} in-memory stored bytes identical after export -> import " +
                              $"(party stat block {pk.SIZE_STORED}..{pk.SIZE_PARTY} is not carried by a .pkX file, by design)");
        }
    }

    // Belt and braces: the identity-bearing fields, in case a format ever tolerates byte drift.
    ok &= Check("species", pk.Species, re.Species);
    ok &= Check("nickname", pk.Nickname, re.Nickname);
    ok &= Check("level", pk.CurrentLevel, re.CurrentLevel);
    ok &= Check("EXP", pk.EXP, re.EXP);
    ok &= Check("OT", pk.OriginalTrainerName, re.OriginalTrainerName);
    ok &= Check("TID16", pk.TID16, re.TID16);
    ok &= Check("SID16", pk.SID16, re.SID16);
    ok &= Check("PID", pk.PID, re.PID);
    ok &= Check("held item", pk.HeldItem, re.HeldItem);
    ok &= Check("shiny", pk.IsShiny, re.IsShiny);
    ok &= Check("moves", $"{pk.Move1}/{pk.Move2}/{pk.Move3}/{pk.Move4}", $"{re.Move1}/{re.Move2}/{re.Move3}/{re.Move4}");
    ok &= Check("IVs", string.Join(",", pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPA, pk.IV_SPD, pk.IV_SPE),
                       string.Join(",", re.IV_HP, re.IV_ATK, re.IV_DEF, re.IV_SPA, re.IV_SPD, re.IV_SPE));
    ok &= Check("EVs", string.Join(",", pk.EV_HP, pk.EV_ATK, pk.EV_DEF, pk.EV_SPA, pk.EV_SPD, pk.EV_SPE),
                       string.Join(",", re.EV_HP, re.EV_ATK, re.EV_DEF, re.EV_SPA, re.EV_SPD, re.EV_SPE));

    ok &= AssertFileUntouched(path, snapshot);
    Console.WriteLine();
    return ok;
}

// ============================================================================================
// Case 2 - Showdown export fidelity. Applies the exported text onto a DIFFERENT entity (a blank
// of the save's own type) so that a field only "matches" if the TEXT actually carried it.
// Applying onto a clone of the source would make every field trivially match and prove nothing.
// ============================================================================================
static bool TestShowdownFidelity(string path, string label)
{
    Console.WriteLine($"=== {label}: PKM -> Showdown text -> parse -> apply onto a blank ===");
    if (!File.Exists(path)) { Fail($"file not found: {path}"); return false; }

    var snapshot = File.ReadAllBytes(path);
    var sav = SaveUtil.GetSaveFile((byte[])snapshot.Clone());
    if (sav is null) { Fail("not recognized as a save"); return false; }

    var src = sav.PartyData[0];
    var ok = true;

    var text = EntityTransferService.ExportShowdown(src);
    Console.WriteLine("  --- exported text ---");
    foreach (var line in text.Split('\n'))
        Console.WriteLine($"  | {line.TrimEnd('\r')}");

    var target = sav.BlankPKM;
    var import = EntityTransferService.ImportShowdown(text, target);
    if (!import.Applied)
        return Fail($"re-import of our own exported text was refused: {import.Message}");
    if (import.ParseErrors.Count != 0)
        ok &= Fail($"our own exported text did not parse cleanly: {string.Join("; ", import.ParseErrors)}");
    else
        Console.WriteLine("  PASS: exported text re-parses with zero InvalidLines");

    Console.WriteLine("  --- what the text carried (PRESERVED) ---");
    ok &= Check("species", src.Species, target.Species);
    ok &= Check("form", src.Form, target.Form);
    ok &= Check("level", src.CurrentLevel, target.CurrentLevel);
    ok &= Check("held item", src.HeldItem, target.HeldItem);
    ok &= Check("ability", src.Ability, target.Ability);
    ok &= Check("gender", src.Gender, target.Gender);
    ok &= Check("friendship", src.CurrentFriendship, target.CurrentFriendship);
    ok &= Check("shiny", src.IsShiny, target.IsShiny);
    ok &= Check("moves", string.Join(",", src.Move1, src.Move2, src.Move3, src.Move4),
                         string.Join(",", target.Move1, target.Move2, target.Move3, target.Move4));
    ok &= Check("IVs", string.Join(",", src.IV_HP, src.IV_ATK, src.IV_DEF, src.IV_SPA, src.IV_SPD, src.IV_SPE),
                       string.Join(",", target.IV_HP, target.IV_ATK, target.IV_DEF, target.IV_SPA, target.IV_SPD, target.IV_SPE));
    ok &= Check("EVs", string.Join(",", src.EV_HP, src.EV_ATK, src.EV_DEF, src.EV_SPA, src.EV_SPD, src.EV_SPE),
                       string.Join(",", target.EV_HP, target.EV_ATK, target.EV_DEF, target.EV_SPA, target.EV_SPD, target.EV_SPE));
    // Nature: on Format >= 8 the set carries StatAlignment (the Mint) and our SyncMintNature
    // makes Nature agree with it, so both must match the source's StatAlignment.
    ok &= Check("nature (StatAlignment)", src.StatAlignment, target.StatAlignment);
    if (target.Format >= 8)
        ok &= Check("nature (Nature, synced to Mint)", src.StatAlignment, target.Nature);

    // These are the honest losses. Reported, not asserted equal - the point is to record them.
    Console.WriteLine("  --- what the text could NOT carry (LOSSY BY DESIGN) ---");
    ReportLoss("PID", src.PID, target.PID);
    ReportLoss("EncryptionConstant", src.EncryptionConstant, target.EncryptionConstant);
    ReportLoss("OT name", src.OriginalTrainerName, target.OriginalTrainerName);
    ReportLoss("TID16", src.TID16, target.TID16);
    ReportLoss("SID16", src.SID16, target.SID16);
    ReportLoss("OT gender", src.OriginalTrainerGender, target.OriginalTrainerGender);
    ReportLoss("Ball", src.Ball, target.Ball);
    ReportLoss("MetLocation", src.MetLocation, target.MetLocation);
    ReportLoss("MetLevel", src.MetLevel, target.MetLevel);
    ReportLoss("Met date", $"{src.MetYear}-{src.MetMonth}-{src.MetDay}", $"{target.MetYear}-{target.MetMonth}-{target.MetDay}");
    ReportLoss("Version (origin game)", src.Version, target.Version);
    ReportLoss("Language", src.Language, target.Language);
    ReportLoss("EggLocation", src.EggLocation, target.EggLocation);

    ok &= AssertFileUntouched(path, snapshot);
    Console.WriteLine();
    return ok;
}

// ============================================================================================
// Case 3 - cross-generation .pkX import. Whatever the answer is, the requirement is the same:
// an accepted import must produce a storable entity of the destination's own type, and a refused
// import must come back with a non-empty human-readable reason and NOT a silently-mangled entity.
// ============================================================================================
static bool TestCrossGenImport(string sourceSavePath, string destSavePath, string label)
{
    Console.WriteLine($"=== {label} ===");
    if (!File.Exists(sourceSavePath) || !File.Exists(destSavePath)) { Fail("save file(s) not found"); return false; }

    var srcSnap = File.ReadAllBytes(sourceSavePath);
    var dstSnap = File.ReadAllBytes(destSavePath);
    var srcSav = SaveUtil.GetSaveFile((byte[])srcSnap.Clone());
    var dstSav = SaveUtil.GetSaveFile((byte[])dstSnap.Clone());
    if (srcSav is null || dstSav is null) { Fail("save(s) not recognized"); return false; }

    var donor = srcSav.PartyData[0];
    var file = EntityTransferService.ExportEntity(donor);
    Console.WriteLine($"  donor     : {donor.GetType().Name} #{donor.Species} '{donor.Nickname}' Lv{donor.CurrentLevel} ({file.Data.Length} bytes)");
    Console.WriteLine($"  target fmt: {dstSav.PKMType.Name}");

    var result = EntityTransferService.ImportEntity(file.Data, dstSav);
    var ok = true;

    if (result.Success)
    {
        Console.WriteLine($"  ACCEPTED  : {result.Message}");
        if (result.Entity is null)
            return Fail("Success reported but Entity is null");
        if (result.Entity.GetType() != dstSav.PKMType)
            ok &= Fail($"accepted entity is {result.Entity.GetType().Name}, but this save stores {dstSav.PKMType.Name}");
        else
            Console.WriteLine($"  PASS: result is a {result.Entity.GetType().Name}, storable in the destination save");
        if (result.Entity.Species == 0)
            ok &= Fail("accepted entity has species 0");
        if (!result.Converted && donor.GetType() != dstSav.PKMType)
            ok &= Fail("cross-format import did not report Converted=true");

        // An accepted cross-gen entity must survive an actual write into the destination save.
        ok &= AssertPartyWriteSucceeds(dstSav, result.Entity);
    }
    else
    {
        Console.WriteLine($"  REFUSED   : {result.Message}");
        if (string.IsNullOrWhiteSpace(result.Message))
            ok &= Fail("refusal carried no reason - the user would see nothing");
        else
            Console.WriteLine("  PASS: refusal carries a human-readable reason (surfaced, not swallowed)");
        if (result.Entity is not null)
            ok &= Fail("refusal returned a non-null entity - caller could store a bad mon by accident");
        else
            Console.WriteLine("  PASS: refusal returns no entity, so nothing can be silently stored");
    }

    ok &= AssertFileUntouched(sourceSavePath, srcSnap);
    ok &= AssertFileUntouched(destSavePath, dstSnap);
    Console.WriteLine();
    return ok;
}

// A file that is the right size but is not an entity must be refused with a reason, not crash.
static bool TestImportGarbageRefused(string destSavePath)
{
    Console.WriteLine("=== Garbage / wrong-size files must be refused with a reason, never throw ===");
    var dstSnap = File.ReadAllBytes(destSavePath);
    var sav = SaveUtil.GetSaveFile((byte[])dstSnap.Clone());
    if (sav is null) { Fail("save not recognized"); return false; }
    var ok = true;

    foreach (var (name, bytes) in new (string, byte[])[]
    {
        ("empty file", []),
        ("17 bytes (implausible size)", new byte[17]),
        ("344 all-zero bytes (plausible size, empty slot)", new byte[344]),
        ("2 MB of 0xFF (a save file, not an entity)", CreateFilled(2 * 1024 * 1024, 0xFF)),
    })
    {
        EntityImportResult r;
        try
        {
            r = EntityTransferService.ImportEntity(bytes, sav);
        }
        catch (Exception ex)
        {
            ok &= Fail($"{name}: threw {ex.GetType().Name} instead of returning a refusal");
            continue;
        }

        if (r.Success)
            ok &= Fail($"{name}: was ACCEPTED - it should not have been");
        else if (string.IsNullOrWhiteSpace(r.Message))
            ok &= Fail($"{name}: refused with no reason");
        else
            Console.WriteLine($"  PASS: {name} -> refused: \"{r.Message}\"");
    }
    Console.WriteLine();
    return ok;
}

static byte[] CreateFilled(int length, byte value)
{
    var b = new byte[length];
    Array.Fill(b, value);
    return b;
}

// ============================================================================================
// Case 4 - the real thing: apply a Showdown set onto a live party mon, write it back, reload from
// the written save, and assert the party stat block, level, PP and nature/Mint are all correct.
// This is where bug classes A-D would show up.
// ============================================================================================
static bool TestShowdownImportIntoParty(string path, string label, string speciesName, byte level, int minFormat)
{
    Console.WriteLine($"=== {label}: apply a '{speciesName}' Showdown set onto party slot 0, save, reload ===");
    if (!File.Exists(path)) { Fail($"file not found: {path}"); return false; }

    var snapshot = File.ReadAllBytes(path);
    var sav = SaveUtil.GetSaveFile((byte[])snapshot.Clone());
    if (sav is null) { Fail("not recognized as a save"); return false; }

    var pk = sav.PartyData[0];
    var ok = true;

    var beforeSpecies = pk.Species;
    var beforeLevel = pk.CurrentLevel;
    var beforeHp = pk.Stat_HPMax;
    var beforeAtk = pk.Stat_ATK;
    Console.WriteLine($"  before    : #{beforeSpecies} Lv{beforeLevel} HPMax={beforeHp} ATK={beforeAtk} " +
                      $"Nature={pk.Nature} Mint={pk.StatAlignment} moves={pk.Move1}/{pk.Move2}/{pk.Move3}/{pk.Move4}");

    // A deliberately ordinary set. Level is spelled out because bug class B (species-before-level)
    // only shows up when the new species' growth rate differs from the old one's.
    // Dragon Claw (#337) is deliberately in here: it does not exist before Gen3, so on an older
    // save MoveApplicator.SetMoves zeroes it and compacts the list. That is the exact condition
    // that exposed the ApplySetDetails PP-misalignment bug (see EntityTransferService.RepairMovePP)
    // - keep it, it is the regression test.
    var text = $"""
                {speciesName} @ Leftovers
                Ability: Levitate
                Level: {level}
                Adamant Nature
                EVs: 252 Atk / 4 HP / 252 Spe
                IVs: 30 Atk / 31 HP
                - Earthquake
                - Dragon Claw
                - Swords Dance
                - Substitute
                """;

    var import = EntityTransferService.ImportShowdown(text, pk);
    Console.WriteLine($"  import    : Applied={import.Applied} \"{import.Message}\"");
    foreach (var e in import.ParseErrors)
        Console.WriteLine($"              parse note: {e}");

    if (!import.Applied)
    {
        // A refusal here is a legitimate outcome for a save whose generation predates the species;
        // it just has to be reported, which the line above proves.
        if (string.IsNullOrWhiteSpace(import.Message))
            ok &= Fail("refusal carried no reason");
        else
            Console.WriteLine("  PASS: refusal is reported to the caller with a reason");
        ok &= AssertFileUntouched(path, snapshot);
        Console.WriteLine();
        return ok;
    }

    // --- Bug class A: party stat block staleness -------------------------------------------
    // ApplySetDetails ends with ResetPartyStats() (CommonEdits.cs:269). Verify empirically that
    // it actually took effect, rather than trusting the source read.
    if (pk.Stat_HPMax == beforeHp && pk.Stat_ATK == beforeAtk && pk.Species != beforeSpecies)
        ok &= Fail($"A: party stat block did NOT move after a species change (HPMax still {beforeHp}, ATK still {beforeAtk}) - stale stats");
    else
        Console.WriteLine($"  PASS (A) : stat block recomputed: HPMax {beforeHp} -> {pk.Stat_HPMax}, ATK {beforeAtk} -> {pk.Stat_ATK}");

    if (pk.Stat_Level != pk.CurrentLevel)
        ok &= Fail($"A: Stat_Level ({pk.Stat_Level}) out of sync with CurrentLevel ({pk.CurrentLevel})");

    // Independently recompute what the stats SHOULD be, so we are not just asserting "it changed".
    var expected = (PKM)pk.Clone();
    expected.ResetPartyStats();
    if (expected.Stat_HPMax != pk.Stat_HPMax || expected.Stat_ATK != pk.Stat_ATK || expected.Stat_SPE != pk.Stat_SPE)
        ok &= Fail($"A: stat block is not what a fresh ResetPartyStats produces " +
                   $"(got HP={pk.Stat_HPMax}/ATK={pk.Stat_ATK}/SPE={pk.Stat_SPE}, " +
                   $"expected HP={expected.Stat_HPMax}/ATK={expected.Stat_ATK}/SPE={expected.Stat_SPE})");
    else
        Console.WriteLine($"  PASS (A) : stat block matches an independent ResetPartyStats recomputation exactly");

    // --- Bug class B: species-before-level ---------------------------------------------------
    if (pk.CurrentLevel != level)
        ok &= Fail($"B: level is {pk.CurrentLevel}, expected {level} - EXP was interpreted with the wrong growth rate " +
                   "(species must be written before level)");
    else
        Console.WriteLine($"  PASS (B) : level survived the species change: Lv{pk.CurrentLevel} " +
                          $"(EXP {pk.EXP} under growth rate {pk.PersonalInfo.EXPGrowth})");

    // --- Bug class C: move PP -----------------------------------------------------------------
    ReadOnlySpan<ushort> gotMoves = [pk.Move1, pk.Move2, pk.Move3, pk.Move4];
    ReadOnlySpan<int> gotPP = [pk.Move1_PP, pk.Move2_PP, pk.Move3_PP, pk.Move4_PP];
    ReadOnlySpan<int> gotPPUps = [pk.Move1_PPUps, pk.Move2_PPUps, pk.Move3_PPUps, pk.Move4_PPUps];
    var ppOk = true;
    for (int i = 0; i < 4; i++)
    {
        var expectedPP = pk.GetMovePP(gotMoves[i], gotPPUps[i]);
        if (gotPP[i] != expectedPP)
            ppOk &= Fail($"C: move slot {i + 1} (move {gotMoves[i]}) has PP {gotPP[i]}, expected {expectedPP} - stale/misaligned PP");
        if (gotMoves[i] == 0 && gotPPUps[i] != 0)
            ppOk &= Fail($"C: empty move slot {i + 1} still carries {gotPPUps[i]} PP-Up(s)");
    }
    // Moves must also be compacted - no empty slot before a filled one.
    for (int i = 1; i < 4; i++)
    {
        if (gotMoves[i] != 0 && gotMoves[i - 1] == 0)
            ppOk &= Fail($"C: move slot {i} is empty but slot {i + 1} is filled - moveset was not compacted");
    }
    ok &= ppOk;
    if (ppOk)
        Console.WriteLine($"  PASS (C) : moves {string.Join("/", gotMoves.ToArray())} " +
                          $"with PP {string.Join("/", gotPP.ToArray())} - each PP matches its own slot's move");

    // --- Bug class D: Gen8+ Mint vs Nature ----------------------------------------------------
    if (pk.Format >= 8)
    {
        if (pk.StatAlignment != Nature.Adamant)
            ok &= Fail($"D: StatAlignment (Mint) is {pk.StatAlignment}, expected Adamant - stats are computed from this");
        else if (pk.Nature != Nature.Adamant)
            ok &= Fail($"D: stats follow Adamant but the displayed Nature is {pk.Nature} - " +
                       "SyncMintNature did not run, user sees a Nature/Mint mismatch");
        else
            Console.WriteLine($"  PASS (D) : Nature and Mint agree ({pk.Nature}); stats are computed from StatAlignment");

        // Prove the Adamant boost is real, not just a stored byte: Adamant is +Atk / -SpA.
        var neutral = (PKM)pk.Clone();
        neutral.StatAlignment = Nature.Hardy;
        neutral.ResetPartyStats();
        if (pk.Stat_ATK <= neutral.Stat_ATK || pk.Stat_SPA >= neutral.Stat_SPA)
            ok &= Fail($"D: Adamant did not actually modify the stat block " +
                       $"(ATK {neutral.Stat_ATK}->{pk.Stat_ATK}, SPA {neutral.Stat_SPA}->{pk.Stat_SPA})");
        else
            Console.WriteLine($"  PASS (D) : Adamant boost is in the real stat values " +
                              $"(ATK {neutral.Stat_ATK}->{pk.Stat_ATK}, SPA {neutral.Stat_SPA}->{pk.Stat_SPA})");
    }
    else if (minFormat >= 8)
    {
        ok &= Fail("D: expected Format >= 8 for this save");
    }
    else
    {
        Console.WriteLine($"  n/a  (D) : Format {pk.Format} has no Mint byte; Nature={pk.Nature}");
    }

    // --- Write back into the party slot, save, and reload from the written bytes ---------------
    sav.SetPartySlotAtIndex(pk, 0);
    var written = sav.Write().ToArray();
    var reloaded = SaveUtil.GetSaveFile((byte[])written.Clone());
    if (reloaded is null)
        return Fail("the save we wrote is no longer recognized as a save");

    var rp = reloaded.PartyData[0];
    ok &= Check("reloaded species", pk.Species, rp.Species);
    ok &= Check("reloaded level", pk.CurrentLevel, rp.CurrentLevel);
    ok &= Check("reloaded HPMax", pk.Stat_HPMax, rp.Stat_HPMax);
    ok &= Check("reloaded ATK", pk.Stat_ATK, rp.Stat_ATK);
    ok &= Check("reloaded SPE", pk.Stat_SPE, rp.Stat_SPE);
    ok &= Check("reloaded moves", string.Join(",", pk.Move1, pk.Move2, pk.Move3, pk.Move4),
                                  string.Join(",", rp.Move1, rp.Move2, rp.Move3, rp.Move4));
    ok &= Check("reloaded nature", pk.Nature, rp.Nature);
    if (pk.Format >= 8)
        ok &= Check("reloaded Mint", pk.StatAlignment, rp.StatAlignment);

    // Legality is REPORTED, never acted on - no auto-legalization anywhere in this project.
    var la = new LegalityAnalysis(rp);
    Console.WriteLine($"  legality  : {(la.Valid ? "Valid" : "INVALID")} (reported only - nothing here legalizes or auto-fixes)");

    ok &= AssertFileUntouched(path, snapshot);
    Console.WriteLine();
    return ok;
}

// A cross-gen entity that claimed to be storable must actually store without throwing.
static bool AssertPartyWriteSucceeds(SaveFile sav, PKM pk)
{
    try
    {
        var copy = (PKM)pk.Clone();

        // A freshly-imported .pkX entity has NO party stat block - the file does not carry one.
        // Assert that before the write, so this test proves WriteIntoPartySlot's ResetPartyStats
        // is actually necessary rather than merely present.
        var hadStatsBefore = copy.Stat_HPMax != 0;

        // Go through the production helper, not a hand-rolled equivalent.
        EntityTransferService.WriteIntoPartySlot(sav, copy, 0);

        if (copy.Stat_HPMax == 0)
            return Fail("party stat block is still empty after WriteIntoPartySlot");
        Console.WriteLine($"  PASS: party stat block computed on import " +
                          $"(HPMax {(hadStatsBefore ? "was non-zero in the file" : "absent from the file")} -> {copy.Stat_HPMax})");
        if (copy.Stat_Level != copy.CurrentLevel)
            return Fail($"Stat_Level {copy.Stat_Level} out of sync with CurrentLevel {copy.CurrentLevel}");

        var bytes = sav.Write().ToArray();
        var reloaded = SaveUtil.GetSaveFile((byte[])bytes.Clone());
        if (reloaded is null)
            return Fail("save written after a cross-gen import is no longer readable");
        var rp = reloaded.PartyData[0];
        if (rp.Species != copy.Species)
            return Fail($"cross-gen import did not survive the save round trip (#{copy.Species} -> #{rp.Species})");
        if (rp.Stat_HPMax == 0)
            return Fail("cross-gen import stored with an empty party stat block");
        Console.WriteLine($"  PASS: stored into party slot 0 and survived save->reload " +
                          $"(#{rp.Species} Lv{rp.CurrentLevel} HPMax={rp.Stat_HPMax})");
        return true;
    }
    catch (Exception ex)
    {
        return Fail($"storing the converted entity threw {ex.GetType().Name}: {ex.Message}");
    }
}

// ============================================================================================
// Helpers
// ============================================================================================
static bool AssertFileUntouched(string path, byte[] snapshot)
{
    var now = File.ReadAllBytes(path);
    if (!now.AsSpan().SequenceEqual(snapshot))
        return Fail($"THE ORIGINAL FILE ON DISK WAS MODIFIED: {path}");
    Console.WriteLine($"  PASS: original file untouched on disk ({Path.GetFileName(path)}, {snapshot.Length} bytes)");
    return true;
}

static bool Check<T>(string what, T expected, T actual)
{
    if (EqualityComparer<T>.Default.Equals(expected, actual))
    {
        Console.WriteLine($"    ok   {what}: {actual}");
        return true;
    }
    return Fail($"{what}: expected {expected}, got {actual}");
}

static void ReportLoss<T>(string what, T source, T imported)
{
    var same = EqualityComparer<T>.Default.Equals(source, imported);
    Console.WriteLine($"    {(same ? "coincidentally equal" : "LOST"),-20} {what}: source={source} -> imported={imported}");
}

static bool Fail(string message)
{
    Console.WriteLine($"  FAIL: {message}");
    return false;
}
