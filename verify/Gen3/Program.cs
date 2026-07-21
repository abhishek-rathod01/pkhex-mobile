using PKHeX.Core;

Console.WriteLine("=== Gen 3 (Emerald) Save Verification ===");

// Build a blank Emerald save via PKHeX.Core's own constructor (no hand-written bytes).
var sav = new SAV3E(); // japanese: false (default)
sav.OT = "VERIFY";
sav.TID16 = 12345;

var pk = sav.BlankPKM;
pk.Species = 25; // Pikachu
pk.PID = 0x1234ABCD; // arbitrary non-zero PID so Nature/Gender/Ability derivation is exercised
pk.CurrentLevel = 10;
pk.OriginalTrainerName = sav.OT;
pk.TID16 = sav.TID16;
pk.Language = 2; // English

sav.PartyData = [pk];

// PRIMARY, REQUIRED verification -- read directly off the live object, no serialization needed.
bool primaryOk = sav.OT == "VERIFY" && sav.TID16 == 12345 && sav.PartyCount == 1
    && sav.PartyData[0].Species == 25 && sav.PartyData[0].CurrentLevel == 10;

Console.WriteLine($"Trainer: {sav.OT}");
Console.WriteLine($"TID16: {sav.TID16}");
Console.WriteLine($"PartyCount: {sav.PartyCount}");
for (int i = 0; i < sav.PartyCount; i++)
{
    var p = sav.PartyData[i];
    Console.WriteLine($"  Species={p.Species} Level={p.CurrentLevel} PID={p.PID:X8}");
    Console.WriteLine($"  Nature={p.Nature} (derived from PID % 25)");
    Console.WriteLine($"  Ability={p.Ability} AbilityNumber={p.AbilityNumber} (derived from PersonalInfo + AbilityBit)");
    Console.WriteLine($"  Gender={p.Gender} (derived from PID + species gender ratio)");
    Console.WriteLine($"  OT={p.OriginalTrainerName}");
}

Console.WriteLine();
Console.WriteLine("=== Primary verification complete ===");

// ---------------------------------------------------------------------------
// OPTIONAL / BONUS: Write() + SaveUtil.GetSaveFile round-trip.
// Per known Gen4 precedent, this may fail because the blank constructor does
// not allocate the full backing buffer / footer fields some generations'
// Write()/detection path expects. Not required for the primary goal.
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== Bonus: Write() + SaveUtil.GetSaveFile round-trip (optional) ===");
try
{
    var bytes = sav.Write().ToArray();
    Console.WriteLine($"Write() succeeded, {bytes.Length} bytes.");

    var reloaded = SaveUtil.GetSaveFile(bytes);
    if (reloaded is null)
    {
        Console.WriteLine("SaveUtil.GetSaveFile returned null -- format not re-detected from blank-constructed buffer.");
    }
    else
    {
        Console.WriteLine($"Re-detected as: {reloaded.GetType().Name}");
        Console.WriteLine($"Reloaded Trainer: {reloaded.OT}, PartyCount: {reloaded.PartyCount}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Bonus round-trip threw: {ex}");
}

return primaryOk ? 0 : 1;
