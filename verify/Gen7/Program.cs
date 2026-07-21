using System;
using PKHeX.Core;

bool Verify(string label, SaveFile sav)
{
    Console.WriteLine($"=== {label} ===");
    sav.OT = "VERIFY";
    var pk = sav.BlankPKM;
    pk.Species = 25; // Pikachu
    pk.CurrentLevel = 10;
    sav.PartyData = [pk];

    Console.WriteLine($"Trainer: {sav.OT}");
    Console.WriteLine($"PartyCount: {sav.PartyCount}");
    for (int i = 0; i < sav.PartyCount; i++)
    {
        var p = sav.PartyData[i];
        Console.WriteLine($"  Species={p.Species} Level={p.CurrentLevel}");
    }
    Console.WriteLine();
    return sav.OT == "VERIFY" && sav.PartyCount == 1 && sav.PartyData[0].Species == 25 && sav.PartyData[0].CurrentLevel == 10;
}

// --- Primary verification: SM ---
var sm = new SAV7SM();
bool smOk = Verify("SAV7SM (Sun/Moon)", sm);

// --- Primary verification: USUM ---
var usum = new SAV7USUM();
bool usumOk = Verify("SAV7USUM (Ultra Sun/Ultra Moon)", usum);

// --- Gen7 quirk check: Alolan form handling (species 19 Rattata, Form=1 = Alolan) ---
Console.WriteLine("=== Gen7 quirk: Alolan form (Rattata, Form=1) ===");
var formSav = new SAV7USUM();
formSav.OT = "VERIFY";
var alolan = formSav.BlankPKM;
alolan.Species = 19; // Rattata
alolan.Form = 1;      // Alolan Rattata
alolan.CurrentLevel = 12;
formSav.PartyData = [alolan];
var readBack = formSav.PartyData[0];
Console.WriteLine($"  Species={readBack.Species} Form={readBack.Form} Level={readBack.CurrentLevel}");
Console.WriteLine();

// --- Optional bonus check: Write() + SaveUtil.GetSaveFile round-trip ---
Console.WriteLine("=== Bonus: Write()/GetSaveFile round-trip (SAV7SM) ===");
try
{
    var bytes = sm.Write();
    Console.WriteLine($"Write() succeeded, byte length: {bytes.Length}");
    var reloaded = SaveUtil.GetSaveFile(bytes);
    if (reloaded is null)
    {
        Console.WriteLine("GetSaveFile did NOT re-detect the save format (returned null).");
    }
    else
    {
        Console.WriteLine($"GetSaveFile re-detected as: {reloaded.GetType().Name}");
        Console.WriteLine($"Reloaded Trainer: {reloaded.OT}");
        Console.WriteLine($"Reloaded PartyCount: {reloaded.PartyCount}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Bonus round-trip FAILED: {ex.GetType().Name}: {ex.Message}");
}

return (smOk && usumOk) ? 0 : 1;
