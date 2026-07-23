using System;
using System.IO;
using PKHeX.Core;

// Every field PokemonDetailPage added this session (Gender, PP/PP-Ups, Pokerus, Markings) was
// verified in ISOLATION only (verify/GenderPPEdit, verify/PokerusEdit, verify/MarkingsEdit) plus
// the pre-existing Species/Moves/IVs/EVs/Nature/Ability/Ball/Friendship stack
// (verify/FormNatureAbilityEdit, verify/BallFriendshipEdit). They all funnel through the SAME
// OnSaveChangesClicked -> SetPartySlotAtIndex(EntityImportSettings.None) -> Write() write path.
// This harness stacks several of them on ONE mon in ONE save operation - replicating the app's
// exact field-application order - to catch a cross-feature interaction that isolated tests can't
// see: does one field's write clobber another's, does ResetPartyStats (triggered by the IV change)
// disturb Gender/Pokerus/Markings, and does the EntityImportSettings.None guard still hold with
// every new field stacked in (no CurrentHandler flip / no fabricated Handling Trainer data).
// Hardcodes local save paths (like verify/BallFriendshipEdit and friends) - excluded from CI.

bool allOk = true;

void Check(string label, bool ok)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {label}");
    if (!ok) allOk = false;
}

void RunCase(string genLabel, string path, int partySlot, bool expectGenderEditable, bool expectPokerusEditable, int ivCap)
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
    int statAtkBefore = pk.Stat_ATK;
    Console.WriteLine($"  Before: Gender={pk.Gender} Pokerus={pk.PokerusStrain}/{pk.PokerusDays} " +
        $"Move1_PP={pk.Move1_PP}/{pk.Move1_PPUps} IV_ATK={pk.IV_ATK} Stat_ATK={pk.Stat_ATK} CurrentHandler={handlerBefore}");

    // Markings: mirror PopulateMarkings's interface probe exactly.
    bool marksAreColor = pk is IAppliedMarkings<MarkingColor>;
    bool marksAreBool = !marksAreColor && pk is IAppliedMarkings<bool>;
    int markingsCount = marksAreColor ? ((IAppliedMarkings<MarkingColor>)pk).MarkingCount
                       : marksAreBool ? ((IAppliedMarkings<bool>)pk).MarkingCount
                       : 0;

    // Target values, all distinct from whatever is currently stored.
    byte targetGender = (byte)(pk.Gender == 0 ? 1 : 0);
    int targetPokerusStrain = pk.PokerusStrain == 5 ? 3 : 5;
    int targetPokerusDays = pk.PokerusDays == 2 ? 1 : 2;
    int targetMove1Pp = pk.Move1_PP == 10 ? 20 : 10;
    int targetMove1PpUps = pk.Move1_PPUps == 1 ? 2 : 1;
    byte targetIvAtk = (byte)(pk.IV_ATK == ivCap ? 0 : ivCap);

    // Apply in the SAME ORDER as PokemonDetailPage.OnSaveChangesClicked: Gender -> Pokerus ->
    // Markings -> PP/PP-Ups -> IVs -> ResetPartyStats (gated on statsAffected) -> SetPartySlotAtIndex.
    if (expectGenderEditable)
        pk.Gender = targetGender;

    if (expectPokerusEditable)
    {
        pk.PokerusStrain = targetPokerusStrain;
        pk.PokerusDays = targetPokerusDays;
    }

    if (marksAreColor)
    {
        var colorMarks = (IAppliedMarkings<MarkingColor>)pk;
        for (int i = 0; i < markingsCount; i++)
            colorMarks.SetMarking(i, i % 2 == 0 ? MarkingColor.Blue : MarkingColor.None);
    }
    else if (marksAreBool)
    {
        var boolMarks = (IAppliedMarkings<bool>)pk;
        for (int i = 0; i < markingsCount; i++)
            boolMarks.SetMarking(i, i % 2 == 0);
    }

    pk.Move1_PP = targetMove1Pp;
    pk.Move1_PPUps = targetMove1PpUps;

    pk.IV_ATK = targetIvAtk; // always real storage from Gen1 on - forces statsAffected below

    pk.ResetPartyStats();

    sav.SetPartySlotAtIndex(pk, partySlot, EntityImportSettings.None);
    var bytes = sav.Write().ToArray();
    var reloaded = SaveUtil.GetSaveFile(bytes);
    if (reloaded is null)
    {
        Check($"{genLabel} reload after Write()", false);
        return;
    }
    var r = reloaded.PartyData[partySlot];
    Console.WriteLine($"  After Write()+reload: Gender={r.Gender} Pokerus={r.PokerusStrain}/{r.PokerusDays} " +
        $"Move1_PP={r.Move1_PP}/{r.Move1_PPUps} IV_ATK={r.IV_ATK} Stat_ATK={r.Stat_ATK} CurrentHandler={r.CurrentHandler}");

    if (expectGenderEditable)
        Check($"{genLabel} Gender round-trips", r.Gender == targetGender);
    if (expectPokerusEditable)
        Check($"{genLabel} Pokerus round-trips", r.PokerusStrain == targetPokerusStrain && r.PokerusDays == targetPokerusDays);

    if (marksAreColor)
    {
        var rMarks = (IAppliedMarkings<MarkingColor>)r;
        bool ok = true;
        for (int i = 0; i < markingsCount; i++)
            ok &= rMarks.GetMarking(i) == (i % 2 == 0 ? MarkingColor.Blue : MarkingColor.None);
        Check($"{genLabel} Color markings round-trip", ok);
    }
    else if (marksAreBool)
    {
        var rMarks = (IAppliedMarkings<bool>)r;
        bool ok = true;
        for (int i = 0; i < markingsCount; i++)
            ok &= rMarks.GetMarking(i) == (i % 2 == 0);
        Check($"{genLabel} Bool markings round-trip", ok);
    }

    Check($"{genLabel} PP round-trips", r.Move1_PP == targetMove1Pp && r.Move1_PPUps == targetMove1PpUps);
    Check($"{genLabel} IV_ATK round-trips", r.IV_ATK == targetIvAtk);
    Check($"{genLabel} Stat_ATK actually recomputed off the new IV (not stale)", r.Stat_ATK != statAtkBefore);
    Check($"{genLabel} CurrentHandler unchanged by the combined write (no silent 'as if traded' side effect)",
        r.CurrentHandler == handlerBefore);

    // Legality must recompute without throwing on a mon carrying every new field at once.
    Exception? legalityError = null;
    try
    {
        var la = new LegalityAnalysis(r);
        _ = la.Report();
    }
    catch (Exception ex)
    {
        legalityError = ex;
    }
    Check($"{genLabel} LegalityAnalysis does not throw on the combined-edit mon", legalityError is null);
    if (legalityError is not null)
        Console.WriteLine($"    Exception: {legalityError}");

    var afterDisk = File.ReadAllBytes(path);
    Check($"{genLabel} original file on disk byte-for-byte unchanged", original.AsSpan().SequenceEqual(afterDisk));
}

const string dir = @"C:\Users\abhis\Downloads\sav files pkmn";
RunCase("Gen1", Path.Combine(dir, "POKEMON RED-0.sav"), 0, expectGenderEditable: false, expectPokerusEditable: false, ivCap: 15);
RunCase("Gen2", Path.Combine(dir, "Pokemon - Crystal Version (UE) (V1.1) C!.SAV"), 0, expectGenderEditable: false, expectPokerusEditable: true, ivCap: 15);
RunCase("Gen3", Path.Combine(dir, "pokeemerald (2).sav"), 0, expectGenderEditable: false, expectPokerusEditable: true, ivCap: 31);
RunCase("Gen5", Path.Combine(dir, "Pokemon Black Version.sav"), 0, expectGenderEditable: true, expectPokerusEditable: true, ivCap: 31);
RunCase("Gen9", Path.Combine(dir, "pkmnscarlet_100", "main"), 0, expectGenderEditable: true, expectPokerusEditable: true, ivCap: 31);

Console.WriteLine();
Console.WriteLine(allOk ? "=== ALL CASES PASS ===" : "=== FAILURE ===");
