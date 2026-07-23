using PKHeX.Core;

// Proof-of-concept for a planned Pokedex feature: "where can I catch this species, in which
// games, at which locations/levels, and what does a Mega Evolution need." Answers three open
// questions before any UI gets built (see PROGRESS.md when this lands):
//
//   1. Is there a public species -> encounters API, or do we have to hand-invert the internal
//      per-generation area tables ourselves? -> YES: EncounterMovesetGenerator.GenerateEncounters
//      (PUBLIC, used by PKHeX's own "Encounter Database" feature) takes a rough PKM (species/form
//      set, nothing else) + an empty moves list, and yields every possible IEncounterable for that
//      species in the games reachable from the input pk's own EntityContext.
//   2. Location byte -> real name: GameInfo.Strings.GetLocationName(isEgg, location, format,
//      generation, version) - PUBLIC. Must be called with the ENCOUNTER's OWN generation, not the
//      scanning context's generation - a real bug caught here: resolving a Gen3 Ruby/Sapphire egg's
//      location ID using generation=9 (the scanning context) produced a nonsense modern Paldea
//      location name for an old game.
//   3. Mega Stone -> species/form: ItemStorage9ZA.GetExpectedMegaStoneOrPrimalOrb(species, form) -
//      PUBLIC, a complete real table, returns 0 for non-Mega species/forms.
//
// Two layers of duplication had to be found and collapsed, both real, both empirical:
//   (a) GameUtil.GetVersionsWithinRange special-cases "HOME era" contexts (Gen8+): it returns the
//       PKM format's full ID range unrestricted rather than limiting to that context's own
//       generation, so Gen8/Gen8a/Gen8b/Gen9 all resolve to nearly the same modern HOME-connected
//       window - scanning all four separately re-discovers the same encounters ~4x over. Fixed:
//       scan the HOME-connected block ONCE via the most current context (Gen9).
//   (b) Independent of that, "Egg" (and some Static) encounters from an EARLY generation are
//       legitimately still a valid explanation for a Pokemon currently in a LATER generation's
//       format (bred in Ruby, transferred forward) - so the same Gen3 fact gets rediscovered once
//       per later generation-context scanned, even after fix (a). Fixed: dedup GLOBALLY across
//       every context scanned, not just within one context's own results.
//
// Test species: Charizard (Megas X/Y, multi-gen history), Eevee (branching evolutions, high
// encounter-method diversity), Mewtwo (legendary, static-only), Piplup (starter, gift-only, never
// a wild slot), Pikachu (extremely common wild encounter across many games - a volume stress test).

var contexts = new EntityContext[]
{
    EntityContext.Gen1, EntityContext.Gen2, EntityContext.Gen3, EntityContext.Gen4,
    EntityContext.Gen5, EntityContext.Gen6, EntityContext.Gen7, EntityContext.Gen7b,
    EntityContext.Gen9, // covers the whole Gen8+/HOME-connected block in one query
};

