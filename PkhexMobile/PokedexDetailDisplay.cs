namespace PkhexMobile;

public sealed record EvoNodeDisplay(PokedexService.EvoNode Node, bool IsCurrent)
{
    public string Name => PokedexService.GetSpeciesName(Node.Species);
    public string SpriteFile => SpriteHelper.SpeciesSpriteFile(Node.Species, shiny: false);
    public Thickness IndentMargin => new(Node.Depth * 24, 0, 0, 0);
    public bool HasMethod => !string.IsNullOrEmpty(Node.MethodDescription);
    public string MethodText => Node.MethodDescription ?? string.Empty;
    public bool HasItem => Node.MethodFromItem && Node.ItemId > 0;
    public string ItemSpriteFile => SpriteHelper.ItemSpriteFile(Node.ItemId);
    public string ItemName => PkmDisplayHelper.GetItemName(Node.ItemId);
}

public sealed record FormEntryDisplay(int FormIndex, string Name, ushort SpeciesId)
{
    public bool IsMega => Name.Contains("Mega", StringComparison.OrdinalIgnoreCase);
    public bool IsGmax => Name.Contains("Gmax", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("Gigantamax", StringComparison.OrdinalIgnoreCase);

    // FormIndex here is a position in GetFormNames' de-duplicated cross-context union list, not
    // guaranteed in general to equal the real form byte ItemStorage9ZA expects - but Gen6 (the
    // generation that introduced Mega Evolution) is scanned FIRST when building that union, so for
    // every Mega-capable species its "base/Mega X/Mega Y" entries land at indices 0/1/2 in the
    // union before any later context could introduce unrelated entries, which does match the real
    // byte encoding. If a species/index combination doesn't line up, GetMegaTriggerItem simply
    // returns 0 and the generic fallback note below is used instead of a wrong item name.
    private ushort MegaItemId => IsMega ? PokedexService.GetMegaTriggerItem(SpeciesId, (byte)FormIndex) : (ushort)0;

    public string Note => IsMega
        ? (MegaItemId > 0
            ? $"Mega Evolution - requires holding {PokedexService.GetItemName(MegaItemId)}, used in battle"
            : "Mega Evolution (battle-only form, PKHeX.Core form data)")
        : IsGmax ? "Gigantamax (battle-only form, PKHeX.Core form data)"
        : string.Empty;
    public bool HasNote => Note.Length > 0;
}

public sealed record EncounterRowDisplay(PokedexService.EncounterRow Row)
{
    public string GameLabel => Row.Version.ToString();
    public string LocationLabel => string.IsNullOrEmpty(Row.Location) ? "(location not recorded)" : Row.Location;
    public string LevelLabel => Row.LevelMin == Row.LevelMax ? $"Lv. {Row.LevelMin}" : $"Lv. {Row.LevelMin}-{Row.LevelMax}";
}

public sealed record EncounterCategoryDisplay(string Title, List<EncounterRowDisplay> Rows, string? SummaryOverride)
{
    public bool HasSummaryOverride => SummaryOverride is not null;
    public bool ShowRows => SummaryOverride is null;
    // Capped for on-screen readability - common long-lived species (Pikachu, Eevee) can have 100+
    // genuinely-distinct rows across 9 generations of games. Not artificial data loss (the data
    // layer returns everything; verify/EncounterLocationData proved this), just a display cap.
    private const int MaxShown = 12;
    public List<EncounterRowDisplay> ShownRows => Rows.Count <= MaxShown ? Rows : Rows.Take(MaxShown).ToList();
    public bool HasMore => Rows.Count > MaxShown;
    public string MoreText => $"+ {Rows.Count - MaxShown} more not shown";
}
