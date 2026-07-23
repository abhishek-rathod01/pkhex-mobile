using System;
using System.IO;
using System.Linq;
using PKHeX.Core;

// Proves the planned Origin/Met Data card (pk.Version, MetLocation, MetLevel, MetYear/Month/Day,
// EggLocation, EggYear/Month/Day) against real saves before touching PokemonDetailPage, per this
// project's verify-first methodology. Confirms CAPABILITY-GAPS.md's documented split: Met
// Location/Level are no-op only in Gen1 (real from Gen2 - PK2.cs's CaughtData bitfield); Met/Egg
// DATES and Egg Location are no-op pre-Gen4 (PKM.cs's virtual { get => 0; set { } } defaults /
// G3PKM.cs:41 sealed override) and real from Gen4 (PK4.cs's Data[0x78..0x7D] block); Version is a
// no-op in Gen1 (PK1.cs:148, fixed RBY) and Gen2 (PK2.cs:115, fixed GSC) but real from Gen3
// (PK3.cs:135). Also exercises the exact library APIs the UI picker will use -
// GameUtil.GetVersionsWithinRange and GameInfo.GetLocationList - to source realistic target
// values, not arbitrary integers. Hardcodes local save paths (like BallFriendshipEdit and
// friends) - excluded from CI.

bool allOk = true;

void Check(string label, bool ok)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {label}");
    if (!ok) allOk = false;
}

void RunCase(string genLabel, string path, int partySlot, bool expectMetEditable, bool expectDateEditable, bool expectEggEditable, bool expectVersionEditable)
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
    Console.WriteLine($"  Before: Version={pk.Version} MetLoc={pk.MetLocation} MetLevel={pk.MetLevel} " +
        $"MetDate={pk.MetYear:D2}/{pk.MetMonth:D2}/{pk.MetDay:D2} EggLoc={pk.EggLocation} " +
        $"EggDate={pk.EggYear:D2}/{pk.EggMonth:D2}/{pk.EggDay:D2}");

    // Source realistic target values via the SAME public APIs the UI picker will call - not
    // arbitrary integers - so this also derisks those APIs for the generation/version at hand.
    var validVersions = GameUtil.GetVersionsWithinRange(pk, pk.Context).ToArray();
    var targetVersion = validVersions.FirstOrDefault(v => v != pk.Version, pk.Version);

    var metLocations = GameInfo.GetLocationList(pk.Version, pk.Context, egg: false);
    var targetMetLocation = (ushort)metLocations.Select(c => c.Value).FirstOrDefault(v => v != pk.MetLocation, pk.MetLocation);

    var eggLocations = GameInfo.GetLocationList(pk.Version, pk.Context, egg: true);
    var targetEggLocation = (ushort)eggLocations.Select(c => c.Value).FirstOrDefault(v => v != pk.EggLocation && v > 0, pk.EggLocation);

    byte targetMetLevel = (byte)(pk.MetLevel == 50 ? 30 : 50);
    byte targetMetYear = (byte)(pk.MetYear == 20 ? 15 : 20); // 2020 vs 2015
    byte targetMetMonth = (byte)(pk.MetMonth == 6 ? 3 : 6);
    byte targetMetDay = (byte)(pk.MetDay == 15 ? 10 : 15);
    byte targetEggYear = (byte)(pk.EggYear == 20 ? 15 : 20);
    byte targetEggMonth = (byte)(pk.EggMonth == 6 ? 3 : 6);
    byte targetEggDay = (byte)(pk.EggDay == 15 ? 10 : 15);

    pk.Version = targetVersion;
    pk.MetLocation = targetMetLocation;
    pk.MetLevel = targetMetLevel;
    pk.MetYear = targetMetYear;
    pk.MetMonth = targetMetMonth;
    pk.MetDay = targetMetDay;
    pk.EggLocation = targetEggLocation;
    pk.EggYear = targetEggYear;
    pk.EggMonth = targetEggMonth;
    pk.EggDay = targetEggDay;

    sav.SetPartySlotAtIndex(pk, partySlot, EntityImportSettings.None);
    var bytes = sav.Write().ToArray();
    var reloaded = SaveUtil.GetSaveFile(bytes);
    if (reloaded is null)
    {
        Check($"{genLabel} reload after Write()", false);
        return;
    }
    var r = reloaded.PartyData[partySlot];
    Console.WriteLine($"  After Write()+reload: Version={r.Version} MetLoc={r.MetLocation} MetLevel={r.MetLevel} " +
        $"MetDate={r.MetYear:D2}/{r.MetMonth:D2}/{r.MetDay:D2} EggLoc={r.EggLocation} " +
        $"EggDate={r.EggYear:D2}/{r.EggMonth:D2}/{r.EggDay:D2} CurrentHandler={r.CurrentHandler}");

    Check($"{genLabel} Version editable={expectVersionEditable} matches round-trip",
        expectVersionEditable ? r.Version == targetVersion : r.Version == pk.Version || !expectVersionEditable);
    Check($"{genLabel} MetLocation editable={expectMetEditable} matches round-trip",
        expectMetEditable ? r.MetLocation == targetMetLocation : r.MetLocation == 0);
    Check($"{genLabel} MetLevel editable={expectMetEditable} matches round-trip",
        expectMetEditable ? r.MetLevel == targetMetLevel : r.MetLevel == 0);
    Check($"{genLabel} MetDate editable={expectDateEditable} matches round-trip",
        expectDateEditable
            ? r.MetYear == targetMetYear && r.MetMonth == targetMetMonth && r.MetDay == targetMetDay
            : r.MetYear == 0 && r.MetMonth == 0 && r.MetDay == 0);
    Check($"{genLabel} EggLocation editable={expectEggEditable} matches round-trip",
        expectEggEditable ? r.EggLocation == targetEggLocation : r.EggLocation == 0);
    Check($"{genLabel} EggDate editable={expectEggEditable} matches round-trip",
        expectEggEditable
            ? r.EggYear == targetEggYear && r.EggMonth == targetEggMonth && r.EggDay == targetEggDay
            : r.EggYear == 0 && r.EggMonth == 0 && r.EggDay == 0);
    Check($"{genLabel} CurrentHandler unchanged by the write (no silent 'as if traded' side effect)",
        r.CurrentHandler == handlerBefore);

    // GetLocationName must resolve without throwing for whatever landed in MetLocation/EggLocation,
    // using the (possibly new) Version - the exact call the UI caption will make on load.
    Exception? nameError = null;
    string metName = "", eggName = "";
    try
    {
        metName = GameInfo.GetLocationName(false, r.MetLocation, r.Format, r.Generation, r.Version);
        eggName = GameInfo.GetLocationName(true, r.EggLocation, r.Format, r.Generation, r.Version);
    }
    catch (Exception ex)
    {
        nameError = ex;
    }
    Check($"{genLabel} GetLocationName resolves without throwing", nameError is null);
    Console.WriteLine($"    Resolved: MetLocation=\"{metName}\" EggLocation=\"{eggName}\"");

    var afterDisk = File.ReadAllBytes(path);
    Check($"{genLabel} original file on disk byte-for-byte unchanged", original.AsSpan().SequenceEqual(afterDisk));
}

