namespace PkhexMobile;

/// <summary>
/// Reference-only Pokedex detail screen. Base stats/types/abilities/forms come from PKHeX.Core's
/// own PersonalTable.SV (see PokedexService); evolution chain comes from PKHeX.Core's
/// EvolutionTree.Evolves9. Not tied to a loaded save file - reachable purely from PokedexListPage,
/// species ID passed as a plain query-string int (safe with Shell's dictionary-coercion trap
/// documented in CLAUDE.md, since that trap only applies to non-IConvertible types like
/// SaveFile/PKM, and int/ushort are IConvertible).
/// </summary>
[QueryProperty(nameof(SpeciesIdParam), "speciesId")]
public partial class PokedexDetailPage : ContentPage
{
    ushort speciesId;
    const byte Form = 0; // base form only - alternate forms are listed read-only in the Forms card

    public string SpeciesIdParam
    {
        set
        {
            if (ushort.TryParse(value, out var id))
                speciesId = id;
        }
    }

    public PokedexDetailPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (speciesId == 0)
            return;
        LoadSpecies();
    }

    void LoadSpecies()
    {
        string name = PokedexService.GetSpeciesName(speciesId);
        Title = name;
        NameLabel.Text = name;
        DexNumberLabel.Text = $"#{speciesId:D4}";
        SpriteImage.Source = SpriteHelper.SpeciesSpriteFile(speciesId, shiny: false);

        var (type1, type2, hasSecond) = PokedexService.GetTypeIds(speciesId, Form);
        SetTypeChip(Type1Chip, Type1Label, type1);
        Type2Chip.IsVisible = hasSecond;
        if (hasSecond)
            SetTypeChip(Type2Chip, Type2Label, type2);

        var abilities = PokedexService.GetAbilities(speciesId, Form);
        BindableLayout.SetItemsSource(AbilitiesList, abilities
            .Select(a => a.IsHidden ? $"{a.Name}  (Hidden Ability)" : a.Name)
            .ToList());

        var stats = PokedexService.GetBaseStats(speciesId, Form);
        SetStat(HpBar, HpValueLabel, stats.HP);
        SetStat(AtkBar, AtkValueLabel, stats.Atk);
        SetStat(DefBar, DefValueLabel, stats.Def);
        SetStat(SpaBar, SpaValueLabel, stats.SpA);
        SetStat(SpdBar, SpdValueLabel, stats.SpD);
        SetStat(SpeBar, SpeValueLabel, stats.Spe);
        TotalStatLabel.Text = $"Base stat total: {stats.Total}";

        var formNames = PokedexService.GetFormNames(speciesId);
        if (formNames.Length > 1)
        {
            var forms = new List<FormEntryDisplay>();
            for (int i = 0; i < formNames.Length; i++)
            {
                string formName = string.IsNullOrEmpty(formNames[i]) ? name : formNames[i];
                forms.Add(new FormEntryDisplay(i, formName));
            }
            FormsCard.IsVisible = true;
            BindableLayout.SetItemsSource(FormsList, forms);
        }
        else
        {
            FormsCard.IsVisible = false;
        }

        var chain = PokedexService.GetEvolutionChain(speciesId, Form);
        BindableLayout.SetItemsSource(EvolutionList, chain
            .Select(n => new EvoNodeDisplay(n, n.Species == speciesId && n.Form == Form))
            .ToList());
        EvolutionSingleLabel.IsVisible = chain.Count <= 1;
    }

    static void SetTypeChip(Border chip, Label label, byte typeId)
    {
        var resources = Application.Current?.Resources;
        if (resources is null)
            return;
        string key = PokedexService.GetTypeColorKey(typeId);
        chip.BackgroundColor = (Color)resources[$"Type{key}"];
        label.TextColor = Colors.White;
        label.Text = PokedexService.GetTypeName(typeId).ToUpperInvariant();
    }

    static void SetStat(ProgressBar bar, Label valueLabel, int value)
    {
        // 255 is the practical ceiling for any single base stat across the whole National Dex
        // (Blissey's HP and Shuckle's Def/SpD) - used only to scale the bar's fill, not a game rule.
        bar.Progress = Math.Clamp(value / 255.0, 0, 1);
        valueLabel.Text = value.ToString();
    }
}
