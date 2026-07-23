using PKHeX.Core;

// Proves the "Characteristic" flavor-line display (CAPABILITY-GAPS.md Tier B "Characteristic
// string", a tiny read-only addition to the Computed card already shipped on PokemonDetailPage).
//
// EntityCharacteristic.GetCharacteristic(ec, ivs) (PKM/Util/EntityCharacteristic.cs) returns an
// index 0-29 (6 stats * 5 remainder buckets); GameInfo.Strings.characteristics is the matching
// text table ("character" localization key, GameStrings.cs:16/101) - both already exist in
// PKHeX.Core, nothing hand-built here (unlike Dex Entries flavor text, which genuinely isn't in
// the library at all).
//
// Gen3+ only: EncryptionConstant is a hard 0 on Gen1/2 (GBPKM.cs:124, sealed no-op setter) - Gen1/2
// never had a Characteristic mechanic in the real games, so this is a structural gate, not a
// display preference. Gen3-5 alias EncryptionConstant to PID (G3PKM.cs:34 confirmed; a real,
// working value, not a no-op) so the calculation is meaningful there too, contrary to a possible
// assumption that this needs Gen6+ (where EC is independently stored).
//
// GetIVs(Span<int>) writes HP/ATK/DEF/SPE/SPA/SPD (PKM.cs:431-440) - EntityCharacteristic's own
// comment documents needing exactly that order ("HP, Atk, Def, Spe, SpA, SpD"), so no reordering
// is needed between the two.

bool allOk = true;
void Check(string label, bool ok) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {label}"); if (!ok) allOk = false; }

void RunCase(string genLabel, string path, int partySlot, bool expectMeaningful)
{
    Console.WriteLine($"\n=== {genLabel}: {path} ===");
    var sav = SaveUtil.GetSaveFile((byte[])File.ReadAllBytes(path).Clone());
    if (sav is null) { Check($"{genLabel} save parses", false); return; }
    var pk = sav.PartyData[partySlot];

    Span<int> ivs = stackalloc int[6];
    pk.GetIVs(ivs);
    int charIndex = EntityCharacteristic.GetCharacteristic(pk.EncryptionConstant, ivs);
    string text = charIndex >= 0 && charIndex < GameInfo.Strings.characteristics.Length
        ? GameInfo.Strings.characteristics[charIndex]
        : $"(index {charIndex} out of range)";

    Console.WriteLine($"  EC={pk.EncryptionConstant} IVs=[{string.Join(',', ivs.ToArray())}] -> index={charIndex} -> \"{text}\"");
    Check($"{genLabel} characteristic index in valid range [0,29]", charIndex is >= 0 and <= 29);
    Check($"{genLabel} characteristic text resolved (non-empty)", !string.IsNullOrWhiteSpace(text) && !text.StartsWith('('));

    // Sanity cross-check: the stat whose index (maxStatIndex = charIndex / 5) the game picked
    // must actually be tied-for-highest among this mon's own IVs - otherwise the index is bogus.
    int statIndex = charIndex / 5;
    int maxIv = ivs.ToArray().Max();
    Check($"{genLabel} characteristic's stat index ({statIndex}) is actually one of the max-IV stats (IV={ivs[statIndex]}, max={maxIv})",
        ivs[statIndex] == maxIv);

    if (!expectMeaningful)
        Console.WriteLine("  NOTE: Gen1/2 - EncryptionConstant is hard 0 here, so this index/text is a structural artifact, not a real per-mon fact. Correctly gated OFF in the app (Generation >= 3 only).");
}

const string dir = @"C:\Users\abhis\Downloads\sav files pkmn";
RunCase("Gen1", Path.Combine(dir, "POKEMON RED-0.sav"), 0, expectMeaningful: false);
RunCase("Gen2", Path.Combine(dir, "Pokemon - Crystal Version (UE) (V1.1) C!.SAV"), 0, expectMeaningful: false);
RunCase("Gen3", Path.Combine(dir, "pokeemerald (2).sav"), 0, expectMeaningful: true);
RunCase("Gen5", Path.Combine(dir, "Pokemon Black Version.sav"), 0, expectMeaningful: true);
RunCase("Gen9", Path.Combine(dir, "pkmnscarlet_100", "main"), 0, expectMeaningful: true);

Console.WriteLine();
Console.WriteLine(allOk ? "=== ALL CASES PASS ===" : "=== FAILURE ===");
