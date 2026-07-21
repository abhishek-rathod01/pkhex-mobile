using PKHeX.Core;

// Replicates the app's planned Form/Nature/Ability edit -> export -> reload path (the same
// pattern the "Species + move editing" harness used, extended to these three fields) against the
// real Gen1/Gen5/Gen9 saves this project has used throughout.
//
// The premise this harness exists to check, per the task: Nature/Ability are "PID-derived on
// Gen3-5" in general folklore, but PKHeX.Core's *stored format* (PK5, PK9, etc.) may or may not
// actually re-derive them from PID on every read/write. Source reading
// (vendor/PKHeX.Core/PKM/Shared/G3PKM.cs, G4PKM.cs, PK5.cs, PK9.cs) already shows:
//   - Gen1/2 (GBPKM.cs): Nature getter always 0, Ability getter always -1, both setters no-ops
//     (sentinel values - the format has no such concept at all). Form setter is a real, working
//     side-effecting setter ONLY for Unown (rejection-samples DV16 until the Unown form matches);
//     a no-op for every other species. Gen1 can never have Unown (introduced Gen2), so Form is
//     unconditionally a no-op there.
//   - Gen3 (G3PKM.cs): same shape - Nature/Ability setters are no-ops (PID % 25 / PersonalInfo
//     lookup via AbilityBit), Form is a real setter only for Unown (rejection-samples PID).
//   - Gen4 (G4PKM.cs + PK4.cs/BK4.cs/RK4.cs): Nature setter is STILL a no-op (PID % 25), but
//     Ability and Form are independently stored bytes with real, working setters - contradicts
//     the "PID-derived on Gen3-5" folklore for two of the three fields.
//   - Gen5 (PK5.cs): Nature, Ability, AND Form are all independently stored bytes (Data[0x41],
//     Data[0x15], Data[0x40]) with real, working setters. Only AbilityNumber (the 0/1/H slot
//     indicator) is still PID-derived. This is the case that most contradicts the task's stated
//     premise and is why this harness verifies it against a real save rather than assuming.
//   - Gen6+ (PK6/PK7/G8PKM/PA8/PA9/PK9): all three fields are independently stored with real
//     setters, same shape as Gen5.
//
// This harness proves the ROUND-TRIP (not just the in-memory setter) for the three generations
// the task calls out (Gen1, Gen5, Gen9), and specifically checks the party stat-block
// consequences: Nature and Form both feed PersonalInfo/StatAlignment-based stat computation
// (PKM.LoadStats -> StatAlignment.ModifyStatsForAlignment), so a Nature or Form edit must trigger
// the same ResetPartyStats() treatment species/level edits already needed (see PROGRESS.md
// "Species + move editing" - party stat block never recomputed on edit). Ability does not affect
// the stat block (battle-time only) so no stat assertion is made for it.

var root = @"C:\Users\abhis\Downloads\sav files pkmn";
var allPass = true;

Console.WriteLine("=== Gen1 (Red, real save): Nature/Ability/Form are sentinel no-ops ===");
allPass &= TestGen1NoOp(Path.Combine(root, "POKEMON RED-0.sav"), 0);
Console.WriteLine();

Console.WriteLine("=== Gen5 (Black, real save): Nature/Ability edit, in-place (no species/form change) ===");
allPass &= TestNatureAbilityRoundTrip(Path.Combine(root, "Pokemon Black Version.sav"), 0, "Gen5");
Console.WriteLine();

Console.WriteLine("=== Gen5 (Black, real save): Form edit via species switch to Giratina (Altered/Origin, real base-stat diff) ===");
allPass &= TestFormRoundTrip(Path.Combine(root, "Pokemon Black Version.sav"), 0, (ushort)Species.Giratina, 1, "Gen5");
Console.WriteLine();

Console.WriteLine("=== Gen9 (Scarlet, real save): Nature/Ability edit, in-place (no species/form change) ===");
allPass &= TestNatureAbilityRoundTrip(Path.Combine(root, @"pkmnscarlet_100\main"), 0, "Gen9");
Console.WriteLine();

