using System;
using System.IO;
using PKHeX.Core;

// Proves two real, severe bugs found by a Haiku read-only audit and independently confirmed
// against real saves before being fixed in PokemonDetailPage.xaml.cs's OnSaveChangesClicked. Both
// are the same bug class as the DatePicker fix earlier this session: a save silently mutating a
// field/flag the user never touched.
//
// BUG 1 - Nickname/IsNicknamed: the old code always ran `pk.Nickname = NicknameEntry.Text;
// pk.IsNicknamed = true;` unconditionally, every single save. For a real non-nicknamed mon,
// PKM.Nickname already EQUALS its species' default display name (confirmed against real saves -
// every Gen9 Scarlet party mon, most Gen1/Gen3 party mons in this project's test saves), so
// NicknameEntry.Text at load already shows that same default text - meaning ANY unrelated field
// edit (an IV, a PP value, a Met Level...) permanently flipped IsNicknamed false->true on save.
// Fixed by routing through PKHeX.Core's own SpeciesName.IsNicknamed/SetNickname/ClearNickname
// (CommonEdits.cs) - the same canonical split PKHeX Desktop itself uses - evaluated against the
// (possibly new) species so a species change without a matching nickname update correctly
// registers as a real custom nickname.
//
// BUG 2 - CurrentLevel EXP loss: `pk.CurrentLevel = level` ran unconditionally every save.
// PKM.cs:395's setter is `EXP = Experience.GetEXP(level, PersonalInfo.EXPGrowth)` - it unconditionally
// snaps EXP to the EXACT threshold for that level, discarding any real "overshoot" EXP toward the
// next level (very common for an actively-trained mon below level 100). Confirmed with a synthetic
// test: a level-50 mon at ~50% progress toward 51 lost that overshoot down to the exact level-50
// floor on an unconditional same-level reassignment. Fixed by only recomputing EXP when
// species/form/level actually changed (a species/form change still forces the recompute even at
// the same level number, since the same EXP means a different level under a different growth
// curve - see the app's own ordering comment).
//
// Hardcodes local save paths (like BallFriendshipEdit and friends) - excluded from CI.

bool allOk = true;

void Check(string label, bool ok)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {label}");
    if (!ok) allOk = false;
}

// Mirrors the FIXED (baseline-gated) nickname logic in OnSaveChangesClicked. An earlier version
// compared nicknameText against newSpecies's default name UNCONDITIONALLY, which reintroduced the
// same bug class in the opposite direction: a mon genuinely nicknamed exactly its species' default
// name (IsNicknamed=true) got silently flipped to false on an untouched save. Gating on "did
// anything actually change" (species changed, or the text differs from what was loaded) before
// doing the real-vs-default evaluation closes that gap.
void ApplyNickname(PKM pk, ushort newSpecies, bool speciesChanged, string nicknameText)
{
    if (!speciesChanged && nicknameText == pk.Nickname) return;
    if (SpeciesName.IsNicknamed(newSpecies, nicknameText, pk.Language, pk.Format))
        pk.SetNickname(nicknameText);
    else
        pk.ClearNickname();
}

// Mirrors the FIXED level logic in OnSaveChangesClicked.
void ApplyLevel(PKM pk, bool speciesChanged, bool formChanged, byte level)
{
    if (speciesChanged || formChanged || level != pk.CurrentLevel)
        pk.CurrentLevel = level;
}

const string dir = @"C:\Users\abhis\Downloads\sav files pkmn";

