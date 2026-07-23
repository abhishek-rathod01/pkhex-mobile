using System;
using System.IO;
using PKHeX.Core;

// Proves the Gender + manual PP/PP-Ups editors, and the three read-only "Computed" card additions
// (final battle stats, species type(s), Hidden Power type) against real saves.
//
// Gender: replicates the app's write path (pk.Gender -> SetPartySlotAtIndex -> Write() ->
// SaveUtil.GetSaveFile(byte[])) and confirms the per-generation split from CAPABILITY-GAPS.md -
// Gen1/2 derived from IVs (GBPKM.cs, hard no-op setter), Gen3 PID-derived (G3PKM.cs, hard no-op
// setter), Gen4+ real independent storage (PK4.cs Data[0x40]).
//
// PP/PP-Ups: pk.MoveN_PP / pk.MoveN_PPUps are uniform abstract members across every format (no
// per-generation split) - one round-trip case is enough to prove the write path, run against all
// five generations anyway since they're cheap and this project's precedent is "verify per
// generation, don't assume."
//
// Computed stats / type / Hidden Power are read-only display additions - sanity-checked for
// plausible values (no exceptions, stats > 0, type indices in range) rather than exact round-trip,
// since nothing is written to the save for these three.
//
// Hardcodes local save paths (like verify/BallFriendshipEdit and friends) - excluded from CI.

bool allOk = true;

void Check(string label, bool ok)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {label}");
    if (!ok) allOk = false;
}

void RunGenderCase(string genLabel, string path, int partySlot, bool expectGenderEditable)
{
    Console.WriteLine($"\n=== Gender - {genLabel}: {path} ===");
    var original = File.ReadAllBytes(path);
    var sav = SaveUtil.GetSaveFile((byte[])original.Clone());
    if (sav is null) { Check($"{genLabel} save parses", false); return; }

    var pk = sav.PartyData[partySlot];
    byte before = pk.Gender;
    byte target = (byte)(before == 0 ? 1 : 0); // Male <-> Female (never picks 2/Genderless, so this is always a real flip attempt)

    pk.Gender = target;
    bool tookEffect = pk.Gender == target;
    Console.WriteLine($"  Before={before} target={target} after in-memory set={pk.Gender} (editable expected={expectGenderEditable})");
    Check($"{genLabel} Gender editable={expectGenderEditable} matches actual took-effect={tookEffect}", tookEffect == expectGenderEditable);

    sav.SetPartySlotAtIndex(pk, partySlot, EntityImportSettings.None);
    var bytes = sav.Write().ToArray();
    var reloaded = SaveUtil.GetSaveFile(bytes);
    if (reloaded is null) { Check($"{genLabel} reload after Write()", false); return; }
    var reloadedPk = reloaded.PartyData[partySlot];

    byte expectedAfter = expectGenderEditable ? target : reloadedPk.Gender; // no-op: whatever the format derives, just must be stable
    Console.WriteLine($"  After Write()+reload: Gender={reloadedPk.Gender}");
    Check($"{genLabel} Gender round-trips through Write()+reload", reloadedPk.Gender == expectedAfter);

    if (!expectGenderEditable)
        Check($"{genLabel} Gender no-op did not silently change the derived value across save/reload", reloadedPk.Gender == before);

    var afterDisk = File.ReadAllBytes(path);
    Check($"{genLabel} original file on disk byte-for-byte unchanged", original.AsSpan().SequenceEqual(afterDisk));
}