Console.WriteLine("=== Gen9 (Scarlet, real save): Form edit via species switch to Zygarde (50%/Complete, real base-stat diff) ===");
allPass &= TestFormRoundTrip(Path.Combine(root, @"pkmnscarlet_100\main"), 0, (ushort)Species.Zygarde, 2, "Gen9");
Console.WriteLine();

// --- Bonus cases: the task only requires Gen1/5/9, but Gen3 and Gen4 real saves happen to be
// available in this project's fixed asset folder, and the source-reading claim for those two gens
// (Nature is always a PID-derived no-op; Ability/Form only become real independent fields starting
// Gen4) is exactly the kind of thing this project has been burned by assuming without a save-level
// check before (see PROGRESS.md Gen1/2 IV/EV and Gen3 Nature/Ability/Gender findings). Cheap to
// confirm, so confirmed rather than left as source-only conjecture.
Console.WriteLine("=== BONUS (not required by task, real saves available): Gen3 (Emerald) - Nature/Ability/Form all no-op ===");
allPass &= TestGen3NoOp(Path.Combine(root, "pokeemerald (2).sav"), 0);
Console.WriteLine();

Console.WriteLine("=== BONUS (not required by task, real saves available): Gen4 (HeartGold) - Nature no-op, Ability+Form real ===");
allPass &= TestGen4Boundary(Path.Combine(root, "Pokemon Heart Gold Version.sav"), 0);
Console.WriteLine();

Console.WriteLine(allPass ? "=== ALL CASES PASS ===" : "=== ONE OR MORE CASES FAILED ===");
return allPass ? 0 : 1;

// --- Gen1: confirm Nature/Ability/Form are unconditional no-op sentinels, and that attempting to
// set them anyway does not crash Write()/reload and does not silently corrupt anything else. ---
static bool TestGen1NoOp(string path, int slot)
{
    if (!File.Exists(path)) { Console.WriteLine($"  FAIL: file not found: {path}"); return false; }

    var originalBytes = File.ReadAllBytes(path);
    var originalSnapshot = (byte[])originalBytes.Clone();
    var sav = SaveUtil.GetSaveFile(originalBytes);
    if (sav is null) { Console.WriteLine("  FAIL: original not recognized as a save"); return false; }
    var pk = sav.PartyData[slot];

    var beforeNature = pk.Nature;
    var beforeAbility = pk.Ability;
    var beforeForm = pk.Form;
    var beforeHpMax = pk.Stat_HPMax;
    Console.WriteLine($"  Before: Species={pk.Species} Nature={beforeNature} Ability={beforeAbility} Form={beforeForm}");

    // Attempt edits exactly like the app's pickers would apply them.
    pk.Nature = beforeNature == Nature.Adamant ? Nature.Modest : Nature.Adamant;
    pk.Ability = beforeAbility == 5 ? 6 : 5;
    pk.Form = 5; // Mew is never Unown, so this must be a no-op regardless of value chosen

    bool natureNoOpInMemory = pk.Nature == beforeNature;
    bool abilityNoOpInMemory = pk.Ability == beforeAbility;
    bool formNoOpInMemory = pk.Form == beforeForm;
    Console.WriteLine($"  After set (pre-export): Nature={pk.Nature} Ability={pk.Ability} Form={pk.Form}");
    if (!natureNoOpInMemory) Console.WriteLine("  NOTE: Nature setter was NOT a no-op on Gen1 - unexpected, re-check GBPKM.cs.");
    if (!abilityNoOpInMemory) Console.WriteLine("  NOTE: Ability setter was NOT a no-op on Gen1 - unexpected, re-check GBPKM.cs.");
    if (!formNoOpInMemory) Console.WriteLine("  NOTE: Form setter was NOT a no-op on Gen1 for a non-Unown species - unexpected.");

    sav.SetPartySlotAtIndex(pk, slot);
    byte[] exported;
    try { exported = sav.Write().ToArray(); }
    catch (Exception ex) { Console.WriteLine($"  FAIL: sav.Write() threw {ex.GetType().Name}: {ex.Message}"); return false; }

    var reloaded = SaveUtil.GetSaveFile(exported);
    if (reloaded is null) { Console.WriteLine("  FAIL: exported bytes not recognized on reload"); return false; }
    var pk2 = reloaded.PartyData[slot];
    Console.WriteLine($"  After reload: Nature={pk2.Nature} Ability={pk2.Ability} Form={pk2.Form} Stat_HPMax={pk2.Stat_HPMax}");

    bool ok = pk2.Nature == beforeNature && pk2.Ability == beforeAbility && pk2.Form == beforeForm
              && pk2.Stat_HPMax == beforeHpMax; // stat block must be untouched - nothing stat-affecting changed
    Console.WriteLine(ok
        ? "  PASS: Nature/Ability/Form are confirmed unconditional sentinel no-ops on Gen1; no crash, no side effects."
        : "  CASE FAILED (see above).");

    var untouched = File.ReadAllBytes(path).AsSpan().SequenceEqual(originalSnapshot);
    Console.WriteLine(untouched ? "  Original file on disk: untouched (confirmed)." : "  WARNING: original file on disk changed!");
    return ok && untouched;
}

