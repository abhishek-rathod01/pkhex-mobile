using PKHeX.Core;

Console.WriteLine("=== Gen4 SAV4DP (Diamond/Pearl) ===");
RunCheck(() =>
{
    var sav = new SAV4DP();
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
});

Console.WriteLine();
Console.WriteLine("=== Gen4 SAV4Pt (Platinum) bonus check ===");
RunCheck(() =>
{
    var sav = new SAV4Pt();
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
});

Console.WriteLine();
Console.WriteLine("=== Gen4 SAV4HGSS (HeartGold/SoulSilver) bonus check ===");
RunCheck(() =>
{
    var sav = new SAV4HGSS();
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
});

Console.WriteLine();
Console.WriteLine("=== Known limitation check: Write() + SaveUtil.GetSaveFile round-trip ===");
RunCheck(() =>
{
    var sav = new SAV4DP(new byte[SaveUtil.SIZE_G4RAW]);
    sav.OT = "VERIFY";
    var pk = sav.BlankPKM;
    pk.Species = 25;
    pk.CurrentLevel = 10;
    sav.PartyData = [pk];

    var bytes = sav.Write();
    Console.WriteLine($"Write() produced {bytes.Length} bytes (expected SIZE_G4RAW={SaveUtil.SIZE_G4RAW})");

    var reloaded = SaveUtil.GetSaveFile(bytes);
    Console.WriteLine(reloaded is null
        ? "SaveUtil.GetSaveFile(bytes) => null (NOT recognized) -- confirms known limitation"
        : $"SaveUtil.GetSaveFile(bytes) => recognized as {reloaded.GetType().Name} (unexpected! limitation may be resolved)");
});

static void RunCheck(Action action)
{
    try
    {
        action();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAILED: {ex.GetType().Name}: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
    }
}
