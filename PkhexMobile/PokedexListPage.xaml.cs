namespace PkhexMobile;

/// <summary>
/// Reference-only Pokedex browse screen - not tied to a loaded save file. Species names come from
/// PKHeX.Core's own string tables (PkmDisplayHelper.GetSpeciesName), the same source every other
/// screen in the app uses, so a species renders identically here as it does on a real Pokemon's
/// detail screen.
/// </summary>
public partial class PokedexListPage : ContentPage
{
    readonly List<PokedexEntryDisplay> allSpecies = new();

    public PokedexListPage()
    {
        InitializeComponent();

        for (int id = 1; id <= PokedexService.MaxSpecies; id++)
            allSpecies.Add(new PokedexEntryDisplay(id, PokedexService.GetSpeciesName(id), PokedexService.GetGeneration(id)));

        var genItems = new List<string> { "All Gens" };
        for (int g = 1; g <= 9; g++)
            genItems.Add($"Gen {g}");
        GenerationPicker.ItemsSource = genItems;
        GenerationPicker.SelectedIndex = 0;

        ApplyFilter();
    }

    void OnFilterChanged(object? sender, EventArgs e) => ApplyFilter();

    void ApplyFilter()
    {
        string query = SearchBox.Text?.Trim() ?? string.Empty;
        int genFilter = GenerationPicker.SelectedIndex; // 0 = All Gens, 1..9 = that generation

        IEnumerable<PokedexEntryDisplay> results = allSpecies;
        if (genFilter > 0)
            results = results.Where(s => s.Generation == genFilter);
        if (query.Length > 0)
        {
            // Numeric query matches by dex number (with or without a leading '#'); otherwise a
            // case-insensitive substring match against the species name.
            string numeric = query.TrimStart('#');
            if (int.TryParse(numeric, out int dexNum))
                results = results.Where(s => s.SpeciesId == dexNum);
            else
                results = results.Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var list = results.ToList();
        SpeciesGrid.ItemsSource = list;
        ResultCountLabel.Text = $"{list.Count} of {allSpecies.Count} species";
    }

    async void OnSpeciesTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not BindableObject { BindingContext: PokedexEntryDisplay entry })
            return;
        await Shell.Current.GoToAsync($"{nameof(PokedexDetailPage)}?speciesId={entry.SpeciesId}");
    }
}