// --- Nature + Ability edit, no species/form change: proves both are independently stored and
// round-trip through export/reload on Gen5+, and that a Nature change alone makes ResetPartyStats
// produce a different stat block (proof the stat-block consequence is real, not just a stored
// byte that nothing reads). ---
static bool TestNatureAbilityRoundTrip(string path, int slot, string label)
{
    if (!File.Exists(path)) { Console.WriteLine($"  FAIL: file not found: {path}"); return false; }

    var originalBytes = File.ReadAllBytes(path);
    var originalSnapshot = (byte[])originalBytes.Clone();
    var sav = SaveUtil.GetSaveFile(originalBytes);
    if (sav is null) { Console.WriteLine("  FAIL: original not recognized as a save"); return false; }
    var pk = sav.PartyData[slot];

    var origNature = pk.Nature;
    var origAbility = pk.Ability;
    var origAtk = pk.Stat_ATK;
    var origSpa = pk.Stat_SPA;
    Console.WriteLine($"  Before: Species={pk.Species}({Name(GameInfo.Strings.Species, pk.Species)}) Nature={Name(GameInfo.Strings.Natures, (int)origNature)} Ability={Name(GameInfo.Strings.Ability, origAbility)} Stat_ATK={origAtk} Stat_SPA={origSpa}");

    // Pick a nature that swaps the Atk/SpA modifier relationship from whatever it currently is, so
    // the stat-block assertion below can't pass by coincidence. Adamant (+Atk/-SpA) vs Modest
    // (+SpA/-Atk) are opposite; fall back between them based on the current value.
    var newNature = origNature == Nature.Adamant ? Nature.Modest : Nature.Adamant;

    // Pick a different valid ability for this species from its own PersonalInfo (not an arbitrary
    // ID), so the edit is a realistic one a user would make via the picker.
    var pi = pk.PersonalInfo;
    int newAbility = origAbility;
    for (int i = 0; i < pi.AbilityCount; i++)
    {
        var candidate = pi.GetAbilityAtIndex(i);
        if (candidate != origAbility && candidate != 0) { newAbility = candidate; break; }
    }
    if (newAbility == origAbility)
        Console.WriteLine("  NOTE: species has only one distinct ability slot - Ability edit will be a same-value round-trip, not a changed one.");

    pk.Nature = newNature;
    // Gen8+ (G8PKM/PA8/PA9/PK9) stores a SECOND independent nature-shaped byte, StatAlignment (the
    // "Mint" mechanic) - PKM.LoadStats/ResetPartyStats reads StatAlignment for the stat-boost
    // calculation, NOT Nature. PKHeX.Core's own CommonEdits.SetNature() only touches StatAlignment
    // on format>=8 (intentionally leaving Nature as the "pre-mint" original, a legality-relevant
    // distinction its full editor exposes via a separate Mint dropdown). This app has one Nature
    // field, not two, so both bytes are synced together - a user picking "Adamant" expects the
    // displayed Nature AND the stat block to both reflect Adamant, not a legal-but-confusing
    // Mint-vs-original mismatch. Discovered empirically: the first version of this harness set only
    // Nature and the Gen9 stat-block assertion below failed (stats didn't move at all).
    if (pk.Format >= 8)
        pk.StatAlignment = newNature;
    pk.Ability = newAbility;

    bool natureAppliedInMemory = pk.Nature == newNature;
    bool abilityAppliedInMemory = pk.Ability == newAbility;
    Console.WriteLine($"  After set (pre-export): Nature={pk.Nature} StatAlignment={pk.StatAlignment} Ability={pk.Ability} (in-memory getter reflects the new value: Nature={natureAppliedInMemory}, Ability={abilityAppliedInMemory})");

    // Mirror the app: a Nature change makes the party stat block stale (StatAlignment feeds
    // PKM.LoadStats), so it must be recomputed - same treatment species/level/IV/EV edits get.
    pk.ResetPartyStats();
    var memAtk = pk.Stat_ATK;
    var memSpa = pk.Stat_SPA;
    Console.WriteLine($"  After ResetPartyStats: Stat_ATK={memAtk} Stat_SPA={memSpa}");

    sav.SetPartySlotAtIndex(pk, slot);
    byte[] exported;
    try { exported = sav.Write().ToArray(); }
    catch (Exception ex) { Console.WriteLine($"  FAIL: sav.Write() threw {ex.GetType().Name}: {ex.Message}"); return false; }

    var reloaded = SaveUtil.GetSaveFile(exported);
    if (reloaded is null) { Console.WriteLine("  FAIL: exported bytes not recognized on reload"); return false; }
    var pk2 = reloaded.PartyData[slot];
    Console.WriteLine($"  After reload: Nature={pk2.Nature} Ability={pk2.Ability} Stat_ATK={pk2.Stat_ATK} Stat_SPA={pk2.Stat_SPA}");

    bool natureOk = pk2.Nature == newNature;
    if (!natureOk) Console.WriteLine($"  FAIL (round-trip): Nature expected {newNature}, got {pk2.Nature}.");
    bool abilityOk = pk2.Ability == newAbility;
    if (!abilityOk) Console.WriteLine($"  FAIL (round-trip): Ability expected {newAbility}, got {pk2.Ability}.");
    bool statRoundTripOk = pk2.Stat_ATK == memAtk && pk2.Stat_SPA == memSpa;
    if (!statRoundTripOk) Console.WriteLine($"  FAIL (stat round-trip): expected ATK={memAtk}/SPA={memSpa}, got ATK={pk2.Stat_ATK}/SPA={pk2.Stat_SPA}.");
    // The whole point of the ResetPartyStats gate: a Nature-only edit must actually move the stat
    // block, not leave it stale at the pre-edit values (the exact bug class already found once).
    bool statChangedAsExpected = memAtk != origAtk || memSpa != origSpa;
    if (!statChangedAsExpected) Console.WriteLine($"  FAIL (stat recalc): Stat_ATK/Stat_SPA unchanged despite an opposite-direction Nature edit - stat block is stale.");

    bool caseOk = natureOk && abilityOk && statRoundTripOk && statChangedAsExpected;
    Console.WriteLine(caseOk
        ? $"  PASS ({label}): Nature and Ability are independently stored, round-trip correctly, and Nature's stat-block effect is real and recomputed correctly."
        : "  CASE FAILED (see above).");

    var untouched = File.ReadAllBytes(path).AsSpan().SequenceEqual(originalSnapshot);
    Console.WriteLine(untouched ? "  Original file on disk: untouched (confirmed)." : "  WARNING: original file on disk changed!");
    return caseOk && untouched;
}

