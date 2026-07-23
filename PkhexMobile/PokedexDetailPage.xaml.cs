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
    bool showingShiny;
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
        showingShiny = false;
        string name = PokedexService.GetSpeciesName(speciesId);
        Title = name;
        NameLabel.Text = name;
        DexNumberLabel.Text = $"#{speciesId:D4}";
        SpriteImage.Source = SpriteHelper.SpeciesSpriteFile(speciesId, shiny: false);
        ShinyToggleBtn.Text = "View Shiny";

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
                forms.Add(new FormEntryDisplay(i, formName, speciesId));
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

        // Both fire-and-forget, not awaited, so neither blocks the rest of the page rendering:
        // flavor text needs async file I/O (a bundled MauiAsset JSON); encounter-location data is
        // the one PKHeX.Core lookup on this page that ISN'T instant - EncounterMovesetGenerator
        // scans across up to 9 EntityContexts and, for a heavily-encountered species like
        // Charizard or Pikachu, produces 1000+ raw records to walk and dedupe. Calling it
        // synchronously from LoadSpecies caused a real on-device ANR ("PkhexMobile isn't
        // responding"), confirmed still unresponsive after 45+ seconds of "Wait" - this is NOT a
        // quick synchronous PKHeX.Core call like the ones above it (base stats/abilities/forms/
        // evolution are all pre-computed table lookups; encounter scanning is not). Task.Run keeps
        // it off the UI thread entirely; the card shows a loading state until it resolves.
        _ = LoadFlavorTextAsync();
        _ = LoadEncounterLocationsAsync();
    }

    async Task LoadFlavorTextAsync()
    {
        var entries = await PokedexFlavorTextService.GetEntriesAsync(speciesId);
        BindableLayout.SetItemsSource(DexEntriesList, entries);
        DexEntriesEmptyLabel.IsVisible = entries.Count == 0;
    }

    void OnShinyToggleClicked(object? sender, EventArgs e)
    {
        showingShiny = !showingShiny;
        SpriteImage.Source = SpriteHelper.SpeciesSpriteFile(speciesId, shiny: showingShiny);
        ShinyToggleBtn.Text = showingShiny ? "View Regular" : "View Shiny";
    }

    // "Where to Find" card: species -> games/methods/locations, sourced entirely from
    // PKHeX.Core's own legality-grade encounter tables (see PokedexService.GetEncounterLocations
    // for the full sourcing story and the two layers of duplication found and fixed while building
    // it). Grouped into a fixed category order rather than PokedexService's own enum declaration
    // order, since Wild/Static/Egg read most naturally first for a "how do I catch this" question.
    //
    // Runs on a background thread (Task.Run) - see the comment in LoadSpecies for why this one
    // PKHeX.Core call, unlike every other lookup on this page, is NOT fast enough to call directly
    // from the UI thread (a real on-device ANR was caused by an earlier synchronous version of
    // this method).
    async Task LoadEncounterLocationsAsync()
    {
        ushort requestedSpecies = speciesId; // captured before the await - see the guard below
        EncounterLoadingLabel.IsVisible = true;
        EncounterCategoriesList.IsVisible = false;

        var rows = await Task.Run(() => PokedexService.GetEncounterLocations(requestedSpecies, Form));

        // If the user navigated to a different species while this was running (this page instance
        // could in principle be reused, matching the same defensive pattern already used for the
        // move-picker chips elsewhere in this app), don't paint stale data over the new species.
        if (requestedSpecies != speciesId)
            return;

        EncounterLoadingLabel.IsVisible = false;
        EncounterCategoriesList.IsVisible = true;

        var categories = new List<EncounterCategoryDisplay>();

        void AddCategory(PokedexService.EncounterCategory cat, string title)
        {
            var matches = rows.Where(r => r.Category == cat).ToList();
            if (matches.Count == 0)
                return;

            if (cat == PokedexService.EncounterCategory.EventGift)
            {
                // Individually real, historical, and far too numerous/granular to list one-by-one
                // (Pikachu alone has ~48 distinct past distribution events across 13 games) -
                // collapsed to one summary line instead.
                int events = matches.Sum(r => r.Count);
                int games = matches.Select(r => r.Version).Distinct().Count();
                categories.Add(new EncounterCategoryDisplay(title, [],
                    $"Available via {events} past Mystery Gift distribution event(s) across {games} game(s) - most are no longer obtainable."));
                return;
            }

            categories.Add(new EncounterCategoryDisplay(title, [.. matches.Select(r => new EncounterRowDisplay(r))], null));
        }

        AddCategory(PokedexService.EncounterCategory.Wild, "Wild Encounter");
        AddCategory(PokedexService.EncounterCategory.StaticGift, "Static / Gift Encounter");
        AddCategory(PokedexService.EncounterCategory.Egg, "Breeding (Egg)");
        AddCategory(PokedexService.EncounterCategory.Trade, "In-Game Trade");
        AddCategory(PokedexService.EncounterCategory.Raid, "Raid Battle");
        AddCategory(PokedexService.EncounterCategory.EventGift, "Mystery Gift Event");

        BindableLayout.SetItemsSource(EncounterCategoriesList, categories);
        EncounterEmptyLabel.IsVisible = categories.Count == 0;
        EncounterRateNoteLabel.IsVisible = categories.Count > 0;
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
