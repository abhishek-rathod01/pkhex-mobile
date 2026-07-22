using PKHeX.Core;

namespace PkhexMobile;

/// <summary>
/// Reference-only Pokedex data. Pulls base stats/types/abilities/forms/evolutions straight from
/// PKHeX.Core (PersonalTable.SV + EvolutionTree.Evolves9) rather than reimplementing anything -
/// SV's PersonalTable covers the full National Dex #1-1025 (format conversion needs stat data for
/// every species regardless of catchability in-game), so it's a single complete source rather than
/// per-generation branching. Not tied to a loaded save file.
/// </summary>
public static class PokedexService
{
    public const int MaxSpecies = 1025;

    // National Dex ID of the last species in each generation (RBY..SV/ZA), used only to label a
    // species with its debut generation for the list-screen filter - not used for stat lookups.
    private static readonly (int LastId, int Gen)[] GenerationBreaks =
    [
        (151, 1), (251, 2), (386, 3), (493, 4), (649, 5), (721, 6), (809, 7), (905, 8), (1025, 9),
    ];

    public static int GetGeneration(int speciesId)
    {
        foreach (var (lastId, gen) in GenerationBreaks)
        {
            if (speciesId <= lastId)
                return gen;
        }
        return 9;
    }

    public static IReadOnlyList<int> AllSpeciesIds { get; } = [.. Enumerable.Range(1, MaxSpecies)];

    public static string GetSpeciesName(int speciesId) => PkmDisplayHelper.GetSpeciesName((ushort)speciesId);

    public static PersonalInfo9SV GetPersonalInfo(ushort species, byte form = 0) =>
        PersonalTable.SV.GetFormEntry(species, form);

    // FormConverter.GetFormList is context-gated to match real game rules (e.g. Mega Evolution
    // doesn't exist in Scarlet/Violet, Gigantamax doesn't exist outside Sword/Shield) - there is no
    // single context that returns the union of every form a species has ever had. Queried across
    // one context per era (verified via EntityContextExtensions.IsMegaContext / FormConverter's own
    // "context.Generation >= 8" gates) and de-duplicated by name, so this screen shows every
    // Mega/Gmax/regional form PKHeX.Core knows about for the species, not just whichever one
    // generation happens to expose.
    private static readonly EntityContext[] FormContexts =
    [
        EntityContext.Gen6, EntityContext.Gen7, EntityContext.Gen7b,
        EntityContext.Gen8, EntityContext.Gen8a, EntityContext.Gen8b,
        EntityContext.Gen9, EntityContext.Gen9a,
    ];

