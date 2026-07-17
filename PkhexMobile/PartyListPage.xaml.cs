using PKHeX.Core;

namespace PkhexMobile;

[QueryProperty(nameof(Save), "Save")]
public partial class PartyListPage : ContentPage
{
    public SaveFile? Save
    {
        set
        {
            if (value is not null)
                LoadParty(value);
        }
    }

    public PartyListPage()
    {
        InitializeComponent();
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

        await Shell.Current.GoToAsync(nameof(PokemonDetailPage), new Dictionary<string, object>
        {
            ["Pokemon"] = entry.Source,
        });
    }
}
