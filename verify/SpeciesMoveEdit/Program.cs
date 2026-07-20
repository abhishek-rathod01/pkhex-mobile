using PKHeX.Core;

// Replicates the app's exact species/move edit -> export -> reload path introduced in
// PokemonDetailPage.OnSaveChangesClicked:
//   pk.Species = newSpecies;   // format setter: updates internal index / types / catch rate etc.
//   pk.SetMoves(newMoves);     // NOT raw Move1..4 - recomputes current PP for the new moves
//   sav.SetPartySlotAtIndex(pk, slot);  // recomputes the party stat block for the new species
//   sav.Write();               // export bytes
//   SaveUtil.GetSaveFile(bytes) // reload (simulates re-picking the exported file)
//
// Actively probes the edge classes earlier sessions were bitten by:
//   * stale derived state (the move-PP analog of the level/stat-recalc gap)
//   * Gen1 internal-index + stored-type coupling to species
//   * party stat-block recalc after a species change
//   * clearing a move slot to (None)
//   * MaxSpeciesID/MaxMoveID structural bounds
//   * leftover Form value after a species change

var root = @"C:\Users\abhis\Downloads\sav files pkmn";

var cases = new Case[]
{
    // Gen1: full 4-move set + species change. Moves all <= MaxMoveID_1 (165).
    new("Gen1 (Red, real save)", "POKEMON RED-0.sav", 0,
        NewSpecies: (ushort)Species.Charizard,
        NewMoves: new ushort[] { 53, 19, 89, 163 }),   // Flamethrower, Fly, Earthquake, Slash
    // Gen5: species change + clearing two move slots to (None) - PP must go to 0, not stale.
    new("Gen5 (Black, real save)", "Pokemon Black Version.sav", 0,
        NewSpecies: (ushort)Species.Pikachu,
        NewMoves: new ushort[] { 85, 98, 0, 0 }),       // Thunderbolt, Quick Attack, (None), (None)
    // Gen9: species change to a form-bearing family + 4 moves - probes Form staleness.
    new("Gen9 (Scarlet, real save)", @"pkmnscarlet_100\main", 0,
        NewSpecies: (ushort)Species.Garchomp,
        NewMoves: new ushort[] { 89, 200, 57, 14 }),    // Earthquake, Outrage, Surf, Swords Dance
};

var allPass = true;