void PrintEncountersForSpecies(ushort species, byte form = 0)
{
    Console.WriteLine($"\n=== Species {species} ({GameInfo.Strings.Species[species]}) form {form} ===");

    var allRows = new List<(GameVersion Version, string Category, string Location, byte LevelMin, byte LevelMax)>();

    foreach (var context in contexts)
    {
        PKM pk;
        try { pk = EntityBlank.GetBlank(context); }
        catch (Exception ex) { Console.WriteLine($"  [{context}] EntityBlank.GetBlank threw {ex.GetType().Name}: {ex.Message}"); continue; }

        if (species > pk.MaxSpeciesID)
            continue; // this format structurally cannot represent this species at all - not an error

        pk.Species = species;
        pk.Form = form;

        List<IEncounterable> found;
        try { found = EncounterMovesetGenerator.GenerateEncounters(pk, ReadOnlyMemory<ushort>.Empty).ToList(); }
        catch (Exception ex) { Console.WriteLine($"  [{context}] GenerateEncounters threw {ex.GetType().Name}: {ex.Message}"); continue; }

        foreach (var enc in found)
        {
            string loc = GameInfo.Strings.GetLocationName(false, enc.Location, (byte)enc.Version.Generation, (byte)enc.Version.Generation, enc.Version);
            if (string.IsNullOrWhiteSpace(loc) || loc is "(None)" or "——————")
                loc = "(no location recorded)";
            allRows.Add((enc.Version, Categorize(enc), loc, enc.LevelMin, enc.LevelMax));
        }
    }

    var grouped = allRows
        .GroupBy(r => (r.Version, r.Category, r.Location, r.LevelMin, r.LevelMax))
        .OrderBy(g => g.Key.Version).ThenBy(g => g.Key.Category)
        .ToList();

    Console.WriteLine($"  {allRows.Count} raw encounter records -> {grouped.Count} deduped rows -> bucketed by category:");
    foreach (var catGroup in grouped.GroupBy(g => g.Key.Category).OrderBy(c => c.Key))
    {
        var rows = catGroup.ToList();
        if (catGroup.Key == "Event/Gift")
        {
            // Historical distribution events are individually real but far too numerous/granular
            // to list one-by-one in a UI - collapse to a single summary line.
            Console.WriteLine($"  [{catGroup.Key}] {rows.Count} distinct past distribution event(s) across {rows.Select(r => r.Key.Version).Distinct().Count()} game(s) - collapse to one summary line in the UI: \"Available via N past Mystery Gift events (no longer obtainable)\"");
            continue;
        }
        Console.WriteLine($"  [{catGroup.Key}] {rows.Count} row(s):");
        foreach (var g in rows.Take(8))
        {
            var k = g.Key;
            string countSuffix = g.Count() > 1 ? $" x{g.Count()}" : "";
            Console.WriteLine($"    Version={k.Version,-10} Location=\"{k.Location}\" Level={k.LevelMin}-{k.LevelMax}{countSuffix}");
        }
        if (rows.Count > 8)
            Console.WriteLine($"    ... and {rows.Count - 8} more");
    }
}

static string Categorize(IEncounterable enc)
{
    if (enc is IEncounterEgg)
        return "Egg";
    if (enc is MysteryGift)
        return "Event/Gift";
    if (enc.Name.Contains("Trade"))
        return "Trade";
    if (enc.Name.Contains("Raid"))
        return "Raid";
    if (enc.Name.Contains("Wild Encounter") || enc.Name.Contains("GO Encounter") ||
        enc.Name.Contains("Entree Forest") || enc.Name.Contains("Dream Radar") || enc.Name.Contains("Pokéwalker"))
        return "Wild";
    return "Static/Gift";
}

PrintEncountersForSpecies(6);   // Charizard
PrintEncountersForSpecies(133); // Eevee
PrintEncountersForSpecies(150); // Mewtwo
PrintEncountersForSpecies(393); // Piplup (starter, gift-only)
PrintEncountersForSpecies(25);  // Pikachu

Console.WriteLine("\n=== Mega Stone / Primal Orb mapping (ItemStorage9ZA.GetExpectedMegaStoneOrPrimalOrb) ===");
void PrintMegaItem(ushort species, byte form, string label)
{
    ushort itemId = ItemStorage9ZA.GetExpectedMegaStoneOrPrimalOrb(species, form);
    string itemName = itemId == 0 ? "(none)" : GameInfo.Strings.Item[itemId];
    Console.WriteLine($"  {label} (species={species} form={form}): item {itemId} = \"{itemName}\"");
}
PrintMegaItem(6, 0, "Charizard base");   // expect 0
PrintMegaItem(6, 1, "Charizard Mega X"); // expect Charizardite X
PrintMegaItem(6, 2, "Charizard Mega Y"); // expect Charizardite Y
PrintMegaItem(150, 1, "Mewtwo Mega X");  // expect Mewtwonite X
PrintMegaItem(150, 2, "Mewtwo Mega Y");  // expect Mewtwonite Y
PrintMegaItem(383, 1, "Groudon Primal"); // expect Red Orb
PrintMegaItem(382, 1, "Kyogre Primal");  // expect Blue Orb

Console.WriteLine("\nDone.");
