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

public sealed record FormEntryDisplay(int FormIndex, string Name)
{
    public bool IsMega => Name.Contains("Mega", StringComparison.OrdinalIgnoreCase);
    public bool IsGmax => Name.Contains("Gmax", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("Gigantamax", StringComparison.OrdinalIgnoreCase);
    public string Note => IsMega ? "Mega Evolution (battle-only form, PKHeX.Core form data)"
        : IsGmax ? "Gigantamax (battle-only form, PKHeX.Core form data)"
        : string.Empty;
    public bool HasNote => Note.Length > 0;
}
