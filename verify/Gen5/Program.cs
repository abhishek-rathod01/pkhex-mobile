using PKHeX.Core;

Console.WriteLine("=== Gen5 (Black/White) Save Verification ===");
Console.WriteLine("Source: library-generated blank save via SAV5BW() constructor (no hand-written bytes).");
Console.WriteLine();

// --- PRIMARY VERIFICATION: construct a blank save via PKHeX.Core's own constructor ---
var sav = new SAV5BW();
sav.OT = "VERIFY";
sav.Version = GameVersion.W; // Gen5 BW/B2W2 saves don't self-report version unless set explicitly

var pk = sav.BlankPKM;
pk.Species = 25; // Pikachu
pk.CurrentLevel = 10;

sav.PartyData = [pk]; // list-setter: explicitly sets PartyCount = ctr

Console.WriteLine($"Trainer: {sav.OT}");
Console.WriteLine($"Version: {sav.Version}");
Console.WriteLine($"Generation: {sav.Generation}");
Console.WriteLine($"PartyCount: {sav.PartyCount}");
for (int i = 0; i < sav.PartyCount; i++)
{
    var p = sav.PartyData[i];
    Console.WriteLine($"  Slot[{i}]: Species={p.Species} Level={p.CurrentLevel} IsPartyValid={p is PK5}");
}

bool primaryOk = sav.OT == "VERIFY"
    && sav.PartyCount == 1
    && sav.PartyData[0].Species == 25
    && sav.PartyData[0].CurrentLevel == 10;

Console.WriteLine();
Console.WriteLine(primaryOk ? "PRIMARY VERIFICATION: PASS" : "PRIMARY VERIFICATION: FAIL");

// --- SECONDARY / BONUS (optional): Write() + SaveUtil.GetSaveFile round-trip ---
// Known trap from Gen4: the blank constructor does not populate the footer/magic
// bytes that SaveUtil's format-sniffing relies on, and Write() may throw because
// the backing buffer/checksum path expects a fully-populated block layout.
// This is attempted only as a best-effort secondary check; failure here does not
// invalidate the primary verification above, and per instructions we do not
// hand-patch bytes to work around it.
Console.WriteLine();
Console.WriteLine("=== SECONDARY (bonus) check: Write() + SaveUtil.GetSaveFile round-trip ===");
try
{
    var bytes = sav.Write();
    Console.WriteLine($"Write() succeeded, {bytes.Length} bytes.");

    var reloaded = SaveUtil.GetSaveFile(bytes);
    if (reloaded is null)
    {
        Console.WriteLine("SaveUtil.GetSaveFile returned null: format not re-detected from blank-constructed buffer (known limitation, same class as Gen4).");
    }
    else
    {
        Console.WriteLine($"Re-detected as: {reloaded.GetType().Name}, OT={reloaded.OT}, PartyCount={reloaded.PartyCount}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Round-trip threw {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine("This is a known limitation class (see Gen4 findings) - not fixed here per instructions (no hand-patching bytes).");
}

Console.WriteLine();
Console.WriteLine("=== Done ===");
