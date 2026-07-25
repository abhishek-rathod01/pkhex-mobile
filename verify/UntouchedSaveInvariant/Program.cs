using System;
using System.IO;
using System.Linq;
using PKHeX.Core;

// The general-purpose version of the "untouched save must not mutate anything" checks this
// session already wrote one-field-at-a-time (DatePicker, Nickname, CurrentLevel). Per an advisor
// review: none of those harnesses assert full identity on a completely untouched round-trip - they
// only check the one field each was written to catch. This harness mirrors the ENTIRE
// OnSaveChangesClicked write path (PokemonDetailPage.xaml.cs) in one place, feeding every "new*"
// value from pk's OWN current value (simulating a user who opened the mon and changed nothing),
// and asserts every field the real method touches is bit-identical after Write()+reload - the
// invariant that would have caught all three bugs found this session in one sweep, plus anything
// not yet found.
//
// Deliberately forces every format-capability gate (formEditable, abilityEditable, etc.) to `true`
// rather than replicating PokemonDetailPage's per-generation PopulateXxx detection logic - since
// every "new*" value equals pk's current value, forcing an assignment through is either a true
// no-op (the field isn't actually editable in this generation and PKHeX.Core's own setter is a
// documented no-op, e.g. Gen3 Ability) or a same-value reassignment (which is exactly what this
// harness is trying to test the safety of). The naturally delta-gated assignments (Nickname,
// CurrentLevel, Met/Egg date) are NOT forced - they use the real method's own value-comparison
// logic unmodified, since forcing those would defeat the point of testing them.
//
// Hardcodes local save paths - excluded from CI, same as BallFriendshipEdit and friends.

bool allOk = true;

void Check(string label, bool ok)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {label}");
    if (!ok) allOk = false;
}

