namespace PkhexMobile;

public sealed record PokedexEntryDisplay(int SpeciesId, string Name, int Generation)
{
    public string DexNumberText => $"#{SpeciesId:D4}";
    public string SpriteFile => SpriteHelper.SpeciesSpriteFile((ushort)SpeciesId, shiny: false);
    public string GenerationText => $"Gen {Generation}";
}