// --- Form edit via a species switch to a form-rich family with genuinely different base stats
// per form (Giratina Altered/Origin, Zygarde 50%/Complete) - proves Form round-trips AND that its
// stat-block consequence (via PersonalInfo.GetFormEntry) is real and gets recomputed. ---
static bool TestFormRoundTrip(string path, int slot, ushort newSpecies, byte newForm, string label)
{
    if (!File.Exists(path)) { Console.WriteLine($"  FAIL: file not found: {path}"); return false; }

    var originalBytes = File.ReadAllBytes(path);
    var originalSnapshot = (byte[])originalBytes.Clone();
    var sav = SaveUtil.GetSaveFile(originalBytes);
    if (sav is null) { Console.WriteLine("  FAIL: original not recognized as a save"); return false; }
    var pk = sav.PartyData[slot];

    if (newSpecies > pk.MaxSpeciesID) { Console.WriteLine($"  FAIL: test species {newSpecies} > MaxSpeciesID {pk.MaxSpeciesID} for this format."); return false; }

    Console.WriteLine($"  Before: Species={pk.Species}({Name(GameInfo.Strings.Species, pk.Species)}) Form={pk.Form}");

    // Species BEFORE form (mirrors species-before-level ordering already established): PersonalInfo
    // is looked up by (Species, Form) together, so form-0 base stats are used first as an
    // intermediate step - harmless, but keep the same "species first" discipline as the existing
    // species/level ordering fix for consistency.
    pk.Species = newSpecies;
    pk.CurrentLevel = 50;
    pk.SetMoves(new ushort[] { 1, 2, 3, 4 }); // arbitrary valid moves so SetMoves' PP recalc doesn't error
    pk.ResetPartyStats();
    var baseFormAtk = pk.Stat_ATK;
    var baseFormDef = pk.Stat_DEF;
    Console.WriteLine($"  After species switch, Form=0: Stat_ATK={baseFormAtk} Stat_DEF={baseFormDef}");

    // Now apply the form edit exactly like the picker would.
    pk.Form = newForm;
    bool formAppliedInMemory = pk.Form == newForm;
    Console.WriteLine($"  After Form set (pre-export): Form={pk.Form} (in-memory getter reflects new value: {formAppliedInMemory})");

    // Mirror the app: a Form change makes the party stat block stale (PersonalInfo.GetFormEntry
    // differs per form for these species), so it must be recomputed.
    pk.ResetPartyStats();
    var memAtk = pk.Stat_ATK;
    var memDef = pk.Stat_DEF;
    Console.WriteLine($"  After ResetPartyStats (new form): Stat_ATK={memAtk} Stat_DEF={memDef}");

    sav.SetPartySlotAtIndex(pk, slot);
    byte[] exported;
    try { exported = sav.Write().ToArray(); }
    catch (Exception ex) { Console.WriteLine($"  FAIL: sav.Write() threw {ex.GetType().Name}: {ex.Message}"); return false; }

    var reloaded = SaveUtil.GetSaveFile(exported);
    if (reloaded is null) { Console.WriteLine("  FAIL: exported bytes not recognized on reload"); return false; }
    var pk2 = reloaded.PartyData[slot];
    Console.WriteLine($"  After reload: Species={pk2.Species} Form={pk2.Form} Stat_ATK={pk2.Stat_ATK} Stat_DEF={pk2.Stat_DEF}");

    bool formOk = pk2.Form == newForm;
    if (!formOk) Console.WriteLine($"  FAIL (round-trip): Form expected {newForm}, got {pk2.Form}.");
    bool statRoundTripOk = pk2.Stat_ATK == memAtk && pk2.Stat_DEF == memDef;
    if (!statRoundTripOk) Console.WriteLine($"  FAIL (stat round-trip): expected ATK={memAtk}/DEF={memDef}, got ATK={pk2.Stat_ATK}/DEF={pk2.Stat_DEF}.");
    bool statChangedAsExpected = memAtk != baseFormAtk || memDef != baseFormDef;
    if (!statChangedAsExpected) Console.WriteLine("  FAIL (stat recalc): stat block unchanged between Form 0 and the new form - expected a real base-stat difference for this species pair.");

    bool caseOk = formOk && statRoundTripOk && statChangedAsExpected;
    Console.WriteLine(caseOk
        ? $"  PASS ({label}): Form is independently stored, round-trips correctly, and its stat-block effect (via PersonalInfo.GetFormEntry) is real and recomputed correctly."
        : "  CASE FAILED (see above).");

    var untouched = File.ReadAllBytes(path).AsSpan().SequenceEqual(originalSnapshot);
    Console.WriteLine(untouched ? "  Original file on disk: untouched (confirmed)." : "  WARNING: original file on disk changed!");
    return caseOk && untouched;
}

