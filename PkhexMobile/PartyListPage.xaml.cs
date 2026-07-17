using PKHeX.Core;

namespace PkhexMobile;

public partial class PartyListPage : ContentPage
{
    SaveFile? currentSave;

    public PartyListPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Only adopt a newly-handed-off save on first arrival; on every appearance
        // (including returning from PokemonDetailPage after an edit) re-read from
        // currentSave so edited nickname/level show up without a fresh navigation.
        if (NavigationState.PendingSave is { } sav)
        {
            currentSave = sav;
            NavigationState.PendingSave = null;
        }

        if (currentSave is not null)
            LoadParty(currentSave);
    }

    private void LoadParty(SaveFile sav)
    {
        HeaderLabel.Text = $"Trainer: {sav.OT}    Party: {sav.PartyCount}";

        var entries = new List<PartyEntryDisplay>();
        for (int i = 0; i < sav.PartyCount; i++)
        {
            var pk = sav.PartyData[i];
            entries.Add(new PartyEntryDisplay(
                Slot: i + 1,
                SpeciesName: PkmDisplayHelper.GetSpeciesName(pk.Species),
                Nickname: PkmDisplayHelper.GetDisplayName(pk),
                Level: pk.CurrentLevel,
                Source: pk));
        }

        PartyList.ItemsSource = entries;
    }

    private async void OnPartySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not PartyEntryDisplay entry)
            return;

        PartyList.SelectedItem = null;

        NavigationState.PendingPokemon = entry.Source;
        NavigationState.PendingPokemonSave = currentSave;
        NavigationState.PendingPokemonIndex = entry.Slot - 1;
        await Shell.Current.GoToAsync(nameof(PokemonDetailPage));
    }
}