// Mirrors OnSaveChangesClicked's full write path for an UNTOUCHED mon: every "new*" is read
// directly from pk's current state (as if the UI displayed it and the user changed nothing).
void ApplyUntouchedSave(PKM pk)
{
    ushort newSpecies = pk.Species;
    ushort[] newMoves = { pk.Move1, pk.Move2, pk.Move3, pk.Move4 };
    byte newForm = pk.Form;
    int newAbility = pk.Ability;
    Nature newNature = pk.Nature;
    int newHeldItem = pk.HeldItem;
    int newBall = pk.Ball;
    int newFriendship = pk.CurrentFriendship;
    byte newGender = pk.Gender;
    int newPokerusStrain = pk.PokerusStrain;
    int newPokerusDays = pk.PokerusDays;
    bool newIsEgg = pk.IsEgg;
    GameVersion newVersion = pk.Version;
    ushort newMetLocation = pk.MetLocation;
    byte newMetLevel = pk.MetLevel;
    byte newMetYear = pk.MetYear, newMetMonth = pk.MetMonth, newMetDay = pk.MetDay;
    ushort newEggLocation = pk.EggLocation;
    byte newEggYear = pk.EggYear, newEggMonth = pk.EggMonth, newEggDay = pk.EggDay;
    byte level = pk.CurrentLevel;
    int ivHp = pk.IV_HP, ivAtk = pk.IV_ATK, ivDef = pk.IV_DEF, ivSpa = pk.IV_SPA, ivSpd = pk.IV_SPD, ivSpe = pk.IV_SPE;
    int evHp = pk.EV_HP, evAtk = pk.EV_ATK, evDef = pk.EV_DEF, evSpa = pk.EV_SPA, evSpd = pk.EV_SPD, evSpe = pk.EV_SPE;
    int move1Pp = pk.Move1_PP, move1PpUps = pk.Move1_PPUps;
    int move2Pp = pk.Move2_PP, move2PpUps = pk.Move2_PPUps;
    int move3Pp = pk.Move3_PP, move3PpUps = pk.Move3_PPUps;
    int move4Pp = pk.Move4_PP, move4PpUps = pk.Move4_PPUps;

    // Real DatePicker baselines: SafeDate(0,0,0) -> (2000,1,1) for an unset date, otherwise the
    // real stored date - matching PokemonDetailPage's PopulateOrigin exactly so this guard
    // evaluates the same way the real UI would for an untouched mon.
    DateTime SafeDate(byte y, byte m, byte d) => new(2000 + y, Math.Clamp((int)m, 1, 12), Math.Clamp((int)d, 1, DateTime.DaysInMonth(2000 + y, Math.Clamp((int)m, 1, 12))));
    var metDateBaseline = SafeDate(pk.MetYear, pk.MetMonth, pk.MetDay);
    var eggDateBaseline = SafeDate(pk.EggYear, pk.EggMonth, pk.EggDay);
    var d = metDateBaseline; // untouched DatePicker.Date
    var ed = eggDateBaseline;

    bool speciesChanged = newSpecies != pk.Species;
    bool formChanged = newForm != pk.Form;

    var nicknameText = pk.Nickname;
    if (speciesChanged || nicknameText != pk.Nickname)
    {
        if (SpeciesName.IsNicknamed(newSpecies, nicknameText, pk.Language, pk.Format))
            pk.SetNickname(nicknameText);
        else
            pk.ClearNickname();
    }

    pk.Species = newSpecies;
    pk.Form = newForm;
    if (speciesChanged || formChanged || level != pk.CurrentLevel)
        pk.CurrentLevel = level;
    pk.SetMoves(newMoves);

    pk.Ability = newAbility;
    pk.Nature = newNature;
    if (pk.Format >= 8)
        pk.StatAlignment = newNature;
    pk.ApplyHeldItem(newHeldItem, pk.Context);
    pk.Ball = (byte)newBall;
    pk.CurrentFriendship = (byte)newFriendship;
    pk.Gender = newGender;
    pk.PokerusStrain = newPokerusStrain;
    pk.PokerusDays = newPokerusDays;
    pk.IsEgg = newIsEgg;
    pk.Version = newVersion;
    pk.MetLocation = newMetLocation;
    pk.MetLevel = newMetLevel;
    if (d != metDateBaseline)
    {
        pk.MetYear = (byte)(d.Year - 2000);
        pk.MetMonth = (byte)d.Month;
        pk.MetDay = (byte)d.Day;
    }
    pk.EggLocation = newEggLocation;
    if (ed != eggDateBaseline)
    {
        pk.EggYear = (byte)(ed.Year - 2000);
        pk.EggMonth = (byte)ed.Month;
        pk.EggDay = (byte)ed.Day;
    }

    if (pk is IAppliedMarkings<MarkingColor> colorMarks)
    {
        int n = colorMarks.MarkingCount;
        for (int i = 0; i < n; i++)
            colorMarks.SetMarking(i, colorMarks.GetMarking(i));
    }
    else if (pk is IAppliedMarkings<bool> boolMarks)
    {
        int n = boolMarks.MarkingCount;
        for (int i = 0; i < n; i++)
            boolMarks.SetMarking(i, boolMarks.GetMarking(i));
    }

    pk.Move1_PP = move1Pp; pk.Move1_PPUps = move1PpUps;
    pk.Move2_PP = move2Pp; pk.Move2_PPUps = move2PpUps;
    pk.Move3_PP = move3Pp; pk.Move3_PPUps = move3PpUps;
    pk.Move4_PP = move4Pp; pk.Move4_PPUps = move4PpUps;

    pk.IV_HP = ivHp; pk.IV_ATK = ivAtk; pk.IV_DEF = ivDef; pk.IV_SPA = ivSpa; pk.IV_SPD = ivSpd; pk.IV_SPE = ivSpe;
    pk.EV_HP = evHp; pk.EV_ATK = evAtk; pk.EV_DEF = evDef; pk.EV_SPA = evSpa; pk.EV_SPD = evSpd; pk.EV_SPE = evSpe;

    bool statsAffected = speciesChanged || formChanged || level != pk.CurrentLevel ||
                         ivHp != pk.IV_HP || ivAtk != pk.IV_ATK || ivDef != pk.IV_DEF ||
                         ivSpa != pk.IV_SPA || ivSpd != pk.IV_SPD || ivSpe != pk.IV_SPE ||
                         evHp != pk.EV_HP || evAtk != pk.EV_ATK || evDef != pk.EV_DEF ||
                         evSpa != pk.EV_SPA || evSpd != pk.EV_SPD || evSpe != pk.EV_SPE;
    if (statsAffected)
        pk.ResetPartyStats();
}

const string dir = @"C:\Users\abhis\Downloads\sav files pkmn";