// --- Bonus: Gen3 - confirm Nature/Ability/Form are all no-ops (same shape as Gen1/2), against a
// real save rather than source-reading alone. ---
static bool TestGen3NoOp(string path, int slot)
{
    if (!File.Exists(path)) { Console.WriteLine($"  FAIL: file not found: {path}"); return false; }

    var originalBytes = File.ReadAllBytes(path);
    var originalSnapshot = (byte[])originalBytes.Clone();
    var sav = SaveUtil.GetSaveFile(originalBytes);
    if (sav is null) { Console.WriteLine("  FAIL: original not recognized as a save"); return false; }
    if (sav.PartyCount <= slot) { Console.WriteLine($"  FAIL: slot {slot} out of range (PartyCount={sav.PartyCount})"); return false; }
    var pk = sav.PartyData[slot];

    var beforeNature = pk.Nature;
    var beforeAbility = pk.Ability;
    var beforeForm = pk.Form;
    var beforeHpMax = pk.Stat_HPMax;
    Console.WriteLine($"  Before: Species={pk.Species}({Name(GameInfo.Strings.Species, pk.Species)}) Nature={beforeNature} Ability={beforeAbility} Form={beforeForm}");

    pk.Nature = beforeNature == Nature.Adamant ? Nature.Modest : Nature.Adamant;
    pk.Ability = beforeAbility <= 0 ? 5 : 0;
    pk.Form = (byte)(beforeForm + 1); // this species is not Unown, so must be a no-op

    bool noOpInMemory = pk.Nature == beforeNature && pk.Ability == beforeAbility && pk.Form == beforeForm;
    Console.WriteLine($"  After set (pre-export): Nature={pk.Nature} Ability={pk.Ability} Form={pk.Form} (all unchanged: {noOpInMemory})");

    sav.SetPartySlotAtIndex(pk, slot);
    byte[] exported;
    try { exported = sav.Write().ToArray(); }
    catch (Exception ex) { Console.WriteLine($"  FAIL: sav.Write() threw {ex.GetType().Name}: {ex.Message}"); return false; }

    var reloaded = SaveUtil.GetSaveFile(exported);
    if (reloaded is null) { Console.WriteLine("  FAIL: exported bytes not recognized on reload"); return false; }
    var pk2 = reloaded.PartyData[slot];
    Console.WriteLine($"  After reload: Nature={pk2.Nature} Ability={pk2.Ability} Form={pk2.Form} Stat_HPMax={pk2.Stat_HPMax}");

    bool ok = pk2.Nature == beforeNature && pk2.Ability == beforeAbility && pk2.Form == beforeForm && pk2.Stat_HPMax == beforeHpMax;
    Console.WriteLine(ok
        ? "  PASS: Nature/Ability/Form confirmed no-op on Gen3 against a real save (matches G3PKM.cs source reading)."
        : "  CASE FAILED (see above).");

    var untouched = File.ReadAllBytes(path).AsSpan().SequenceEqual(originalSnapshot);
    Console.WriteLine(untouched ? "  Original file on disk: untouched (confirmed)." : "  WARNING: original file on disk changed!");
    return ok && untouched;
}

