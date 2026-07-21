using PKHeX.Core;

namespace PkhexMobile;

public sealed record PartyEntryDisplay(int Slot, string SpeciesName, string Nickname, int Level, PKM Source)
{
    public bool IsShiny => Source.IsShiny;
    public string SpriteFile => SpriteHelper.SpeciesSpriteFile(Source.Species, Source.IsShiny);

    public bool HasItem => Source.HeldItem != 0;
    public bool HasNoItem => !HasItem;
    public string ItemSpriteFile => SpriteHelper.ItemSpriteFile(Source.HeldItem);
    public string ItemName => PkmDisplayHelper.GetItemName(Source.HeldItem);
    public string ItemFirstWord => ItemName.Length == 0 ? string.Empty : ItemName.Split(' ')[0];
}
