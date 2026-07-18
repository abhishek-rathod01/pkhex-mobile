using PKHeX.Core;

// Picks which real saves have populated boxes worth driving through the on-device
// BoxListPage UI for Item 3 - no point testing against a save with all-empty boxes.

var root = @"C:\Users\abhis\Downloads\sav files pkmn";

var cases = new (string Label, string RelPath)[]
{
    ("Gen1 (Red)", "POKEMON RED-0.sav"),
    ("Gen2 (Crystal)", "Pokemon - Crystal Version (UE) (V1.1) C!.SAV"),
    ("Gen3 (Emerald)", "pokeemerald (2).sav"),
    ("Gen4 (HeartGold)", "Pokemon Heart Gold Version.sav"),
    ("Gen5 (Black)", "Pokemon Black Version.sav"),
    ("Gen6 (Alpha Sapphire)", @"oldgen6&7saves\Sapphire\Pokemon Alpha Sapphire\oldASsave\main"),
    ("Gen7 (Ultra Moon)", @"oldgen6&7saves\Moon\Ultra Moon\oldUMsave\main"),
    ("Gen8 (Sword)", @"pokemonsword_100\main"),
    ("Gen9 (Scarlet)", @"pkmnscarlet_100\main"),
};

foreach (var (label, relPath) in cases)
{
    var path = Path.Combine(root, relPath);
    if (!File.Exists(path))
    {
        Console.WriteLine($"{label}: FILE NOT FOUND at {path}");
        continue;
    }

    var bytes = File.ReadAllBytes(path);
    var sav = SaveUtil.GetSaveFile(bytes);
    if (sav is null)
    {
        Console.WriteLine($"{label}: not recognized as a save file.");
        continue;
    }

    if (!sav.HasBox)
    {
        Console.WriteLine($"{label}: HasBox=false (no PC storage).");
        continue;
    }

    int totalNonEmpty = 0;
    var perBoxCounts = new List<(int Box, string Name, int Count)>();
    for (int box = 0; box < sav.BoxCount; box++)
    {
        var name = sav is IBoxDetailNameRead r ? r.GetBoxName(box) : BoxDetailNameExtensions.GetDefaultBoxName(box);
        int count = 0;
        for (int slot = 0; slot < sav.BoxSlotCount; slot++)
        {
            var pk = sav.GetBoxSlotAtIndex(box, slot);
            if (pk.Species != 0)
                count++;
        }
        if (count > 0)
            perBoxCounts.Add((box, name, count));
        totalNonEmpty += count;
    }

    Console.WriteLine($"{label}: HasBox=true BoxCount={sav.BoxCount} BoxSlotCount={sav.BoxSlotCount} TotalNonEmpty={totalNonEmpty}");
    foreach (var (box, name, count) in perBoxCounts.Take(5))
        Console.WriteLine($"    Box {box} \"{name}\": {count} occupied");
    if (perBoxCounts.Count > 5)
        Console.WriteLine($"    ... and {perBoxCounts.Count - 5} more non-empty boxes");
}