// --- Bonus: Gen4 - confirm Nature is a no-op but Ability and Form are real, independently stored,
// round-tripping fields (per PK4.cs/G4PKM.cs source reading) against a real save. ---
static bool TestGen4Boundary(string path, int slot)
{
    if (!File.Exists(path)) { Console.WriteLine($"  FAIL: file not found: {path}"); return false; }

    var originalBytes = File.ReadAllBytes(path);
    var originalSnapshot = (byte[])originalBytes.Clone();
    var sav = SaveUtil.GetSaveFile(originalBytes);
    if (sav is null) { Console.WriteLine("  FAIL: original not recognized as a save"); return false; }
    if (sav.PartyCount <= slot) { Console.WriteLine($"  FAIL: slot {slot} out of range (PartyCount={sav.PartyCount})"); return false; }
    var pk = sav.PartyData[slot];

    var beforeNature = pk.Nature;
    var beforeAbility = pk.Ability;
    Console.WriteLine($"  Before: Species={pk.Species}({Name(GameInfo.Strings.Species, pk.Species)}) Nature={beforeNature} Ability={beforeAbility} Form={pk.Form}");

    var newNature = beforeNature == Nature.Adamant ? Nature.Modest : Nature.Adamant;
    int newAbility = beforeAbility <= 0 ? 1 : 0;
    pk.Nature = newNature;
    pk.Ability = newAbility;
    // Form: switch species to Giratina (available since Gen4/Platinum) to get a real base-stat
    // difference between forms, same probe as the Gen5/Gen9 Form cases above.
    if ((ushort)Species.Giratina <= pk.MaxSpeciesID)
    {
        pk.Species = (ushort)Species.Giratina;
        pk.CurrentLevel = 50;
    }
    pk.Form = 1;
    bool natureNoOpInMemory = pk.Nature == beforeNature;
    bool abilityAppliedInMemory = pk.Ability == newAbility;
    bool formAppliedInMemory = pk.Form == 1;
    Console.WriteLine($"  After set (pre-export): Nature={pk.Nature} (no-op: {natureNoOpInMemory}) Ability={pk.Ability} (applied: {abilityAppliedInMemory}) Form={pk.Form} (applied: {formAppliedInMemory})");

    pk.ResetPartyStats();
    sav.SetPartySlotAtIndex(pk, slot);
    byte[] exported;
    try { exported = sav.Write().ToArray(); }
    catch (Exception ex) { Console.WriteLine($"  FAIL: sav.Write() threw {ex.GetType().Name}: {ex.Message}"); return false; }

    var reloaded = SaveUtil.GetSaveFile(exported);
    if (reloaded is null) { Console.WriteLine("  FAIL: exported bytes not recognized on reload"); return false; }
    var pk2 = reloaded.PartyData[slot];
    Console.WriteLine($"  After reload: Species={pk2.Species} Nature={pk2.Nature} Ability={pk2.Ability} Form={pk2.Form} Stat_HPMax={pk2.Stat_HPMax}");

    bool natureStillNoOp = pk2.Nature == beforeNature;
    if (!natureStillNoOp) Console.WriteLine($"  FAIL: expected Nature to remain a no-op ({beforeNature}), got {pk2.Nature}.");
    bool abilityOk = pk2.Ability == newAbility;
    if (!abilityOk) Console.WriteLine($"  FAIL (round-trip): Ability expected {newAbility}, got {pk2.Ability}.");
    bool formOk = pk2.Form == 1;
    if (!formOk) Console.WriteLine($"  FAIL (round-trip): Form expected 1, got {pk2.Form}.");

    bool ok = natureStillNoOp && abilityOk && formOk;
    Console.WriteLine(ok
        ? "  PASS: Gen4 confirmed against a real save - Nature stays a no-op, Ability and Form are real and round-trip."
        : "  CASE FAILED (see above).");

    var untouched = File.ReadAllBytes(path).AsSpan().SequenceEqual(originalSnapshot);
    Console.WriteLine(untouched ? "  Original file on disk: untouched (confirmed)." : "  WARNING: original file on disk changed!");
    return ok && untouched;
}

static string Name(IReadOnlyList<string> list, int id) => id >= 0 && id < list.Count ? list[id] : $"#{id}";
