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
    ("Gen9 (Legends Z-A, real save, CJK nickname)", @"pkmnlegendsza_100_21\main", 0, "テスト測試", 77),
};

// IV/EV values chosen to be distinguishable from each other and from typical
// defaults (0 or 31), so a mixup between stats is obvious in the diff.
const int NewIvHp = 5, NewIvAtk = 11, NewIvDef = 17, NewIvSpa = 23, NewIvSpd = 29, NewIvSpe = 31;
const int NewEvHp = 4, NewEvAtk = 12, NewEvDef = 20, NewEvSpa = 28, NewEvSpd = 36, NewEvSpe = 44;

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

    var beforeIvs = (pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPA, pk.IV_SPD, pk.IV_SPE);
    var beforeEvs = (pk.EV_HP, pk.EV_ATK, pk.EV_DEF, pk.EV_SPA, pk.EV_SPD, pk.EV_SPE);
    Console.WriteLine($"  Before IVs: HP={beforeIvs.Item1} Atk={beforeIvs.Item2} Def={beforeIvs.Item3} SpA={beforeIvs.Item4} SpD={beforeIvs.Item5} Spe={beforeIvs.Item6}");
    Console.WriteLine($"  Before EVs: HP={beforeEvs.Item1} Atk={beforeEvs.Item2} Def={beforeEvs.Item3} SpA={beforeEvs.Item4} SpD={beforeEvs.Item5} Spe={beforeEvs.Item6}");

    pk.Nickname = newNickname;
    pk.IsNicknamed = true;
    pk.CurrentLevel = (byte)effectiveLevel;

    pk.IV_HP = NewIvHp;
    pk.IV_ATK = NewIvAtk;
    pk.IV_DEF = NewIvDef;
    pk.IV_SPA = NewIvSpa;
    pk.IV_SPD = NewIvSpd;
    pk.IV_SPE = NewIvSpe;

    pk.EV_HP = NewEvHp;
    pk.EV_ATK = NewEvAtk;
    pk.EV_DEF = NewEvDef;
    pk.EV_SPA = NewEvSpa;
    pk.EV_SPD = NewEvSpd;
    pk.EV_SPE = NewEvSpe;

    // Snapshot what the PKM object actually holds right after the setters ran
    // (pre-export). This is the correct round-trip baseline: some gens clamp
    // or derive fields (e.g. Gen1/2 DVs cap at 15 and HP/SpD are derived from
    // other stats, not independently stored) - the round-trip check cares
    // whether export+reload preserves *this* value, not the raw requested
    // input, which a generation may legitimately normalize on set.
    var ivSnapshot = new (string Name, int Value)[]
    {
        ("IV_HP", pk.IV_HP), ("IV_ATK", pk.IV_ATK), ("IV_DEF", pk.IV_DEF),
        ("IV_SPA", pk.IV_SPA), ("IV_SPD", pk.IV_SPD), ("IV_SPE", pk.IV_SPE),
    };
    var evSnapshot = new (string Name, int Value)[]
    {
        ("EV_HP", pk.EV_HP), ("EV_ATK", pk.EV_ATK), ("EV_DEF", pk.EV_DEF),
        ("EV_SPA", pk.EV_SPA), ("EV_SPD", pk.EV_SPD), ("EV_SPE", pk.EV_SPE),
    };
    Console.WriteLine($"  In-memory after set (pre-export) IVs: {string.Join(" ", ivSnapshot.Select(s => $"{s.Name}={s.Value}"))}");
    Console.WriteLine($"  In-memory after set (pre-export) EVs: {string.Join(" ", evSnapshot.Select(s => $"{s.Name}={s.Value}"))}");

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
    Console.WriteLine($"  After reload party stat block: Stat_Level={pk2.Stat_Level} Stat_HPMax={pk2.Stat_HPMax} Stat_HPCurrent={pk2.Stat_HPCurrent} Stat_ATK={pk2.Stat_ATK}");
    Console.WriteLine($"  After reload IVs: HP={pk2.IV_HP} Atk={pk2.IV_ATK} Def={pk2.IV_DEF} SpA={pk2.IV_SPA} SpD={pk2.IV_SPD} Spe={pk2.IV_SPE}");
    Console.WriteLine($"  After reload EVs: HP={pk2.EV_HP} Atk={pk2.EV_ATK} Def={pk2.EV_DEF} SpA={pk2.EV_SPA} SpD={pk2.EV_SPD} Spe={pk2.EV_SPE}");

    var nicknameOk = pk2.Nickname == newNickname;
    var levelOk = pk2.CurrentLevel == effectiveLevel;

    var actualIvs = new[] { pk2.IV_HP, pk2.IV_ATK, pk2.IV_DEF, pk2.IV_SPA, pk2.IV_SPD, pk2.IV_SPE };
    var actualEvs = new[] { pk2.EV_HP, pk2.EV_ATK, pk2.EV_DEF, pk2.EV_SPA, pk2.EV_SPD, pk2.EV_SPE };

    // Round-trip fidelity: compare against what the object held right before
    // Write(), not the raw requested input (see snapshot comment above).
    var ivMismatches = ivSnapshot.Zip(actualIvs, (s, a) => (s.Name, Expected: s.Value, Actual: a))
        .Where(c => c.Expected != c.Actual).ToList();
    var evMismatches = evSnapshot.Zip(actualEvs, (s, a) => (s.Name, Expected: s.Value, Actual: a))
        .Where(c => c.Expected != c.Actual).ToList();

    // Input-normalization note: separately flag any field the generation
    // silently changed on *set* (requested input != in-memory pre-export
    // value). Not a round-trip failure - a UX finding about how much the
    // generic IV/EV UI trusts a generation to store what was typed in.
    var requestedIvs = new[] { NewIvHp, NewIvAtk, NewIvDef, NewIvSpa, NewIvSpd, NewIvSpe };
    var requestedEvs = new[] { NewEvHp, NewEvAtk, NewEvDef, NewEvSpa, NewEvSpd, NewEvSpe };
    var ivNormalized = ivSnapshot.Zip(requestedIvs, (s, r) => (s.Name, Requested: r, Stored: s.Value))
        .Where(c => c.Requested != c.Stored).ToList();
    var evNormalized = evSnapshot.Zip(requestedEvs, (s, r) => (s.Name, Requested: r, Stored: s.Value))
        .Where(c => c.Requested != c.Stored).ToList();

    if (nicknameOk && levelOk && ivMismatches.Count == 0 && evMismatches.Count == 0)
    {
        Console.WriteLine("  PASS: nickname, level, IVs, and EVs all round-tripped correctly (export+reload preserves the in-memory value).");
    }
    else
    {
        allPass = false;
        if (!nicknameOk)
            Console.WriteLine($"  FAIL (round-trip): nickname mismatch - expected '{newNickname}', got '{pk2.Nickname}'.");
        if (!levelOk)
            Console.WriteLine($"  FAIL (round-trip): level mismatch - expected {effectiveLevel}, got {pk2.CurrentLevel}.");
        foreach (var m in ivMismatches)
            Console.WriteLine($"  FAIL (round-trip): {m.Name} mismatch - in-memory pre-export was {m.Expected}, reload gave {m.Actual}.");
        foreach (var m in evMismatches)
            Console.WriteLine($"  FAIL (round-trip): {m.Name} mismatch - in-memory pre-export was {m.Expected}, reload gave {m.Actual}.");
    }

    foreach (var n in ivNormalized)
        Console.WriteLine($"  NOTE (input normalized, not a round-trip bug): requested {n.Name}={n.Requested}, generation stored {n.Stored} instead.");
    foreach (var n in evNormalized)
        Console.WriteLine($"  NOTE (input normalized, not a round-trip bug): requested {n.Name}={n.Requested}, generation stored {n.Stored} instead.");

    // Never touch the original file on disk - confirm it's untouched.
    var stillOriginal = File.ReadAllBytes(path).AsSpan().SequenceEqual(originalBytesSnapshot);
    Console.WriteLine(stillOriginal
        ? "  Original file on disk: untouched (confirmed)."
        : "  WARNING: original file on disk changed unexpectedly!");

    Console.WriteLine();
}

Console.WriteLine(allPass ? "=== ALL CASES PASS ===" : "=== ONE OR MORE CASES FAILED ===");
return allPass ? 0 : 1;