foreach (var c in cases)
{
    Console.WriteLine($"=== {c.Label} ===");
    var path = Path.Combine(root, c.RelPath);

    if (!File.Exists(path))
    {
        Console.WriteLine($"  FAIL: file not found: {path}");
        allPass = false;
        Console.WriteLine();
        continue;
    }

    var originalBytes = File.ReadAllBytes(path);
    var originalSnapshot = (byte[])originalBytes.Clone(); // see EditRoundTrip note: GetSaveFile wraps, no clone
    var sav = SaveUtil.GetSaveFile(originalBytes);
    if (sav is null) { Fail("original not recognized as a save"); continue; }
    if (c.Slot >= sav.PartyCount) { Fail($"slot {c.Slot} out of range (PartyCount={sav.PartyCount})"); continue; }

    var pk = sav.PartyData[c.Slot];

    Console.WriteLine($"  Format bounds: MaxSpeciesID={pk.MaxSpeciesID} MaxMoveID={pk.MaxMoveID}");

    // Structural bound sanity: the app's pickers never offer past these, so the harness must not
    // request past them either - assert the chosen edit is representable in this format.
    if (c.NewSpecies > pk.MaxSpeciesID) { Fail($"test species {c.NewSpecies} > MaxSpeciesID {pk.MaxSpeciesID}"); continue; }
    if (c.NewMoves.Any(m => m > pk.MaxMoveID)) { Fail($"a test move exceeds MaxMoveID {pk.MaxMoveID}"); continue; }

    var origSpecies = pk.Species;
    var origMoves = new[] { pk.Move1, pk.Move2, pk.Move3, pk.Move4 };
    var origPP = new[] { pk.Move1_PP, pk.Move2_PP, pk.Move3_PP, pk.Move4_PP };
    var origForm = pk.Form;
    var origHpMax = pk.Stat_HPMax;
    Console.WriteLine($"  Before: Species={origSpecies}({Name(GameInfo.Strings.Species, origSpecies)}) Form={origForm} Stat_HPMax={origHpMax}");
    Console.WriteLine($"  Before moves: {DescribeMoves(origMoves, origPP)}");

    // --- Apply exactly what the app does ---
    // Also change level, so the stat-block recompute is exercised for both a species change and a
    // level change at once (level 100 -> 50, or -> 51 if it was already 50).
    byte newLevel = pk.CurrentLevel == 50 ? (byte)51 : (byte)50;
    // Species BEFORE level (see PokemonDetailPage): level is stored as EXP, and EXP<->level depends
    // on the species' growth rate, so the species must be set first or the level is misinterpreted.
    pk.Species = c.NewSpecies;
    pk.CurrentLevel = newLevel;
    pk.SetMoves(c.NewMoves);
    // Mirror PokemonDetailPage.OnSaveChangesClicked: recompute the stat block because a species /
    // level change was made (SetPartySlotAtIndex will NOT, since stats are already present).
    pk.ResetPartyStats();

    // In-memory snapshot right after the setters (pre-export) - the correct round-trip baseline.
    // Stat block is already recomputed by ResetPartyStats above.
    var memMoves = new[] { pk.Move1, pk.Move2, pk.Move3, pk.Move4 };
    var memPP = new[] { pk.Move1_PP, pk.Move2_PP, pk.Move3_PP, pk.Move4_PP };
    var memPPUps = new[] { pk.Move1_PPUps, pk.Move2_PPUps, pk.Move3_PPUps, pk.Move4_PPUps };
    var memForm = pk.Form;
    var memHpMax = pk.Stat_HPMax;
    var memStatLevel = pk.Stat_Level;
    Console.WriteLine($"  After set (pre-export): Species={pk.Species}({Name(GameInfo.Strings.Species, pk.Species)}) Form={memForm} Level={newLevel} Stat_HPMax={memHpMax} Stat_Level={memStatLevel}");
    Console.WriteLine($"  After set moves: {DescribeMoves(memMoves, memPP)}");

    // PP-recalc proof: each non-zero new move's PP must equal that move's freshly-computed max PP
    // (given its PP-ups), not carry over the previous move's PP. A cleared slot must be 0.
    bool ppRecalcOk = true;
    for (int i = 0; i < 4; i++)
    {
        int expectedPP = memMoves[i] == 0 ? 0 : pk.GetMovePP(memMoves[i], memPPUps[i]);
        if (memPP[i] != expectedPP)
        {
            ppRecalcOk = false;
            Console.WriteLine($"  FAIL (PP recalc): move slot {i + 1} PP={memPP[i]}, expected {expectedPP} for move {memMoves[i]}.");
        }
    }

    sav.SetPartySlotAtIndex(pk, c.Slot);

    byte[] exported;
    try { exported = sav.Write().ToArray(); }
    catch (Exception ex) { Fail($"sav.Write() threw {ex.GetType().Name}: {ex.Message}"); continue; }

    var reloaded = SaveUtil.GetSaveFile(exported);
    if (reloaded is null) { Fail("exported bytes not recognized on reload"); continue; }
    if (c.Slot >= reloaded.PartyCount) { Fail($"reloaded slot out of range (PartyCount={reloaded.PartyCount})"); continue; }

    var pk2 = reloaded.PartyData[c.Slot];
    var rMoves = new[] { pk2.Move1, pk2.Move2, pk2.Move3, pk2.Move4 };
    var rPP = new[] { pk2.Move1_PP, pk2.Move2_PP, pk2.Move3_PP, pk2.Move4_PP };
    Console.WriteLine($"  After reload: Species={pk2.Species}({Name(GameInfo.Strings.Species, pk2.Species)}) Form={pk2.Form} Stat_HPMax={pk2.Stat_HPMax} Stat_Level={pk2.Stat_Level}");
    Console.WriteLine($"  After reload moves: {DescribeMoves(rMoves, rPP)}");

    // --- Round-trip assertions ---
    bool speciesOk = pk2.Species == c.NewSpecies;
    if (!speciesOk) Console.WriteLine($"  FAIL (round-trip): species expected {c.NewSpecies}, got {pk2.Species}.");

    bool movesOk = true;
    for (int i = 0; i < 4; i++)
    {
        if (rMoves[i] != memMoves[i])
        {
            movesOk = false;
            Console.WriteLine($"  FAIL (round-trip): move slot {i + 1} expected {memMoves[i]}, got {rMoves[i]}.");
        }
        if (rPP[i] != memPP[i])
        {
            movesOk = false;
            Console.WriteLine($"  FAIL (round-trip): move slot {i + 1} PP expected {memPP[i]}, got {rPP[i]}.");
        }
    }

    // Stat-block recompute + round-trip: reloaded HP/Stat_Level must match the in-memory recomputed
    // values, AND the stat block must NOT be stale - since species and level both changed, HP must
    // differ from the original species/level's HP (a stale block, the pre-fix bug, would keep it).
    bool hpOk = pk2.Stat_HPMax == memHpMax;
    if (!hpOk) Console.WriteLine($"  FAIL (stat round-trip): Stat_HPMax expected {memHpMax}, got {pk2.Stat_HPMax}.");
    bool statLevelOk = pk2.Stat_Level == newLevel;
    if (!statLevelOk) Console.WriteLine($"  FAIL (stat recalc): Stat_Level expected {newLevel}, got {pk2.Stat_Level}.");
    bool notStale = pk2.Stat_HPMax != origHpMax; // species+level both changed => HP must change
    if (!notStale) Console.WriteLine($"  FAIL (stat recalc): Stat_HPMax still {origHpMax} despite species+level change - stat block is stale.");

    // Gen1: species and its stored types are coupled - verify the stored types match the new
    // species' personal info after round-trip (the PK1.Species setter is responsible for this).
    if (pk2 is PK1 g1)
    {
        var pi = g1.PersonalInfo;
        bool typesOk = g1.Type1 == pi.Type1 && g1.Type2 == pi.Type2;
        Console.WriteLine(typesOk
            ? $"  Gen1 stored types match new species (Type1={g1.Type1}, Type2={g1.Type2})."
            : $"  FAIL (Gen1 types): stored Type1={g1.Type1}/Type2={g1.Type2} != personal {pi.Type1}/{pi.Type2}.");
        if (!typesOk) movesOk = false; // fold into overall pass/fail
    }

    // Form probe: not asserted (form legality is out of scope), just surfaced - a crash on Write
    // above would already have failed the case.
    if (pk2.Form != 0)
        Console.WriteLine($"  NOTE: Form={pk2.Form} after species change (carried over; legality not auto-fixed, as designed).");

    bool caseOk = speciesOk && movesOk && hpOk && statLevelOk && notStale && ppRecalcOk;
    Console.WriteLine(caseOk
        ? "  PASS: species, moves, PP, and stat block all round-tripped correctly."
        : "  CASE FAILED (see above).");
    if (!caseOk) allPass = false;

    var untouched = File.ReadAllBytes(path).AsSpan().SequenceEqual(originalSnapshot);
    Console.WriteLine(untouched ? "  Original file on disk: untouched (confirmed)." : "  WARNING: original file on disk changed!");
    if (!untouched) allPass = false;

    Console.WriteLine();

    void Fail(string msg) { Console.WriteLine($"  FAIL: {msg}"); allPass = false; Console.WriteLine(); }
}