void RunPPCase(string genLabel, string path, int partySlot)
{
    Console.WriteLine($"\n=== PP/PP-Ups - {genLabel}: {path} ===");
    var original = File.ReadAllBytes(path);
    var sav = SaveUtil.GetSaveFile((byte[])original.Clone());
    if (sav is null) { Check($"{genLabel} save parses", false); return; }

    var pk = sav.PartyData[partySlot];
    if (pk.Move1 == 0) { Check($"{genLabel} party slot 0 has a move in slot 1 to test", false); return; }

    int targetUps = pk.Move1_PPUps == 2 ? 1 : 2;
    int maxPp = pk.GetMovePP(pk.Move1, targetUps);
    int targetPp = Math.Max(0, maxPp - 1); // deliberately not full, so a real (non-maxed) value round-trips

    pk.Move1_PPUps = targetUps;
    pk.Move1_PP = targetPp;

    Console.WriteLine($"  Set Move1_PPUps={targetUps} Move1_PP={targetPp} (max at that PPUps={maxPp})");
    Check($"{genLabel} Move1_PPUps took effect in-memory", pk.Move1_PPUps == targetUps);
    Check($"{genLabel} Move1_PP took effect in-memory", pk.Move1_PP == targetPp);

    sav.SetPartySlotAtIndex(pk, partySlot, EntityImportSettings.None);
    var bytes = sav.Write().ToArray();
    var reloaded = SaveUtil.GetSaveFile(bytes);
    if (reloaded is null) { Check($"{genLabel} reload after Write()", false); return; }
    var reloadedPk = reloaded.PartyData[partySlot];

    Console.WriteLine($"  After Write()+reload: Move1_PPUps={reloadedPk.Move1_PPUps} Move1_PP={reloadedPk.Move1_PP}");
    Check($"{genLabel} Move1_PPUps round-trips through Write()+reload", reloadedPk.Move1_PPUps == targetUps);
    Check($"{genLabel} Move1_PP round-trips through Write()+reload", reloadedPk.Move1_PP == targetPp);

    var afterDisk = File.ReadAllBytes(path);
    Check($"{genLabel} original file on disk byte-for-byte unchanged", original.AsSpan().SequenceEqual(afterDisk));
}

void RunComputedDisplayCase(string genLabel, string path, int partySlot)
{
    Console.WriteLine($"\n=== Computed display - {genLabel}: {path} ===");
    var sav = SaveUtil.GetSaveFile((byte[])File.ReadAllBytes(path).Clone());
    if (sav is null) { Check($"{genLabel} save parses", false); return; }
    var pk = sav.PartyData[partySlot];

    // Final battle stats: PKM.GetStats(IBaseStat) computes fresh from the PKM's OWN current
    // IV/EV/level/nature fields - it does not read/require the stored Stat_* block, so it works
    // identically for a party mon (already has one) and a box mon (may be zeroed, per CLAUDE.md's
    // "box slots can carry a stale/zeroed party stat block" trap) without special-casing either.
    var stats = pk.GetStats(pk.PersonalInfo);
    Console.WriteLine($"  Stats (HP/Atk/Def/Spe/SpA/SpD): {string.Join('/', stats.ToArray())}");
    Check($"{genLabel} computed HP > 0", stats[0] > 0);
    Check($"{genLabel} computed stats has 6 entries", stats.Length == 6);

    // Species type: pk.PersonalInfo.Type1/Type2 is a per-SAVE-FORMAT raw byte and NOT safe to use
    // directly for Gen1 (PersonalInfo1.Type1 = Data[0x06], the game's own internal type-ID table,
    // which has unused gaps like the "Bird" type slot and does not match PKHeX's modern 0-17
    // MoveType order - confirmed by a real crash here on first run, GameInfo.Strings.Types[t1]
    // throwing ArgumentOutOfRangeException for a Gen1 Pokemon). Instead, reuse the SAME technique
    // the already-shipped, on-device-verified Pokedex feature uses: PersonalTable.SV, keyed by
    // species/form, which is always in the modern type-byte space regardless of the PKM's own
    // format. This deliberately shows the CURRENT-games type (e.g. Fairy for Clefairy), not
    // necessarily the exact historical type in that Pokemon's origin generation (a few species were
    // retyped in later gens) - same tradeoff the Pokedex feature already made and documented.
    var pi = PersonalTable.SV.GetFormEntry(pk.Species, pk.Form);
    byte t1 = pi.Type1;
    byte t2 = pi.Type2;
    Console.WriteLine($"  Types (current-gen table): {GameInfo.Strings.Types[t1]} / {(t2 != t1 ? GameInfo.Strings.Types[t2] : "(none)")}");
    Check($"{genLabel} Type1 in range", t1 < GameInfo.Strings.Types.Count);

    // Hidden Power: only shown for Generation >= 3 here. ShowdownSet.cs's own "1 + HiddenPowerType
    // // skip Normal" comment confirms the +1 offset for the Gen3+ path; the GB (Gen1/2) branch
    // (HiddenPower.GetTypeGB) is a different raw encoding never actually exercised by ShowdownSet
    // (Showdown format doesn't cover Gen1/2), so whether the same +1 offset is correct there is
    // unverified in the library itself - not worth asserting. Gen1 doesn't have the Hidden Power
    // move at all (introduced Gen2), so excluding it there costs nothing.
    if (pk.Generation >= 3)
    {
        Span<int> ivs = stackalloc int[6];
        pk.GetIVs(ivs);
        int hpRaw = HiddenPower.GetType(ivs, pk.Context);
        int hpTypeIndex = 1 + hpRaw;
        Console.WriteLine($"  Hidden Power: raw={hpRaw} -> type={GameInfo.Strings.Types[hpTypeIndex]}");
        Check($"{genLabel} Hidden Power type index in range and never Normal", hpTypeIndex >= 1 && hpTypeIndex < GameInfo.Strings.Types.Count);
    }
    else
    {
        Console.WriteLine("  Hidden Power: not shown (Gen1 has no Hidden Power move)");
    }
}

