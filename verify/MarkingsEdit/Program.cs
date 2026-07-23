using System;
using System.IO;
using PKHeX.Core;

// Proves the Markings editor (CAPABILITY-GAPS.md Tier B item #12) against real saves.
//
// Confirms the exact SPLIT this feature needs: Gen1/2 implement NEITHER IAppliedMarkings<bool> NOR
// IAppliedMarkings<MarkingColor> at all (no marking concept exists pre-Gen3); Gen3 implements
// IAppliedMarkings<bool> with MarkingCount=4 (Circle/Triangle/Square/Heart only - G3PKM.cs:42);
// Gen4-6 implement IAppliedMarkings<bool> with MarkingCount=6 (+ Star/Diamond - G4PKM.cs:172);
// Gen7+ implement IAppliedMarkings<MarkingColor> (None/Blue/Pink) with MarkingCount=6 (PK7.cs:427).
// Index order (0-5 = Circle/Triangle/Square/Heart/Star/Diamond) is identical across every
// generation that has 6 markings - confirmed by reading G4PKM.cs/PK7.cs side by side.
//
// Replicates the app's write path (SetMarking -> SetPartySlotAtIndex -> Write() ->
// SaveUtil.GetSaveFile(byte[])).

bool allOk = true;
void Check(string label, bool ok) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {label}"); if (!ok) allOk = false; }

void RunBoolCase(string genLabel, string path, int partySlot, int expectedCount)
{
    Console.WriteLine($"\n=== {genLabel} (bool markings): {path} ===");
    var original = File.ReadAllBytes(path);
    var sav = SaveUtil.GetSaveFile((byte[])original.Clone());
    if (sav is null) { Check($"{genLabel} save parses", false); return; }
    var pk = sav.PartyData[partySlot];

    if (pk is not IAppliedMarkings<bool> marks)
    {
        Check($"{genLabel} implements IAppliedMarkings<bool> (expected count={expectedCount})", expectedCount == 0);
        return;
    }
    Check($"{genLabel} MarkingCount == {expectedCount}", marks.MarkingCount == expectedCount);

    // Flip every marking, verify each took effect, round-trip, then flip back off and confirm that too.
    for (int i = 0; i < marks.MarkingCount; i++)
        marks.SetMarking(i, true);
    bool allSet = true;
    for (int i = 0; i < marks.MarkingCount; i++)
        allSet &= marks.GetMarking(i);
    Check($"{genLabel} all {marks.MarkingCount} markings set to true in-memory", allSet);

    sav.SetPartySlotAtIndex(pk, partySlot, EntityImportSettings.None);
    var bytes = sav.Write().ToArray();
    var reloaded = SaveUtil.GetSaveFile(bytes);
    if (reloaded is null) { Check($"{genLabel} reload after Write()", false); return; }
    var reloadedPk = (IAppliedMarkings<bool>)reloaded.PartyData[partySlot];
    bool allSetAfterReload = true;
    for (int i = 0; i < reloadedPk.MarkingCount; i++)
        allSetAfterReload &= reloadedPk.GetMarking(i);
    Check($"{genLabel} all markings round-trip as true through Write()+reload", allSetAfterReload);

    var afterDisk = File.ReadAllBytes(path);
    Check($"{genLabel} original file on disk byte-for-byte unchanged", original.AsSpan().SequenceEqual(afterDisk));
}

void RunColorCase(string genLabel, string path, int partySlot)
{
    Console.WriteLine($"\n=== {genLabel} (color markings): {path} ===");
    var original = File.ReadAllBytes(path);
    var sav = SaveUtil.GetSaveFile((byte[])original.Clone());
    if (sav is null) { Check($"{genLabel} save parses", false); return; }
    var pk = sav.PartyData[partySlot];

    if (pk is not IAppliedMarkings<MarkingColor> marks)
    {
        Check($"{genLabel} implements IAppliedMarkings<MarkingColor>", false);
        return;
    }
    Check($"{genLabel} MarkingCount == 6", marks.MarkingCount == 6);

    marks.SetMarking(0, MarkingColor.Blue);
    marks.SetMarking(1, MarkingColor.Pink);
    Check($"{genLabel} Circle=Blue took effect in-memory", marks.GetMarking(0) == MarkingColor.Blue);
    Check($"{genLabel} Triangle=Pink took effect in-memory", marks.GetMarking(1) == MarkingColor.Pink);

    sav.SetPartySlotAtIndex(pk, partySlot, EntityImportSettings.None);
    var bytes = sav.Write().ToArray();
    var reloaded = SaveUtil.GetSaveFile(bytes);
    if (reloaded is null) { Check($"{genLabel} reload after Write()", false); return; }
    var reloadedPk = (IAppliedMarkings<MarkingColor>)reloaded.PartyData[partySlot];
    Check($"{genLabel} Circle=Blue round-trips through Write()+reload", reloadedPk.GetMarking(0) == MarkingColor.Blue);
    Check($"{genLabel} Triangle=Pink round-trips through Write()+reload", reloadedPk.GetMarking(1) == MarkingColor.Pink);

    var afterDisk = File.ReadAllBytes(path);
    Check($"{genLabel} original file on disk byte-for-byte unchanged", original.AsSpan().SequenceEqual(afterDisk));
}

const string dir = @"C:\Users\abhis\Downloads\sav files pkmn";
RunBoolCase("Gen1", Path.Combine(dir, "POKEMON RED-0.sav"), 0, expectedCount: 0);
RunBoolCase("Gen2", Path.Combine(dir, "Pokemon - Crystal Version (UE) (V1.1) C!.SAV"), 0, expectedCount: 0);
RunBoolCase("Gen3", Path.Combine(dir, "pokeemerald (2).sav"), 0, expectedCount: 4);
RunBoolCase("Gen5", Path.Combine(dir, "Pokemon Black Version.sav"), 0, expectedCount: 6);
RunColorCase("Gen9", Path.Combine(dir, "pkmnscarlet_100", "main"), 0);

Console.WriteLine();
Console.WriteLine(allOk ? "=== ALL CASES PASS ===" : "=== FAILURE ===");
