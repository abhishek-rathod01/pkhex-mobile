using PKHeX.Core;

// Replicates the app's exact edit -> export -> reload code path:
//   load:   MainPage.TryParseSaveFile      -> SaveUtil.GetSaveFile(byte[])
//   edit:   PokemonDetailPage.OnSaveChangesClicked -> pk.Nickname / pk.IsNicknamed / pk.CurrentLevel,
//                                              sav.SetPartySlotAtIndex(pk, index), sav.Write()
//   reload: MainPage.TryParseSaveFile again on the exported bytes (simulates re-picking the exported file)

var root = @"C:\Users\abhis\Downloads\sav files pkmn";

var cases = new (string Label, string RelPath, int Slot, string NewNickname, int NewLevel)[]
{
    ("Gen1 (Red, real save)", "POKEMON RED-0.sav", 0, "TESTEDIT", 42),
    ("Gen5 (Black, real save)", "Pokemon Black Version.sav", 0, "TESTEDIT", 55),
    ("Gen9 (Scarlet, real save)", @"pkmnscarlet_100\main", 0, "TESTEDIT", 88),
};

var allPass = true;

foreach (var (label, relPath, slot, newNickname, newLevel) in cases)
{
    Console.WriteLine($"=== {label} ===");
    var path = Path.Combine(root, relPath);

    var originalBytes = File.ReadAllBytes(path);
    // Defensive copy: SaveUtil.GetSaveFile(byte[]) wraps the array as Memory<byte>
    // without cloning, so editing `pk`/calling Write() later mutates this same
    // array in place. Snapshot a separate copy now so the "untouched on disk"
    // check below compares against a true pre-edit baseline, not a live view.
    var originalBytesSnapshot = (byte[])originalBytes.Clone();
    var sav = SaveUtil.GetSaveFile(originalBytes);
    if (sav is null)
    {
        Console.WriteLine("  FAIL: original file not recognized as a save.");
        allPass = false;
        continue;
    }

    if (slot >= sav.PartyCount)
    {
        Console.WriteLine($"  FAIL: party slot {slot} out of range (PartyCount={sav.PartyCount}).");
        allPass = false;
        continue;
    }

    var pk = sav.PartyData[slot];
    var originalNickname = pk.Nickname;
    var originalLevel = pk.CurrentLevel;
    var effectiveLevel = newLevel == originalLevel ? newLevel + 1 : newLevel;

    Console.WriteLine($"  Before: Nickname='{originalNickname}' Level={originalLevel}");

    pk.Nickname = newNickname;
    pk.IsNicknamed = true;
    pk.CurrentLevel = (byte)effectiveLevel;
    sav.SetPartySlotAtIndex(pk, slot);

    byte[] exportedBytes;
    try
    {
        exportedBytes = sav.Write().ToArray();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  FAIL: sav.Write() threw {ex.GetType().Name}: {ex.Message}");
        allPass = false;
        continue;
    }

    var reloaded = SaveUtil.GetSaveFile(exportedBytes);
    if (reloaded is null)
    {
        Console.WriteLine("  FAIL: exported bytes not recognized by SaveUtil.GetSaveFile on reload.");
        allPass = false;
        continue;
    }

    if (slot >= reloaded.PartyCount)
    {
        Console.WriteLine($"  FAIL: reloaded party slot {slot} out of range (PartyCount={reloaded.PartyCount}).");
        allPass = false;
        continue;
    }

    var pk2 = reloaded.PartyData[slot];
    Console.WriteLine($"  After reload: Nickname='{pk2.Nickname}' Level={pk2.CurrentLevel}");

    var nicknameOk = pk2.Nickname == newNickname;
    var levelOk = pk2.CurrentLevel == effectiveLevel;

    if (nicknameOk && levelOk)
    {
        Console.WriteLine("  PASS: nickname and level round-tripped correctly.");
    }
    else
    {
        allPass = false;
        if (!nicknameOk)
            Console.WriteLine($"  FAIL: nickname mismatch - expected '{newNickname}', got '{pk2.Nickname}'.");
        if (!levelOk)
            Console.WriteLine($"  FAIL: level mismatch - expected {effectiveLevel}, got {pk2.CurrentLevel}.");
    }

    // Never touch the original file on disk - confirm it's untouched.
    var stillOriginal = File.ReadAllBytes(path).AsSpan().SequenceEqual(originalBytesSnapshot);
    Console.WriteLine(stillOriginal
        ? "  Original file on disk: untouched (confirmed)."
        : "  WARNING: original file on disk changed unexpectedly!");

    Console.WriteLine();
}

Console.WriteLine(allPass ? "=== ALL CASES PASS ===" : "=== ONE OR MORE CASES FAILED ===");
return allPass ? 0 : 1;