void RunCase(string genLabel, string path, int slot)
{
    Console.WriteLine($"\n=== {genLabel}: {path} (party slot {slot}) ===");
    var original = File.ReadAllBytes(path);
    var sav = SaveUtil.GetSaveFile((byte[])original.Clone());
    if (sav is null) { Check($"{genLabel} save parses", false); return; }

    var pk = sav.PartyData[slot];
    if (pk is null || pk.Species == 0) { Check($"{genLabel} slot {slot} has a mon", false); return; }

    // Full before-snapshot of every field the write path touches.
    var before = new
    {
        pk.Nickname, pk.IsNicknamed, pk.Species, pk.Form, pk.CurrentLevel, pk.EXP,
        pk.Move1, pk.Move2, pk.Move3, pk.Move4,
        pk.Move1_PP, pk.Move1_PPUps, pk.Move2_PP, pk.Move2_PPUps,
        pk.Move3_PP, pk.Move3_PPUps, pk.Move4_PP, pk.Move4_PPUps,
        pk.Ability, pk.Nature, pk.HeldItem, pk.Ball, pk.CurrentFriendship, pk.Gender,
        pk.PokerusStrain, pk.PokerusDays, pk.IsEgg, pk.Version,
        pk.MetLocation, pk.MetLevel, pk.MetYear, pk.MetMonth, pk.MetDay,
        pk.EggLocation, pk.EggYear, pk.EggMonth, pk.EggDay,
        pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPA, pk.IV_SPD, pk.IV_SPE,
        pk.EV_HP, pk.EV_ATK, pk.EV_DEF, pk.EV_SPA, pk.EV_SPD, pk.EV_SPE,
        pk.CurrentHandler, pk.Stat_HPMax, pk.Stat_ATK,
        StatAlign = pk.Format >= 8 ? pk.StatAlignment : (Nature)255,
    };
    int markingCountBefore = pk is IAppliedMarkings<MarkingColor> cmb ? cmb.MarkingCount
        : pk is IAppliedMarkings<bool> bmb ? bmb.MarkingCount : 0;
    var markingsBefore = Enumerable.Range(0, markingCountBefore)
        .Select(i => pk is IAppliedMarkings<MarkingColor> cm ? (int)cm.GetMarking(i)
               : pk is IAppliedMarkings<bool> bm ? (bm.GetMarking(i) ? 1 : 0) : -1)
        .ToArray();

    ApplyUntouchedSave(pk);

    sav.SetPartySlotAtIndex(pk, slot, EntityImportSettings.None);

    var bytes = sav.Write().ToArray();
    var reloaded = SaveUtil.GetSaveFile(bytes);
    if (reloaded is null) { Check($"{genLabel} reload after Write()", false); return; }
    var r = reloaded.PartyData[slot];
    if (r is null) { Check($"{genLabel} reloaded slot has a mon", false); return; }

    var after = new
    {
        r.Nickname, r.IsNicknamed, r.Species, r.Form, r.CurrentLevel, r.EXP,
        r.Move1, r.Move2, r.Move3, r.Move4,
        r.Move1_PP, r.Move1_PPUps, r.Move2_PP, r.Move2_PPUps,
        r.Move3_PP, r.Move3_PPUps, r.Move4_PP, r.Move4_PPUps,
        r.Ability, r.Nature, r.HeldItem, r.Ball, r.CurrentFriendship, r.Gender,
        r.PokerusStrain, r.PokerusDays, r.IsEgg, r.Version,
        r.MetLocation, r.MetLevel, r.MetYear, r.MetMonth, r.MetDay,
        r.EggLocation, r.EggYear, r.EggMonth, r.EggDay,
        r.IV_HP, r.IV_ATK, r.IV_DEF, r.IV_SPA, r.IV_SPD, r.IV_SPE,
        r.EV_HP, r.EV_ATK, r.EV_DEF, r.EV_SPA, r.EV_SPD, r.EV_SPE,
        r.CurrentHandler, r.Stat_HPMax, r.Stat_ATK,
        StatAlign = r.Format >= 8 ? r.StatAlignment : (Nature)255,
    };
    int markingCountAfter = r is IAppliedMarkings<MarkingColor> cma ? cma.MarkingCount
        : r is IAppliedMarkings<bool> bma ? bma.MarkingCount : 0;
    var markingsAfter = Enumerable.Range(0, markingCountAfter)
        .Select(i => r is IAppliedMarkings<MarkingColor> cm ? (int)cm.GetMarking(i)
               : r is IAppliedMarkings<bool> bm ? (bm.GetMarking(i) ? 1 : 0) : -1)
        .ToArray();

    if (!before.Equals(after))
        Console.WriteLine($"  BEFORE: {before}\n  AFTER:  {after}");
    Check($"{genLabel} full field identity on an untouched save", before.Equals(after));
    Check($"{genLabel} markings identity on an untouched save", markingsBefore.SequenceEqual(markingsAfter));

    var afterDisk = File.ReadAllBytes(path);
    Check($"{genLabel} original file on disk byte-for-byte unchanged", original.AsSpan().SequenceEqual(afterDisk));
}

RunCase("Gen9 Scarlet party[0]", Path.Combine(dir, "pkmnscarlet_100", "main"), 0);
RunCase("Gen5 Black party[0]", Path.Combine(dir, "Pokemon Black Version.sav"), 0);
RunCase("Gen4 HeartGold party[0]", Path.Combine(dir, "Pokemon Heart Gold Version.sav"), 0);
RunCase("Gen3 Emerald party[0]", Path.Combine(dir, "pokeemerald (2).sav"), 0);
RunCase("Gen1 RBY party[0]", Path.Combine(dir, "POKEMON RED-0.sav"), 0);

Console.WriteLine();
Console.WriteLine(allOk ? "=== ALL CASES PASS ===" : "=== FAILURE ===");
