using PKHeX.Core;

namespace PkhexMobile;

public partial class BoxListPage : ContentPage
{
    SaveFile? currentSave;

    public BoxListPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (NavigationState.PendingSave is { } sav)
        {
            currentSave = sav;
            NavigationState.PendingSave = null;
        }

        if (currentSave is not null)
            LoadBoxNames(currentSave);
    }

    private void LoadBoxNames(SaveFile sav)
    {
        var names = new List<string>();
        for (int i = 0; i < sav.BoxCount; i++)
        {
            var name = sav is IBoxDetailNameRead r ? r.GetBoxName(i) : BoxDetailNameExtensions.GetDefaultBoxName(i);
            names.Add(string.IsNullOrWhiteSpace(name) ? BoxDetailNameExtensions.GetDefaultBoxName(i) : name);
        }

        BoxPicker.ItemsSource = names;
        if (names.Count > 0)
            BoxPicker.SelectedIndex = 0;
    }

    private void OnBoxSelected(object? sender, EventArgs e)
    {
        if (currentSave is null || BoxPicker.SelectedIndex < 0)
            return;

        LoadBox(currentSave, BoxPicker.SelectedIndex);
    }

    private void LoadBox(SaveFile sav, int box)
    {
        var entries = new List<PartyEntryDisplay>();
        for (int slot = 0; slot < sav.BoxSlotCount; slot++)
        {
            var pk = sav.GetBoxSlotAtIndex(box, slot);
            if (pk.Species == 0)
                continue;

            entries.Add(new PartyEntryDisplay(
                Slot: slot + 1,
                SpeciesName: PkmDisplayHelper.GetSpeciesName(pk.Species),
                Nickname: PkmDisplayHelper.GetDisplayName(pk),
                Level: pk.CurrentLevel,
                Source: pk));
        }

        HeaderLabel.Text = entries.Count == 0 ? "No Pokémon in this box." : $"{entries.Count} Pokémon";
        BoxList.ItemsSource = entries;
    }

    private async void OnBoxEntrySelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not PartyEntryDisplay entry)
            return;

        BoxList.SelectedItem = null;

        // Read-only: leave PendingPokemonSave null so the detail page hides Save Changes
        // and never calls SetPartySlotAtIndex - box slots aren't party slots, and writing
        // one back through the party-index path would corrupt data or go out of range.
        NavigationState.PendingPokemon = entry.Source;
        NavigationState.PendingPokemonSave = null;
        NavigationState.PendingPokemonIndex = 0;
        await Shell.Current.GoToAsync(nameof(PokemonDetailPage));
    }
}
