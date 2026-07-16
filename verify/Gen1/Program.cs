using PKHeX.Core;

Console.WriteLine("=== Gen 1 (Red/Blue/Yellow) Save Verification ===");
Console.WriteLine();

// --- Primary verification: build a blank save via PKHeX.Core's own constructor ---
var sav = new SAV1(); // LanguageID.English, GameVersion defaults to RBY
sav.OT = "VERIFY";
var pk = sav.BlankPKM; // PK1
pk.Species = 25; // Pikachu (National Dex number)
pk.CurrentLevel = 10;
sav.PartyData = [pk]; // Use the PartyData list-setter (sets PartyCount = ctr correctly)

Console.WriteLine("-- Direct read off the live SAV1 object --");
Console.WriteLine($"SAV type: {sav.GetType().Name}");
Console.WriteLine($"Version: {sav.Version}");
Console.WriteLine($"Japanese: {sav.Japanese}");
Console.WriteLine($"Trainer: {sav.OT}");
Console.WriteLine($"TID16: {sav.TID16}");
Console.WriteLine($"PartyCount: {sav.PartyCount}");
for (int i = 0; i < sav.PartyCount; i++)
{
    var p = sav.PartyData[i];
    Console.WriteLine($"  Slot {i}: Species={p.Species} Level={p.CurrentLevel} Nickname='{p.Nickname}'");
}

bool primaryOk = sav.OT == "VERIFY"
    && sav.PartyCount == 1
    && sav.PartyData[0].Species == 25
    && sav.PartyData[0].CurrentLevel == 10;
Console.WriteLine();
Console.WriteLine(primaryOk ? "PRIMARY VERIFICATION: PASS" : "PRIMARY VERIFICATION: FAIL");
Console.WriteLine();

// --- Gen1-specific quirk check: internal storage index vs National Dex number ---
// Gen 1 games store species using an internal (non-National-Dex) index order in the
// party/box list, distinct from the Pokedex order used everywhere else in PKHeX (and
// in every later generation's save format). PKHeX.Core's PK1.Species property is
// supposed to abstract this away: the public Species getter/setter always deals in
// National Dex numbers, while PK1.SpeciesInternal exposes the raw internal byte PKHeX
// actually writes into the party/box buffer.
Console.WriteLine("-- Gen1 internal index vs National Dex abstraction --");
var pk1 = (PK1)pk;
Console.WriteLine($"Public Species (National Dex): {pk1.Species}");
Console.WriteLine($"Raw SpeciesInternal (Gen1 internal index byte): {pk1.SpeciesInternal}");
bool quirkOk = pk1.Species == 25 && pk1.SpeciesInternal != 25; // Pikachu's internal index is not 25
Console.WriteLine($"Internal index differs from National Dex number as expected: {quirkOk}");

// Cross-check via the converter directly.
byte internalId = SpeciesConverter.GetInternal1(25);
ushort roundTrip = SpeciesConverter.GetNational1(internalId);
Console.WriteLine($"SpeciesConverter.GetInternal1(25) = {internalId}; GetNational1({internalId}) = {roundTrip}");
Console.WriteLine();

// Try a second species to further confirm the abstraction isn't a coincidence (e.g. Mew = 151).
var pk2 = sav.BlankPKM;
pk2.Species = 151; // Mew
pk2.CurrentLevel = 5;
sav.PartyData = [pk, pk2];
Console.WriteLine("-- Second slot: Mew (species 151) --");
Console.WriteLine($"PartyCount: {sav.PartyCount}");
for (int i = 0; i < sav.PartyCount; i++)
{
    var p = sav.PartyData[i];
    Console.WriteLine($"  Slot {i}: Species={p.Species} Level={p.CurrentLevel}");
}
bool secondOk = sav.PartyCount == 2 && sav.PartyData[0].Species == 25 && sav.PartyData[1].Species == 151;
Console.WriteLine(secondOk ? "SECOND SLOT VERIFICATION: PASS" : "SECOND SLOT VERIFICATION: FAIL");
Console.WriteLine();

// --- OPTIONAL / BONUS: Write() + SaveUtil.GetSaveFile round-trip ---
// Not required by the task; documented as a factual outcome either way.
Console.WriteLine("-- OPTIONAL bonus: Write() + SaveUtil.GetSaveFile round-trip --");
try
{
    var written = sav.Write();
    Console.WriteLine($"sav.Write() succeeded. Byte length: {written.Length}");

    var reloaded = SaveUtil.GetSaveFile(written);
    if (reloaded is null)
    {
        Console.WriteLine("SaveUtil.GetSaveFile(written) returned null (format not re-detected).");
        Console.WriteLine("BONUS ROUND-TRIP: FAIL (re-detection failed)");
    }
    else
    {
        Console.WriteLine($"Re-detected as: {reloaded.GetType().Name}, Generation={reloaded.Generation}");
        Console.WriteLine($"Reloaded Trainer: {reloaded.OT}");
        Console.WriteLine($"Reloaded PartyCount: {reloaded.PartyCount}");
        for (int i = 0; i < reloaded.PartyCount; i++)
        {
            var p = reloaded.PartyData[i];
            Console.WriteLine($"  Slot {i}: Species={p.Species} Level={p.CurrentLevel}");
        }
        bool bonusOk = reloaded.OT == "VERIFY" && reloaded.PartyCount == 2
            && reloaded.PartyData[0].Species == 25 && reloaded.PartyData[1].Species == 151;
        Console.WriteLine(bonusOk ? "BONUS ROUND-TRIP: PASS" : "BONUS ROUND-TRIP: FAIL (data mismatch after reload)");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"BONUS ROUND-TRIP: THREW {ex.GetType().Name}: {ex.Message}");
}

Console.WriteLine();

// --- Follow-up experiment: does placing a Pokemon in a PC box (rather than only the
// party) make the Write()+GetSaveFile round-trip succeed? This uses only the public
// SetBoxSlotAtIndex API (no hand-patched bytes) to test the root cause identified above:
// PokeList1.MergeSingles skips writing the 0xFF list terminator for a box that has never
// been "initialized" (BoxesInitialized flag), which SaveUtil's detection logic requires.
Console.WriteLine("-- Follow-up: Write()+GetSaveFile after also populating Box 1 Slot 0 --");
try
{
    var boxMon = sav.BlankPKM;
    boxMon.Species = 1; // Bulbasaur
    boxMon.CurrentLevel = 5;
    sav.SetBoxSlotAtIndex(boxMon, 0, 0);
    Console.WriteLine($"BoxesInitialized after SetBoxSlotAtIndex: {sav.BoxesInitialized}");

    var written2 = sav.Write();
    var reloaded2 = SaveUtil.GetSaveFile(written2);
    if (reloaded2 is null)
    {
        Console.WriteLine("Still not re-detected after populating a box slot.");
    }
    else
    {
        Console.WriteLine($"Re-detected as: {reloaded2.GetType().Name}, Generation={reloaded2.Generation}");
        Console.WriteLine($"Reloaded Trainer: {reloaded2.OT}, PartyCount: {reloaded2.PartyCount}");
        var boxPk = reloaded2.GetBoxSlotAtIndex(0, 0);
        Console.WriteLine($"Reloaded Box 1 Slot 0: Species={boxPk.Species} Level={boxPk.CurrentLevel}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Follow-up experiment threw {ex.GetType().Name}: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("=== Done ===");
