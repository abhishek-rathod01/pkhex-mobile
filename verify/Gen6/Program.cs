using PKHeX.Core;

Console.WriteLine("=== Gen6 (X/Y) Save Verification ===");

var sav = new SAV6XY();
sav.OT = "VERIFY";
var pk = sav.BlankPKM;
pk.Species = 25; // Pikachu
pk.CurrentLevel = 10;
sav.PartyData = [pk];

// PRIMARY, REQUIRED verification — read directly off the live object, no serialization needed.
bool primaryOk = sav.OT == "VERIFY" && sav.PartyCount == 1 && sav.PartyData[0].Species == 25 && sav.PartyData[0].CurrentLevel == 10;

Console.WriteLine($"Trainer: {sav.OT}");
Console.WriteLine($"PartyCount: {sav.PartyCount}");
for (int i = 0; i < sav.PartyCount; i++)
{
    var p = sav.PartyData[i];
    Console.WriteLine($"  Species={p.Species} Level={p.CurrentLevel}");
}

Console.WriteLine();
Console.WriteLine($"Version: {sav.Version}");
Console.WriteLine($"Generation: {sav.Generation}");
Console.WriteLine($"IsVersionValid (before setting Version): {sav.IsVersionValid()}");

// SAV6XY defaults to GameVersion.Any until Version is explicitly set; set it to exercise
// the Gen6-specific IsVersionValid() override (X or Y only).
sav.Version = GameVersion.Y;
Console.WriteLine($"Version (after set): {sav.Version}");
Console.WriteLine($"IsVersionValid (after setting Version=Y): {sav.IsVersionValid()}");

// --- BONUS / secondary check (optional, not required) ---
// Attempt the Write() + SaveUtil.GetSaveFile() round trip to simulate loading the save as a
// file from disk. Per the known Gen4 trap, this may throw or fail to re-detect because the
// blank constructor doesn't populate footer/size-detection fields the way a real save does.
Console.WriteLine();
Console.WriteLine("=== Bonus: Write()+GetSaveFile round-trip (optional) ===");
try
{
    var bytes = sav.Write();
    Console.WriteLine($"Write() succeeded, {bytes.Length} bytes.");
    var reloaded = SaveUtil.GetSaveFile(bytes);
    if (reloaded is null)
    {
        Console.WriteLine("SaveUtil.GetSaveFile returned null (format not re-detected from blank-constructor bytes). This matches the known Gen4-style limitation.");
    }
    else
    {
        Console.WriteLine($"Re-detected as: {reloaded.GetType().Name}, OT={reloaded.OT}, PartyCount={reloaded.PartyCount}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Round-trip threw {ex.GetType().Name}: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("Done.");

return primaryOk ? 0 : 1;
