using PKHeX.Core;

namespace PkhexMobile;

public static class PkmDisplayHelper
{
    public static string GetSpeciesName(ushort species)
    {
        var list = GameInfo.Strings.Species;
        return species < list.Count ? list[species] : $"#{species}";
    }

    public static string GetDisplayName(PKM pk) => string.IsNullOrWhiteSpace(pk.Nickname) ? GetSpeciesName(pk.Species) : pk.Nickname;

    public static string GetMoveName(ushort move)
    {
        var list = GameInfo.Strings.Move;
        return move != 0 && move < list.Count ? list[move] : "—";
    }

    public static string GetAbilityName(int ability)
    {
        var list = GameInfo.Strings.Ability;
        return ability > 0 && ability < list.Count ? list[ability] : "—";
    }

    public static string GetNatureName(Nature nature)
    {
        var list = GameInfo.Strings.Natures;
        return (int)nature < list.Count ? list[(int)nature] : nature.ToString();
    }

    public static string GetItemName(int item)
    {
        var list = GameInfo.Strings.Item;
        return item > 0 && item < list.Count ? list[item] : string.Empty;
    }
}