void RunUntouchedSaveCase(string genLabel, string path, int partySlot)
{
    Console.WriteLine($"\n=== {genLabel} untouched save: {path} ===");
    var original = File.ReadAllBytes(path);
    var sav = SaveUtil.GetSaveFile((byte[])original.Clone());
    if (sav is null) { Check($"{genLabel} save parses", false); return; }

    var pk = sav.PartyData[partySlot];
    bool nicknamedBefore = pk.IsNicknamed;
    string nickBefore = pk.Nickname;
    uint expBefore = pk.EXP;
    byte levelBefore = pk.CurrentLevel;
    int handlerBefore = pk.CurrentHandler;
    Console.WriteLine($"  Before: IsNicknamed={nicknamedBefore} Nickname=\"{nickBefore}\" Level={levelBefore} EXP={expBefore}");

    // Exactly what the app does for an untouched mon: NicknameEntry.Text/LevelEntry.Text were
    // populated from pk.Nickname/pk.CurrentLevel at load and never edited.
    string nicknameTextFromUi = pk.Nickname;
    byte levelFromUi = pk.CurrentLevel;
    ApplyNickname(pk, pk.Species, speciesChanged: false, nicknameTextFromUi);
    ApplyLevel(pk, speciesChanged: false, formChanged: false, levelFromUi);

    sav.SetPartySlotAtIndex(pk, partySlot, EntityImportSettings.None);
    var bytes = sav.Write().ToArray();
    var reloaded = SaveUtil.GetSaveFile(bytes);
    if (reloaded is null) { Check($"{genLabel} reload after Write()", false); return; }
    var r = reloaded.PartyData[partySlot];
    Console.WriteLine($"  After Write()+reload: IsNicknamed={r.IsNicknamed} Nickname=\"{r.Nickname}\" Level={r.CurrentLevel} EXP={r.EXP} CurrentHandler={r.CurrentHandler}");

    Check($"{genLabel} IsNicknamed unchanged by an untouched save", r.IsNicknamed == nicknamedBefore);
    Check($"{genLabel} Nickname text unchanged by an untouched save", r.Nickname == nickBefore);
    Check($"{genLabel} EXP unchanged by an untouched save (no overshoot loss)", r.EXP == expBefore);
    Check($"{genLabel} CurrentHandler unchanged", r.CurrentHandler == handlerBefore);

    var afterDisk = File.ReadAllBytes(path);
    Check($"{genLabel} original file on disk byte-for-byte unchanged", original.AsSpan().SequenceEqual(afterDisk));
}

RunUntouchedSaveCase("Gen9 Scarlet (non-nicknamed)", Path.Combine(dir, "pkmnscarlet_100", "main"), 0);
RunUntouchedSaveCase("Gen5 Black (already nicknamed)", Path.Combine(dir, "Pokemon Black Version.sav"), 0);
RunUntouchedSaveCase("Gen1 RBY (non-nicknamed)", Path.Combine(dir, "POKEMON RED-0.sav"), 0);
RunUntouchedSaveCase("Gen3 Emerald (non-nicknamed)", Path.Combine(dir, "pokeemerald (2).sav"), 0);

// Regression case for the residual bug the value-comparison version of this fix reintroduced: a
// mon deliberately nicknamed to EXACTLY its own species' default name (IsNicknamed=true) must
// survive an untouched save. A value-comparison ("does the text match the species default?")
// reads this as "not a real nickname" and silently clears IsNicknamed - the same bug class, mirror
// image. The baseline-gated version must leave it alone since the text never changed from load.
{
    Console.WriteLine("\n=== Residual-bug regression: nickname deliberately set to the species' own default text ===");
    var sav = SaveUtil.GetSaveFile((byte[])File.ReadAllBytes(Path.Combine(dir, "pkmnscarlet_100", "main")).Clone())!;
    var pk = sav.PartyData[0];
    var speciesDefault = SpeciesName.GetSpeciesNameGeneration(pk.Species, pk.Language, pk.Format);
    pk.SetNickname(speciesDefault); // simulates a player deliberately typing their species' own name
    Check("Setup: nickname now matches species default text", pk.Nickname == speciesDefault);
    Check("Setup: IsNicknamed is true (a real, deliberate nickname)", pk.IsNicknamed);

    // Untouched save: UI text box was populated from pk.Nickname at load and never edited.
    string nicknameTextFromUi = pk.Nickname;
    ApplyNickname(pk, pk.Species, speciesChanged: false, nicknameTextFromUi);
    Check("Species-name nickname's IsNicknamed survives an untouched save", pk.IsNicknamed);
    Check("Species-name nickname's text survives an untouched save", pk.Nickname == speciesDefault);
}

// Genuine nickname edit: user types a real new nickname on a previously non-nicknamed mon.
{
    Console.WriteLine("\n=== Genuine new nickname on a previously non-nicknamed mon ===");
    var sav = SaveUtil.GetSaveFile((byte[])File.ReadAllBytes(Path.Combine(dir, "pkmnscarlet_100", "main")).Clone())!;
    var pk = sav.PartyData[0];
    Check("Precondition: was not nicknamed", !pk.IsNicknamed);
    ApplyNickname(pk, pk.Species, speciesChanged: false, "MyBuddy");
    Check("Genuine nickname sets IsNicknamed=true", pk.IsNicknamed);
    Check("Genuine nickname text sticks", pk.Nickname == "MyBuddy");
}

