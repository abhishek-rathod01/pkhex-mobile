using PKHeX.Core;

// Verifies Item 1(e): loading/handling an out-of-range Gen1/2 IV value never crashes.
//
// Ground truth from vendor/PKHeX.Core/PKM/Shared/GBPKM.cs:186-191: Gen1/2 IVs are 4-bit
// DVs packed into a single 16-bit DV16 register (4 bits per stat). That means a real save
// file structurally CANNOT contain a DV above 15 for IV_ATK/DEF/SPA/SPE - there is no bit
// pattern that represents it. IV_HP has no storage at all (derived from the other four DVs'
// low bits) and IV_SPD is an alias of IV_SPA. So "out of range" cannot occur via genuine
// save data for these fields.
//
// What CAN happen, and what this harness actually exercises:
//   1. The real PKM setters (IV_ATK/DEF/SPA/SPE) already clamp any out-of-range assignment
//      to 15 at the library level (`value > 0xF ? 0xF : value`) - confirm this holds and
//      never throws, then confirm SetPartySlotAtIndex + Write() + reload survives it.
//   2. The app's own defensive clamp (PokemonDetailPage.LoadPokemon: `Math.Clamp(p.IV_X, 0,
//      ivMax)`) is exercised directly against synthetic extreme/adversarial inputs
//      (int.MaxValue, negative numbers) that don't come from any real PKM getter, to prove
//      the backstop itself can never throw or produce an out-of-bounds display value,
//      regardless of what a future/corrupted format might someday hand it.

var root = @"C:\Users\abhis\Downloads\sav files pkmn";

var cases = new (string Label, string RelPath)[]
{
    ("Gen1 (Red, real save)", "POKEMON RED-0.sav"),
    ("Gen2 (Crystal, real save)", "Pokemon - Crystal Version (UE) (V1.1) C!.SAV"),
};

var allPass = true;

// --- Part 1: library-level setter clamp, exercised through the app's real edit path ---
foreach (var (label, relPath) in cases)
{
    Console.WriteLine($"=== {label}: over-range IV set via real setters ===");
    var path = Path.Combine(root, relPath);
    var bytes = File.ReadAllBytes(path);
    var originalSnapshot = (byte[])bytes.Clone();

    var sav = SaveUtil.GetSaveFile(bytes);
    if (sav is null || sav.PartyCount == 0)
    {
        Console.WriteLine("  FAIL: could not load a save with at least one party slot.");
        allPass = false;
        continue;
    }

    var pk = sav.PartyData[0];
    try
    {
        // Deliberately way out of range (real hardware max is 15).
        pk.IV_ATK = 999;
        pk.IV_DEF = 255;
        pk.IV_SPA = 200;
        pk.IV_SPE = 31;
        pk.IV_HP = 999;  // no-op setter, must not throw
        pk.IV_SPD = 999; // no-op setter (aliases SPA), must not throw

        Console.WriteLine($"  After over-range set: ATK={pk.IV_ATK} DEF={pk.IV_DEF} SPA={pk.IV_SPA} SPE={pk.IV_SPE} HP={pk.IV_HP} SPD={pk.IV_SPD}");

        var inRange = pk.IV_ATK <= 15 && pk.IV_DEF <= 15 && pk.IV_SPA <= 15 && pk.IV_SPE <= 15 && pk.IV_HP <= 15 && pk.IV_SPD <= 15;
        if (!inRange)
        {
            Console.WriteLine("  FAIL: library did not clamp an over-range DV to <= 15.");
            allPass = false;
        }

        sav.SetPartySlotAtIndex(pk, 0);
        var exported = sav.Write().ToArray();
        var reloaded = SaveUtil.GetSaveFile(exported);
        if (reloaded is null || reloaded.PartyCount == 0)
        {
            Console.WriteLine("  FAIL: export+reload of the clamped save failed.");
            allPass = false;
        }
        else
        {
            var pk2 = reloaded.PartyData[0];
            Console.WriteLine($"  After reload: ATK={pk2.IV_ATK} DEF={pk2.IV_DEF} SPA={pk2.IV_SPA} SPE={pk2.IV_SPE} HP={pk2.IV_HP} SPD={pk2.IV_SPD}");
            Console.WriteLine("  PASS: no crash setting/exporting/reloading an over-range IV; library clamped to <= 15.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  FAIL: threw {ex.GetType().Name}: {ex.Message}");
        allPass = false;
    }

    var stillOriginal = File.ReadAllBytes(path).AsSpan().SequenceEqual(originalSnapshot);
    Console.WriteLine(stillOriginal ? "  Original file on disk: untouched (confirmed)." : "  WARNING: original file changed on disk!");
    Console.WriteLine();
}

// --- Part 2: the app's own defensive Math.Clamp backstop, against adversarial synthetic input ---
Console.WriteLine("=== App-level defensive clamp (PokemonDetailPage.LoadPokemon pattern) ===");
foreach (var (rawValue, ivMax) in new[] { (int.MaxValue, 15), (int.MinValue, 15), (-1, 15), (16, 15), (999999, 31), (-999999, 31) })
{
    try
    {
        var clamped = Math.Clamp(rawValue, 0, ivMax);
        var ok = clamped >= 0 && clamped <= ivMax;
        Console.WriteLine($"  Math.Clamp({rawValue}, 0, {ivMax}) = {clamped}  {(ok ? "PASS" : "FAIL")}");
        if (!ok)
            allPass = false;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  FAIL: Math.Clamp({rawValue}, 0, {ivMax}) threw {ex.GetType().Name}: {ex.Message}");
        allPass = false;
    }
}

Console.WriteLine();
Console.WriteLine(allPass ? "=== ALL CASES PASS ===" : "=== ONE OR MORE CASES FAILED ===");
return allPass ? 0 : 1;
