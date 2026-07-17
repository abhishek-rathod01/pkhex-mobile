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
}