// User clears the nickname text back to empty - should cleanly un-nickname, not crash or leave
// an empty string stored.
{
    Console.WriteLine("\n=== Clearing nickname text back to empty ===");
    var sav = SaveUtil.GetSaveFile((byte[])File.ReadAllBytes(Path.Combine(dir, "Pokemon Black Version.sav")).Clone())!;
    var pk = sav.PartyData[0];
    Check("Precondition: was nicknamed", pk.IsNicknamed);
    ApplyNickname(pk, pk.Species, speciesChanged: false, "");
    Check("Clearing nickname sets IsNicknamed=false", !pk.IsNicknamed);
    var expectedDefault = SpeciesName.GetSpeciesNameGeneration(pk.Species, pk.Language, pk.Format);
    Check("Clearing nickname restores the species default text", pk.Nickname == expectedDefault);
}

// Species changed but nickname text left as the OLD species' default - should now register as a
// real custom nickname (matches real in-game/traded-Pokemon behavior).
{
    Console.WriteLine("\n=== Species changed, nickname text left stale ===");
    var sav = SaveUtil.GetSaveFile((byte[])File.ReadAllBytes(Path.Combine(dir, "pkmnscarlet_100", "main")).Clone())!;
    var pk = sav.PartyData[0];
    string staleText = pk.Nickname; // "Skeledirge" - the OLD species' default
    ushort newSpecies = 25; // Pikachu - unrelated species, staleText won't match its default
    ApplyNickname(pk, newSpecies, speciesChanged: true, staleText);
    Check("Stale nickname after a species change registers as a real nickname", pk.IsNicknamed);
    Check("Stale nickname text is preserved as typed", pk.Nickname == staleText);
}

// Explicit level change: EXP should snap to the NEW level's canonical value - this is correct,
// expected behavior (the whole point of a level editor), not something the fix should prevent.
{
    Console.WriteLine("\n=== Explicit level change still recomputes EXP correctly ===");
    var sav = SaveUtil.GetSaveFile((byte[])File.ReadAllBytes(Path.Combine(dir, "Pokemon Heart Gold Version.sav")).Clone())!;
    var pk = sav.PartyData[0];
    byte targetLevel = (byte)(pk.CurrentLevel == 50 ? 60 : 50);
    ApplyLevel(pk, speciesChanged: false, formChanged: false, targetLevel);
    Check("Explicit level change updates CurrentLevel", pk.CurrentLevel == targetLevel);
    Check("Explicit level change lands on the canonical EXP for that level",
        pk.EXP == Experience.GetEXP(targetLevel, pk.PersonalInfo.EXPGrowth));
}

// Species changed with the level NUMBER left unchanged - EXP must still recompute (same EXP under
// a different growth curve means a different level), even though level != pk.CurrentLevel is false.
{
    Console.WriteLine("\n=== Species change with same level number still recomputes EXP ===");
    var sav = SaveUtil.GetSaveFile((byte[])File.ReadAllBytes(Path.Combine(dir, "Pokemon Heart Gold Version.sav")).Clone())!;
    var pk = sav.PartyData[0];
    byte levelFromUi = pk.CurrentLevel; // unchanged number
    pk.Species = 6; // Charizard - different growth-rate group than the original species
    ApplyLevel(pk, speciesChanged: true, formChanged: false, levelFromUi);
    Check("Species change forces EXP recompute even at the same level number",
        pk.CurrentLevel == levelFromUi && pk.EXP == Experience.GetEXP(levelFromUi, pk.PersonalInfo.EXPGrowth));
}

// Synthetic overshoot preservation, isolated from the file round-trip above for a very explicit
// before/after readout.
{
    Console.WriteLine("\n=== Synthetic EXP-overshoot preservation (isolated) ===");
    var sav = SaveUtil.GetSaveFile((byte[])File.ReadAllBytes(Path.Combine(dir, "Pokemon Heart Gold Version.sav")).Clone())!;
    var pk = sav.PartyData[0];
    uint expAt50 = Experience.GetEXP(50, pk.PersonalInfo.EXPGrowth);
    uint expAt51 = Experience.GetEXP(51, pk.PersonalInfo.EXPGrowth);
    uint overshootExp = expAt50 + (expAt51 - expAt50) / 2;
    pk.EXP = overshootExp;
    byte levelFromUi = pk.CurrentLevel; // reads back 50
    ApplyLevel(pk, speciesChanged: false, formChanged: false, levelFromUi);
    Console.WriteLine($"  EXP set to {overshootExp} (halfway through level 50), after guarded same-level reassign: {pk.EXP}");
    Check("Guarded CurrentLevel write preserves EXP overshoot on an untouched level", pk.EXP == overshootExp);
}

Console.WriteLine();
Console.WriteLine(allOk ? "=== ALL CASES PASS ===" : "=== FAILURE ===");