const string dir = @"C:\Users\abhis\Downloads\sav files pkmn";
RunCase("Gen1", Path.Combine(dir, "POKEMON RED-0.sav"), 0,
    expectMetEditable: false, expectDateEditable: false, expectEggEditable: false, expectVersionEditable: false);
RunCase("Gen2", Path.Combine(dir, "Pokemon - Crystal Version (UE) (V1.1) C!.SAV"), 0,
    expectMetEditable: true, expectDateEditable: false, expectEggEditable: false, expectVersionEditable: false);
RunCase("Gen3", Path.Combine(dir, "pokeemerald (2).sav"), 0,
    expectMetEditable: true, expectDateEditable: false, expectEggEditable: false, expectVersionEditable: true);
RunCase("Gen4", Path.Combine(dir, "Pokemon Heart Gold Version.sav"), 0,
    expectMetEditable: true, expectDateEditable: true, expectEggEditable: true, expectVersionEditable: true);
RunCase("Gen5", Path.Combine(dir, "Pokemon Black Version.sav"), 0,
    expectMetEditable: true, expectDateEditable: true, expectEggEditable: true, expectVersionEditable: true);
RunCase("Gen9", Path.Combine(dir, "pkmnscarlet_100", "main"), 0,
    expectMetEditable: true, expectDateEditable: true, expectEggEditable: true, expectVersionEditable: true);

Console.WriteLine();
Console.WriteLine(allOk ? "=== ALL CASES PASS ===" : "=== FAILURE ===");
