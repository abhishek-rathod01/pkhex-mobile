using System;
using PKHeX.Core;

Console.WriteLine("=== Gen 8 (SWSH) save-parsing verification ===");
Console.WriteLine("Save source: library-generated blank save (no real .sav file used, none hand-written).");
Console.WriteLine();

// ---- PRIMARY, REQUIRED verification: build a blank save via PKHeX.Core's own constructor ----
var sav = new SAV8SWSH();
sav.OT = "VERIFY";

var pk = sav.BlankPKM;
pk.Species = 25; // Pikachu
pk.CurrentLevel = 10;
sav.PartyData = [pk]; // PartyData list-setter sets PartyCount = ctr directly

Console.WriteLine($"Trainer: {sav.OT}");
Console.WriteLine($"PartyCount: {sav.PartyCount}");
for (int i = 0; i < sav.PartyCount; i++)
{
    var p = sav.PartyData[i];
    Console.WriteLine($"  Species={p.Species} Level={p.CurrentLevel}");
}

bool primaryOk = sav.OT == "VERIFY" && sav.PartyCount == 1 && sav.PartyData[0].Species == 25 && sav.PartyData[0].CurrentLevel == 10;
Console.WriteLine();
Console.WriteLine($"PRIMARY VERIFICATION RESULT: {(primaryOk ? "PASS" : "FAIL")}");

// ---- BONUS / OPTIONAL secondary check: Write() then re-detect via SaveUtil.GetSaveFile ----
Console.WriteLine();
Console.WriteLine("=== Bonus check: Write() + SaveUtil.GetSaveFile round-trip (optional, not required) ===");
try
{
    var bytes = sav.Write();
    Console.WriteLine($"Write() succeeded. Output length: {bytes.Length} bytes (0x{bytes.Length:X}).");

    var reloaded = SaveUtil.GetSaveFile(bytes);
    if (reloaded is null)
    {
        Console.WriteLine("SaveUtil.GetSaveFile returned null: format was NOT re-detected from the written bytes.");
    }
    else if (reloaded is SAV8SWSH swsh2)
    {
        Console.WriteLine("SaveUtil.GetSaveFile re-detected the save as SAV8SWSH.");
        Console.WriteLine($"  Reloaded Trainer: {swsh2.OT}");
        Console.WriteLine($"  Reloaded PartyCount: {swsh2.PartyCount}");
        for (int i = 0; i < swsh2.PartyCount; i++)
        {
            var p = swsh2.PartyData[i];
            Console.WriteLine($"    Species={p.Species} Level={p.CurrentLevel}");
        }
    }
    else
    {
        Console.WriteLine($"SaveUtil.GetSaveFile returned an unexpected type: {reloaded.GetType().Name}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Bonus round-trip threw: {ex.GetType().Name}: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("=== Done ===");

return primaryOk ? 0 : 1;
