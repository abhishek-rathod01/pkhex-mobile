using System;
using System.IO;
using PKHeX.Core;

// Proves the Pokerus Strain/Days editor (CAPABILITY-GAPS.md Tier B item #13) against real saves -
// replicates the app's write path (pk.PokerusStrain/PokerusDays -> SetPartySlotAtIndex -> Write()
// -> SaveUtil.GetSaveFile(byte[])) and confirms the per-generation editable/no-op split:
// Gen1 hard no-op (PK1.cs:149-150, get => 0; set { } - RBY has no Pokerus mechanic at all),
// Gen2+ real, packed as one byte (upper nibble = strain, lower nibble = days - PK2.cs:83-84,
// PK3.cs:129-130, PK9.cs:152-153 all share this exact bit layout across every generation checked).
//
// Note: Pokerus.IsObtainable (Editing/Pokerus.cs) says the true in-game mechanic is absent from
// PB7 (Let's Go), PK9 (Scarlet/Violet), and PA9 (Legends Z-A) - none of those games' wild
// encounters/breeding ever produce a nonzero value, and HOME doesn't transfer it in. The PK9
// storage bytes are nonetheless real and independently read/written (confirmed below) - this is a
// plausibility fact for the read-only LegalityAnalysis badge to flag, not a storage no-op, so
// Gen9 is NOT specially gated in the app: same "applied as-is, legality reported not enforced"
// stance as every other editable field on this page (Nature/Gender/Ability/etc.).
//
// Hardcodes local save paths (like verify/BallFriendshipEdit and friends) - excluded from CI.

bool allOk = true;
void Check(string label, bool ok) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {label}"); if (!ok) allOk = false; }

void RunCase(string genLabel, string path, int partySlot, bool expectEditable)
{
    Console.WriteLine($"\n=== {genLabel}: {path} ===");
    var original = File.ReadAllBytes(path);
    var sav = SaveUtil.GetSaveFile((byte[])original.Clone());
    if (sav is null) { Check($"{genLabel} save parses", false); return; }
    var pk = sav.PartyData[partySlot];

    Console.WriteLine($"  Before: PokerusStrain={pk.PokerusStrain} PokerusDays={pk.PokerusDays}");
    int targetStrain = pk.PokerusStrain == 4 ? 2 : 4;
    int targetDays = pk.PokerusDays == 1 ? 3 : 1;

    pk.PokerusStrain = targetStrain;
    pk.PokerusDays = targetDays;

    bool strainTookEffect = pk.PokerusStrain == targetStrain;
    bool daysTookEffect = pk.PokerusDays == targetDays;
    Console.WriteLine($"  After in-memory set: PokerusStrain={pk.PokerusStrain} (target {targetStrain}) PokerusDays={pk.PokerusDays} (target {targetDays})");
    Check($"{genLabel} Strain editable={expectEditable} matches actual took-effect={strainTookEffect}", strainTookEffect == expectEditable);
    Check($"{genLabel} Days editable={expectEditable} matches actual took-effect={daysTookEffect}", daysTookEffect == expectEditable);

    sav.SetPartySlotAtIndex(pk, partySlot, EntityImportSettings.None);
    var bytes = sav.Write().ToArray();
    var reloaded = SaveUtil.GetSaveFile(bytes);
    if (reloaded is null) { Check($"{genLabel} reload after Write()", false); return; }
    var reloadedPk = reloaded.PartyData[partySlot];

    int expectedStrainAfter = expectEditable ? targetStrain : reloadedPk.PokerusStrain;
    int expectedDaysAfter = expectEditable ? targetDays : reloadedPk.PokerusDays;
    Console.WriteLine($"  After Write()+reload: PokerusStrain={reloadedPk.PokerusStrain} PokerusDays={reloadedPk.PokerusDays}");
    Check($"{genLabel} Strain round-trips through Write()+reload", reloadedPk.PokerusStrain == expectedStrainAfter);
    Check($"{genLabel} Days round-trips through Write()+reload", reloadedPk.PokerusDays == expectedDaysAfter);

    var afterDisk = File.ReadAllBytes(path);
    Check($"{genLabel} original file on disk byte-for-byte unchanged", original.AsSpan().SequenceEqual(afterDisk));
}

const string dir = @"C:\Users\abhis\Downloads\sav files pkmn";
RunCase("Gen1", Path.Combine(dir, "POKEMON RED-0.sav"), 0, expectEditable: false);
RunCase("Gen2", Path.Combine(dir, "Pokemon - Crystal Version (UE) (V1.1) C!.SAV"), 0, expectEditable: true);
RunCase("Gen3", Path.Combine(dir, "pokeemerald (2).sav"), 0, expectEditable: true);
RunCase("Gen5", Path.Combine(dir, "Pokemon Black Version.sav"), 0, expectEditable: true);
RunCase("Gen9", Path.Combine(dir, "pkmnscarlet_100", "main"), 0, expectEditable: true);

Console.WriteLine();
Console.WriteLine(allOk ? "=== ALL CASES PASS ===" : "=== FAILURE ===");
