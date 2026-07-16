using PKHeX.Core;

Console.WriteLine("=== Gen2 PKHeX.Core Verification ===");
Console.WriteLine();

// ---------------------------------------------------------------------
// PRIMARY VERIFICATION: Crystal (GameVersion.C), English/International
// This matches the SAV2() default constructor: SAV2(LanguageID.English, GameVersion.C)
// ---------------------------------------------------------------------
Console.WriteLine("--- Primary: SAV2 (Crystal, English/International) ---");
{
    var sav = new SAV2(); // default: LanguageID.English, GameVersion.C
    sav.OT = "VERIFY";

    var pk = sav.BlankPKM;
    pk.Species = 25; // Pikachu
    pk.CurrentLevel = 10;
    pk.HeldItem = 244; // Berry (Gen2 held item test)

    sav.PartyData = [pk];

    Console.WriteLine($"Version: {sav.Version}");
    Console.WriteLine($"Japanese: {sav.Japanese}  Korean: {sav.Korean}");
    Console.WriteLine($"Trainer: {sav.OT}");
    Console.WriteLine($"PartyCount: {sav.PartyCount}");
    for (int i = 0; i < sav.PartyCount; i++)
    {
        var p = sav.PartyData[i];
        Console.WriteLine($"  Species={p.Species} Level={p.CurrentLevel} HeldItem={p.HeldItem}");
    }

    bool ok = sav.OT == "VERIFY" && sav.PartyCount == 1
        && sav.PartyData[0].Species == 25 && sav.PartyData[0].CurrentLevel == 10;
    Console.WriteLine(ok ? "PRIMARY CHECK: PASS" : "PRIMARY CHECK: FAIL");
    Console.WriteLine();

    // ---------------------------------------------------------------------
    // BONUS/SECONDARY (optional): Write() + SaveUtil.GetSaveFile() round-trip.
    // Documented as a known limitation if it fails; not required for primary pass.
    // ---------------------------------------------------------------------
    Console.WriteLine("--- Bonus: Write() + SaveUtil.GetSaveFile() round-trip ---");
    try
    {
        Memory<byte> bytes = sav.Write();
        Console.WriteLine($"Write() produced {bytes.Length} bytes (expect 0x10000 = {0x10000}).");
        var reloaded = SaveUtil.GetSaveFile(bytes);
        if (reloaded is null)
        {
            Console.WriteLine("BONUS CHECK: SaveUtil.GetSaveFile returned null (format not re-detected). Known limitation.");
        }
        else
        {
            Console.WriteLine($"Re-detected as: {reloaded.GetType().Name}, Version={reloaded.Version}");
            Console.WriteLine($"Re-detected Trainer: {reloaded.OT}, PartyCount: {reloaded.PartyCount}");
            Console.WriteLine("BONUS CHECK: PASS");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"BONUS CHECK: THREW {ex.GetType().Name}: {ex.Message}");
    }
    Console.WriteLine();
}

// ---------------------------------------------------------------------
// SECONDARY VERIFICATION: Gold/Silver (GameVersion.GS)
// ---------------------------------------------------------------------
Console.WriteLine("--- Secondary: SAV2 (Gold/Silver, English/International) ---");
{
    var sav = new SAV2(LanguageID.English, GameVersion.GS);
    sav.OT = "GSVER";

    var pk = sav.BlankPKM;
    pk.Species = 152; // Chikorita
    pk.CurrentLevel = 5;

    sav.PartyData = [pk];

    Console.WriteLine($"Version: {sav.Version}");
    Console.WriteLine($"Trainer: {sav.OT}");
    Console.WriteLine($"PartyCount: {sav.PartyCount}");
    for (int i = 0; i < sav.PartyCount; i++)
    {
        var p = sav.PartyData[i];
        Console.WriteLine($"  Species={p.Species} Level={p.CurrentLevel}");
    }

    bool ok = sav.OT == "GSVER" && sav.PartyCount == 1
        && sav.PartyData[0].Species == 152 && sav.PartyData[0].CurrentLevel == 5;
    Console.WriteLine(ok ? "SECONDARY CHECK (GS): PASS" : "SECONDARY CHECK (GS): FAIL");
    Console.WriteLine();
}

// ---------------------------------------------------------------------
// SECONDARY VERIFICATION: Japanese Crystal
// ---------------------------------------------------------------------
Console.WriteLine("--- Secondary: SAV2 (Crystal, Japanese) ---");
{
    var sav = new SAV2(LanguageID.Japanese, GameVersion.C);
    sav.OT = "J";

    var pk = sav.BlankPKM;
    pk.Species = 249; // Lugia
    pk.CurrentLevel = 40;

    sav.PartyData = [pk];

    Console.WriteLine($"Version: {sav.Version}");
    Console.WriteLine($"Japanese: {sav.Japanese}");
    Console.WriteLine($"Trainer: {sav.OT}");
    Console.WriteLine($"PartyCount: {sav.PartyCount}");
    for (int i = 0; i < sav.PartyCount; i++)
    {
        var p = sav.PartyData[i];
        Console.WriteLine($"  Species={p.Species} Level={p.CurrentLevel}");
    }

    bool ok = sav.PartyCount == 1 && sav.PartyData[0].Species == 249 && sav.PartyData[0].CurrentLevel == 40;
    Console.WriteLine(ok ? "SECONDARY CHECK (JP Crystal): PASS" : "SECONDARY CHECK (JP Crystal): FAIL");
}

Console.WriteLine();
Console.WriteLine("=== Done ===");
