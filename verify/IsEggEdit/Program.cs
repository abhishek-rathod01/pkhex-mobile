using System;
using System.IO;
using PKHeX.Core;

// Proves a plain pk.IsEgg toggle (CAPABILITY-GAPS.md Tier B #10, the part not already covered by
// Egg Location/Date in the Origin card) against real saves. Deliberately does NOT call
// ForceHatchPKM/SetEggMetData - those auto-SUGGEST met location/date/version, which conflicts with
// this project's standing "no auto-legalization, no auto-fix" rule (CLAUDE.md SS9). A plain
// pk.IsEgg = value is a direct field write like everything else on PokemonDetailPage - applied
// exactly as chosen, not auto-corrected.
//
// PK1.cs:156 is a hard no-op (get => false; set { } - RBY has no egg mechanic at all - Gen1 Pokemon
// were never obtainable as eggs). PK2.cs:112 is a plain auto-property (real). PK3.cs:147's setter
// has a real in-game side effect worth knowing about: setting IsEgg=true also forces
// Nickname="EggNameJapanese" and Language=Japanese - matching real Gen3 hardware behavior (every
// Gen3 egg displays a hardcoded Japanese nickname regardless of game language, not an app-invented
// auto-fix). Gen4+ (PK4.cs:129 etc.) are a plain IV32 bit flip with no side effect - the "Egg"
// nickname display in Gen4+ is computed client-side from IsEgg at read time, not stored.
// Hardcodes local save paths (like BallFriendshipEdit and friends) - excluded from CI.

bool allOk = true;

void Check(string label, bool ok)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {label}");
    if (!ok) allOk = false;
}

void RunCase(string genLabel, string path, int partySlot, bool expectEditable)
{
    Console.WriteLine($"\n=== {genLabel}: {path} ===");
    var original = File.ReadAllBytes(path);
    var sav = SaveUtil.GetSaveFile((byte[])original.Clone());
    if (sav is null)
    {
        Check($"{genLabel} save parses", false);
        return;
    }

    var pk = sav.PartyData[partySlot];
    int handlerBefore = pk.CurrentHandler;
    bool before = pk.IsEgg;
    Console.WriteLine($"  Before: IsEgg={before} Nickname={pk.Nickname} Language={pk.Language}");

    bool target = !before;
    pk.IsEgg = target;
    Console.WriteLine($"  After in-memory set to {target}: IsEgg={pk.IsEgg} Nickname={pk.Nickname} Language={pk.Language}");

    sav.SetPartySlotAtIndex(pk, partySlot, EntityImportSettings.None);
    var bytes = sav.Write().ToArray();
    var reloaded = SaveUtil.GetSaveFile(bytes);
    if (reloaded is null)
    {
        Check($"{genLabel} reload after Write()", false);
        return;
    }
    var r = reloaded.PartyData[partySlot];
    Console.WriteLine($"  After Write()+reload: IsEgg={r.IsEgg} Nickname={r.Nickname} CurrentHandler={r.CurrentHandler}");

    Check($"{genLabel} IsEgg editable={expectEditable} matches round-trip",
        expectEditable ? r.IsEgg == target : r.IsEgg == before);
    Check($"{genLabel} CurrentHandler unchanged by the write (no silent 'as if traded' side effect)",
        r.CurrentHandler == handlerBefore);

    var afterDisk = File.ReadAllBytes(path);
    Check($"{genLabel} original file on disk byte-for-byte unchanged", original.AsSpan().SequenceEqual(afterDisk));
}

const string dir = @"C:\Users\abhis\Downloads\sav files pkmn";
RunCase("Gen1", Path.Combine(dir, "POKEMON RED-0.sav"), 0, expectEditable: false);
RunCase("Gen2", Path.Combine(dir, "Pokemon - Crystal Version (UE) (V1.1) C!.SAV"), 0, expectEditable: true);
RunCase("Gen3", Path.Combine(dir, "pokeemerald (2).sav"), 0, expectEditable: true);
RunCase("Gen4", Path.Combine(dir, "Pokemon Heart Gold Version.sav"), 0, expectEditable: true);
RunCase("Gen5", Path.Combine(dir, "Pokemon Black Version.sav"), 0, expectEditable: true);
RunCase("Gen9", Path.Combine(dir, "pkmnscarlet_100", "main"), 0, expectEditable: true);

Console.WriteLine();
Console.WriteLine(allOk ? "=== ALL CASES PASS ===" : "=== FAILURE ===");