const string dir = @"C:\Users\abhis\Downloads\sav files pkmn";

RunGenderCase("Gen1", Path.Combine(dir, "POKEMON RED-0.sav"), 0, expectGenderEditable: false);
RunGenderCase("Gen2", Path.Combine(dir, "Pokemon - Crystal Version (UE) (V1.1) C!.SAV"), 0, expectGenderEditable: false);
RunGenderCase("Gen3", Path.Combine(dir, "pokeemerald (2).sav"), 0, expectGenderEditable: false);
RunGenderCase("Gen5", Path.Combine(dir, "Pokemon Black Version.sav"), 0, expectGenderEditable: true);
RunGenderCase("Gen9", Path.Combine(dir, "pkmnscarlet_100", "main"), 0, expectGenderEditable: true);

RunPPCase("Gen1", Path.Combine(dir, "POKEMON RED-0.sav"), 0);
RunPPCase("Gen2", Path.Combine(dir, "Pokemon - Crystal Version (UE) (V1.1) C!.SAV"), 0);
RunPPCase("Gen3", Path.Combine(dir, "pokeemerald (2).sav"), 0);
RunPPCase("Gen5", Path.Combine(dir, "Pokemon Black Version.sav"), 0);
RunPPCase("Gen9", Path.Combine(dir, "pkmnscarlet_100", "main"), 0);

RunComputedDisplayCase("Gen1", Path.Combine(dir, "POKEMON RED-0.sav"), 0);
RunComputedDisplayCase("Gen2", Path.Combine(dir, "Pokemon - Crystal Version (UE) (V1.1) C!.SAV"), 0);
RunComputedDisplayCase("Gen3", Path.Combine(dir, "pokeemerald (2).sav"), 0);
RunComputedDisplayCase("Gen5", Path.Combine(dir, "Pokemon Black Version.sav"), 0);
RunComputedDisplayCase("Gen9", Path.Combine(dir, "pkmnscarlet_100", "main"), 0);

Console.WriteLine();
Console.WriteLine(allOk ? "=== ALL CASES PASS ===" : "=== FAILURE ===");
