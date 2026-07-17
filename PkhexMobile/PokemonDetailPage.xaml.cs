using CommunityToolkit.Maui.Storage;
using PKHeX.Core;

namespace PkhexMobile;

public partial class PokemonDetailPage : ContentPage
{
    PKM? pk;
    SaveFile? parentSave;
    int partyIndex;

    public PokemonDetailPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        var p = NavigationState.PendingPokemon;
        var sav = NavigationState.PendingPokemonSave;
        var idx = NavigationState.PendingPokemonIndex;
        NavigationState.PendingPokemon = null;
        NavigationState.PendingPokemonSave = null;

        if (p is null)
            return;

        pk = p;
        parentSave = sav;
        partyIndex = idx;
        LoadPokemon(p);
    }

    private void LoadPokemon(PKM p)
    {
        TitleLabel.Text = PkmDisplayHelper.GetDisplayName(p);
        NicknameEntry.Text = p.Nickname;
        LevelEntry.Text = p.CurrentLevel.ToString();
        SaveStatusLabel.Text = string.Empty;
        SaveChangesBtn.IsVisible = parentSave is not null;

        var rows = new List<StatRow>
        {
            new("Species", PkmDisplayHelper.GetSpeciesName(p.Species)),
            new("Nature", PkmDisplayHelper.GetNatureName(p.Nature)),
            new("Ability", PkmDisplayHelper.GetAbilityName(p.Ability)),
            new("Move 1", PkmDisplayHelper.GetMoveName(p.Move1)),
            new("Move 2", PkmDisplayHelper.GetMoveName(p.Move2)),
            new("Move 3", PkmDisplayHelper.GetMoveName(p.Move3)),
            new("Move 4", PkmDisplayHelper.GetMoveName(p.Move4)),
            new("IVs", $"HP {p.IV_HP} / Atk {p.IV_ATK} / Def {p.IV_DEF} / SpA {p.IV_SPA} / SpD {p.IV_SPD} / Spe {p.IV_SPE}"),
            new("EVs", $"HP {p.EV_HP} / Atk {p.EV_ATK} / Def {p.EV_DEF} / SpA {p.EV_SPA} / SpD {p.EV_SPD} / Spe {p.EV_SPE}"),
        };

        StatsList.ItemsSource = rows;
    }

    private async void OnSaveChangesClicked(object? sender, EventArgs e)
    {
        if (pk is null || parentSave is null)
        {
            SaveStatusLabel.Text = "No save context available - can't export.";
            return;
        }

        if (!byte.TryParse(LevelEntry.Text, out var level) || level is < 1 or > 100)
        {
            SaveStatusLabel.Text = "Level must be a number between 1 and 100.";
            return;
        }

        try
        {
            pk.Nickname = NicknameEntry.Text ?? string.Empty;
            pk.IsNicknamed = true;
            pk.CurrentLevel = level;

            parentSave.SetPartySlotAtIndex(pk, partyIndex);

            var bytes = parentSave.Write().ToArray();

            using var stream = new MemoryStream(bytes);
            var fileName = $"edited_{DateTime.Now:yyyyMMdd_HHmmss}.sav";
            var result = await FileSaver.Default.SaveAsync(fileName, stream, CancellationToken.None);

            SaveStatusLabel.Text = result.IsSuccessful
                ? $"Saved to: {result.FilePath}"
                : result.IsCancelled
                    ? "Save cancelled."
                    : $"Save failed: {result.Exception?.Message}";

            if (result.IsSuccessful)
                TitleLabel.Text = PkmDisplayHelper.GetDisplayName(pk);
        }
        catch (Exception ex)
        {
            SaveStatusLabel.Text = $"Error: {ex.Message}";
        }
    }
}