    /// <summary>
    /// Union of form display names for a species across every era PKHeX.Core models, de-duplicated.
    /// Not tied to a loaded PKM's real Form index - reference display only.
    /// </summary>
    public static string[] GetFormNames(ushort species)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var ctx in FormContexts)
        {
            string[] names;
            try
            {
                names = FormConverter.GetFormList(species, GameInfo.Strings.Types, GameInfo.Strings.forms, ctx);
            }
            catch
            {
                continue; // some species/context combinations aren't modeled - skip rather than fail the whole card
            }
            foreach (var n in names)
            {
                string key = string.IsNullOrEmpty(n) ? "\0base" : n;
                if (seen.Add(key))
                    result.Add(n);
            }
        }
        return result.Count == 0 ? [string.Empty] : [.. result];
    }

    public static (byte Type1, byte Type2, bool HasSecondType) GetTypeIds(ushort species, byte form = 0)
    {
        var pi = GetPersonalInfo(species, form);
        return (pi.Type1, pi.Type2, pi.Type1 != pi.Type2);
    }

    // Type byte -> Colors.xaml "Type*" token name, in PKHeX's OWN type-byte order (verified in
    // verify/MoveTypes). Duplicated from PokemonDetailPage's private TypeColorKeys rather than
    // shared - both derive from the same verified source, and this table is cheap/stable enough
    // that a shared helper isn't worth the extra indirection for two call sites.
    private static readonly string[] TypeColorKeys =
    [
        "Normal", "Fighting", "Flying", "Poison", "Ground", "Rock", "Bug", "Ghost", "Steel",
        "Fire", "Water", "Grass", "Electric", "Psychic", "Ice", "Dragon", "Dark", "Fairy",
    ];

    public static string GetTypeColorKey(byte typeId) => typeId < TypeColorKeys.Length ? TypeColorKeys[typeId] : "Normal";
    public static string GetTypeName(byte typeId) => typeId < GameInfo.Strings.Types.Count ? GameInfo.Strings.Types[typeId] : $"Type {typeId}";

    public readonly record struct AbilityEntry(string Name, bool IsHidden);

    /// <summary>
    /// PersonalInfo9SV.AbilityCount is always 3 (Ability1/Ability2/AbilityH) even when a species has
    /// no true second ability (Ability1 == Ability2 in that case) - dedupe here so the UI doesn't
    /// show the same ability name twice.
    /// </summary>
    public static List<AbilityEntry> GetAbilities(ushort species, byte form = 0)
    {
        var pi = GetPersonalInfo(species, form);
        var result = new List<AbilityEntry>();
        var seen = new HashSet<int>();
        for (int i = 0; i < pi.AbilityCount; i++)
        {
            int abilityId = pi.GetAbilityAtIndex(i);
            if (abilityId == 0 || !seen.Add(abilityId))
                continue;
            result.Add(new AbilityEntry(PkmDisplayHelper.GetAbilityName(abilityId), IsHidden: i == 2));
        }
        return result;
    }

    public readonly record struct BaseStats(int HP, int Atk, int Def, int SpA, int SpD, int Spe)
    {
        public int Total => HP + Atk + Def + SpA + SpD + Spe;
    }

    public static BaseStats GetBaseStats(ushort species, byte form = 0)
    {
        var pi = GetPersonalInfo(species, form);
        return new BaseStats(pi.HP, pi.ATK, pi.DEF, pi.SPA, pi.SPD, pi.SPE);
    }

    public readonly record struct EvoNode(ushort Species, byte Form, int Depth, string? MethodDescription, bool MethodFromItem, int ItemId);

    /// <summary>
    /// Full evolution family (ancestors, the queried species, and every descendant branch -
    /// including branching evolutions like Eevee and per-form branches like regional-form
    /// evolution lines) sourced from <see cref="EvolutionTree.Evolves9"/>, the Scarlet/Violet
    /// tree - chosen because it is PKHeX.Core's most complete/current forward+reverse table.
    /// Judgement call, per this task's instructions: source is PKHeX.Core's own evolution data,
    /// not PokeAPI - stated here so the source is auditable.
    /// </summary>
    public static List<EvoNode> GetEvolutionChain(ushort species, byte form)
    {
        var tree = EvolutionTree.Evolves9;
        var root = tree.GetBaseSpeciesForm(species, form);

        var nodes = new List<EvoNode>();
        var visited = new HashSet<(ushort, byte)>();
        void Walk(ushort s, byte f, int depth)
        {
            if (!visited.Add((s, f)))
                return; // cycle guard
            nodes.Add(new EvoNode(s, f, depth, null, false, 0));
            var forwardMethods = tree.Forward.GetForward(s, f).ToArray();
            foreach (var (nextSpecies, nextForm) in tree.Forward.GetEvolutions(s, f))
            {
                var method = forwardMethods.FirstOrDefault(m => m.Species == nextSpecies && m.GetDestinationForm(f) == nextForm);
                var (desc, isItem, itemId) = DescribeMethod(method);
                Walk(nextSpecies, nextForm, depth + 1);
                // Overwrite the just-added descendant node's method description now that we know
                // which edge reached it (Walk adds the node with a null description first so the
                // recursion/cycle-guard logic above stays uniform for every node, root included).
                int idx = nodes.FindIndex(n => n.Species == nextSpecies && n.Form == nextForm && n.Depth == depth + 1);
                if (idx >= 0)
                    nodes[idx] = nodes[idx] with { MethodDescription = desc, MethodFromItem = isItem, ItemId = itemId };
            }
        }
        Walk(root.Species, root.Form, 0);
        return nodes;
    }

    private static (string Description, bool IsItem, int ItemId) DescribeMethod(EvolutionMethod m)
    {
        string ItemName(int id) => id > 0 && id < GameInfo.Strings.Item.Count ? GameInfo.Strings.Item[id] : $"item #{id}";

        return m.Method switch
        {
            EvolutionType.None => ("", false, 0),
            EvolutionType.LevelUp => ($"Level {m.Level}", false, 0),
            EvolutionType.LevelUpFriendship => ("Level up (high friendship)", false, 0),
            EvolutionType.LevelUpFriendshipMorning => ("Level up (high friendship, morning)", false, 0),
            EvolutionType.LevelUpFriendshipNight => ("Level up (high friendship, night)", false, 0),
            EvolutionType.Trade => ("Trade", false, 0),
            EvolutionType.TradeHeldItem => ($"Trade holding {ItemName(m.Argument)}", true, m.Argument),
            EvolutionType.TradeShelmetKarrablast => ("Trade for Shelmet/Karrablast", false, 0),
            EvolutionType.UseItem => ($"Use {ItemName(m.Argument)}", true, m.Argument),
            EvolutionType.UseItemMale => ($"Use {ItemName(m.Argument)} (male)", true, m.Argument),
            EvolutionType.UseItemFemale => ($"Use {ItemName(m.Argument)} (female)", true, m.Argument),
            EvolutionType.UseItemWormhole => ($"Use {ItemName(m.Argument)} (Ultra Wormhole)", true, m.Argument),
            EvolutionType.UseItemFullMoon => ($"Use {ItemName(m.Argument)} (full moon)", true, m.Argument),
            EvolutionType.LevelUpMale => ($"Level {m.Level} (male)", false, 0),
            EvolutionType.LevelUpFemale => ($"Level {m.Level} (female)", false, 0),
            EvolutionType.LevelUpFormFemale1 => ($"Level {m.Level} (female, alternate form)", false, 0),
            EvolutionType.LevelUpATK => ($"Level {m.Level} (Attack > Defense)", false, 0),
            EvolutionType.LevelUpDEF => ($"Level {m.Level} (Defense > Attack)", false, 0),
            EvolutionType.LevelUpAeqD => ($"Level {m.Level} (Attack = Defense)", false, 0),
            EvolutionType.LevelUpECl5 => ($"Level {m.Level} (personality < 5)", false, 0),
            EvolutionType.LevelUpECgeq5 => ($"Level {m.Level} (personality >= 5)", false, 0),
            EvolutionType.LevelUpNinjask => ("Level up (Ninjask evolves)", false, 0),
            EvolutionType.LevelUpShedinja => ("Level up, extra party slot + Poke Ball (Shedinja)", false, 0),
            EvolutionType.LevelUpBeauty => ($"Level up (high Beauty, {m.Argument}+)", false, 0),
            EvolutionType.LevelUpHeldItemDay => ($"Level up holding {ItemName(m.Argument)} (day)", true, m.Argument),
            EvolutionType.LevelUpHeldItemNight => ($"Level up holding {ItemName(m.Argument)} (night)", true, m.Argument),
            EvolutionType.LevelUpKnowMove => ("Level up knowing a specific move", false, 0),
            EvolutionType.LevelUpWithTeammate => ("Level up with a specific teammate in the party", false, 0),
            EvolutionType.LevelUpElectric => ("Level up near a Thunderstone-like electric field", false, 0),
            EvolutionType.LevelUpForest => ("Level up near Moss Rock", false, 0),
            EvolutionType.LevelUpCold => ("Level up near Ice Rock", false, 0),
            EvolutionType.LevelUpInverted => ($"Level {m.Level} (Inverted Battle)", false, 0),
            EvolutionType.LevelUpAffection50MoveType => ("Level up with high affection, knowing a move of a specific type", false, 0),
            EvolutionType.LevelUpMoveType => ("Level up knowing a move of a specific type", false, 0),
            EvolutionType.LevelUpWeather => ("Level up in specific weather", false, 0),
            EvolutionType.LevelUpMorning => ($"Level {m.Level} (morning)", false, 0),
            EvolutionType.LevelUpNight => ($"Level {m.Level} (night)", false, 0),
            EvolutionType.LevelUpDusk => ($"Level {m.Level} (dusk)", false, 0),
            EvolutionType.LevelUpSummit => ("Level up at the summit", false, 0),
            EvolutionType.LevelUpVersion => ($"Level {m.Level} (version-specific)", false, 0),
            EvolutionType.LevelUpVersionDay => ($"Level {m.Level} (version-specific, day)", false, 0),
            EvolutionType.LevelUpVersionNight => ($"Level {m.Level} (version-specific, night)", false, 0),
            EvolutionType.LevelUpWormhole => ($"Level {m.Level} (Ultra Wormhole)", false, 0),
            EvolutionType.CriticalHitsInBattle => ("Land critical hits in battle (Sirfetch'd)", false, 0),
            EvolutionType.HitPointsLostInBattle => ("Lose HP in battle without fainting (Runerigus)", false, 0),
            EvolutionType.Spin => ("Spin while walking (Alcremie)", false, 0),
            EvolutionType.LevelUpNatureAmped => ("Level up, Amped nature (Toxtricity)", false, 0),
            EvolutionType.LevelUpNatureLowKey => ("Level up, Low Key nature (Toxtricity)", false, 0),
            EvolutionType.TowerOfDarkness => ("Train at the Tower of Darkness (Urshifu)", false, 0),
            EvolutionType.TowerOfWaters => ("Train at the Tower of Waters (Urshifu)", false, 0),
            EvolutionType.LevelUpWalkStepsWith => ("Walk a distance with a specific teammate", false, 0),
            EvolutionType.LevelUpUnionCircle => ("Level up in a Union Circle with a teammate (Palafin)", false, 0),
            EvolutionType.LevelUpInBattleEC100 => ($"Level {m.Level} in battle (form A)", false, 0),
            EvolutionType.LevelUpInBattleECElse => ($"Level {m.Level} in battle (form B)", false, 0),
            EvolutionType.LevelUpCollect999 => ("Collect 999 Gimmighoul Coins", false, 0),
            EvolutionType.LevelUpDefeatEquals => ("Level up after defeating opponents (Kingambit)", false, 0),
            EvolutionType.LevelUpUseMoveSpecial => ("Use a specific move 20 times (Annihilape)", false, 0),
            EvolutionType.LevelUpKnowMoveECElse => ("Level up knowing a specific move (form A)", false, 0),
            EvolutionType.LevelUpKnowMoveEC100 => ("Level up knowing a specific move (form B)", false, 0),
            EvolutionType.LevelUpRecoilDamageMale => ("Take recoil damage total (male, Basculegion)", false, 0),
            EvolutionType.LevelUpRecoilDamageFemale => ("Take recoil damage total (female, Basculegion)", false, 0),
            EvolutionType.UseMoveAgileStyle => ("Use a move with Agile Style (Wyrdeer)", false, 0),
            EvolutionType.UseMoveStrongStyle => ("Use a move with Strong Style (Overqwil)", false, 0),
            EvolutionType.UseMoveBarbBarrage => ("Use Barb Barrage at night (Qwilfish)", false, 0),
            EvolutionType.Hisui => ("Hisuian-region-specific evolution", false, 0),
            _ => ($"{m.Method} (Lv.{m.Level})", false, 0),
        };
    }
}