// --- Form-staleness probe ---------------------------------------------------------------------
// The app doesn't edit Form, but if the loaded mon already has a non-zero Form and the user changes
// species, the stale Form byte persists (we deliberately don't auto-correct it - that's legality,
// out of scope). This must NOT crash Write()/ResetPartyStats or corrupt the save; PersonalInfo
// falls back to the base (form-0) entry for an out-of-range form (PersonalInfo.HasForm). Prove it.
Console.WriteLine("=== Form-staleness probe (Gen9, synthetic out-of-range form) ===");
try
{
    var probePath = Path.Combine(root, @"pkmnscarlet_100\main");
    var bytes = File.ReadAllBytes(probePath);
    var sav = SaveUtil.GetSaveFile(bytes)!;
    var pk = sav.PartyData[0];
    pk.Form = 20; // deliberately out of range for the species we're about to set (Garchomp: 1 form)
    pk.Species = (ushort)Species.Garchomp;
    pk.CurrentLevel = 50;
    pk.SetMoves(new ushort[] { 89, 200, 57, 14 });
    pk.ResetPartyStats(); // uses PersonalInfo for (Garchomp, form 20) -> falls back to base form
    sav.SetPartySlotAtIndex(pk, 0);
    var exported = sav.Write().ToArray();
    var reloaded = SaveUtil.GetSaveFile(exported)!;
    var pk2 = reloaded.PartyData[0];
    bool ok = pk2.Species == (ushort)Species.Garchomp && pk2.Stat_HPMax > 0;
    Console.WriteLine(ok
        ? $"  PASS: no crash; Species={pk2.Species} Form={pk2.Form} (stale, as designed) Stat_HPMax={pk2.Stat_HPMax} computed from base form."
        : $"  FAIL: unexpected result Species={pk2.Species} Stat_HPMax={pk2.Stat_HPMax}.");
    if (!ok) allPass = false;
}
catch (Exception ex)
{
    Console.WriteLine($"  FAIL: form-staleness probe threw {ex.GetType().Name}: {ex.Message}");
    allPass = false;
}
Console.WriteLine();

Console.WriteLine(allPass ? "=== ALL CASES PASS ===" : "=== ONE OR MORE CASES FAILED ===");
return allPass ? 0 : 1;

static string Name(IReadOnlyList<string> list, int id) => id >= 0 && id < list.Count ? list[id] : $"#{id}";

static string DescribeMoves(ushort[] moves, int[] pp)
{
    var parts = new string[4];
    for (int i = 0; i < 4; i++)
        parts[i] = $"{Name(GameInfo.Strings.Move, moves[i])}(id={moves[i]},pp={pp[i]})";
    return string.Join(", ", parts);
}

record Case(string Label, string RelPath, int Slot, ushort NewSpecies, ushort[] NewMoves);
