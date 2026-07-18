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

        IvHpEntry.Text = p.IV_HP.ToString();
        IvAtkEntry.Text = p.IV_ATK.ToString();
        IvDefEntry.Text = p.IV_DEF.ToString();
        IvSpaEntry.Text = p.IV_SPA.ToString();
        IvSpdEntry.Text = p.IV_SPD.ToString();
        IvSpeEntry.Text = p.IV_SPE.ToString();

        EvHpEntry.Text = p.EV_HP.ToString();
        EvAtkEntry.Text = p.EV_ATK.ToString();
        EvDefEntry.Text = p.EV_DEF.ToString();
        EvSpaEntry.Text = p.EV_SPA.ToString();
        EvSpdEntry.Text = p.EV_SPD.ToString();
        EvSpeEntry.Text = p.EV_SPE.ToString();

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
        };

        StatsList.ItemsSource = rows;
    }

    private static bool TryParseStat(string? text, int max, out byte value)
    {
        value = 0;
        if (!byte.TryParse(text, out var parsed) || parsed > max)
            return false;
        value = parsed;
        return true;
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

        if (!TryParseStat(IvHpEntry.Text, 31, out var ivHp) || !TryParseStat(IvAtkEntry.Text, 31, out var ivAtk) ||
            !TryParseStat(IvDefEntry.Text, 31, out var ivDef) || !TryParseStat(IvSpaEntry.Text, 31, out var ivSpa) ||
            !TryParseStat(IvSpdEntry.Text, 31, out var ivSpd) || !TryParseStat(IvSpeEntry.Text, 31, out var ivSpe))
        {
            SaveStatusLabel.Text = "IVs must be numbers between 0 and 31.";
            return;
        }

        if (!TryParseStat(EvHpEntry.Text, 252, out var evHp) || !TryParseStat(EvAtkEntry.Text, 252, out var evAtk) ||
            !TryParseStat(EvDefEntry.Text, 252, out var evDef) || !TryParseStat(EvSpaEntry.Text, 252, out var evSpa) ||
            !TryParseStat(EvSpdEntry.Text, 252, out var evSpd) || !TryParseStat(EvSpeEntry.Text, 252, out var evSpe))
        {
            SaveStatusLabel.Text = "EVs must be numbers between 0 and 252.";
            return;
        }

        try
        {
            pk.Nickname = NicknameEntry.Text ?? string.Empty;
            pk.IsNicknamed = true;
            pk.CurrentLevel = level;

            pk.IV_HP = ivHp;
            pk.IV_ATK = ivAtk;
            pk.IV_DEF = ivDef;
            pk.IV_SPA = ivSpa;
            pk.IV_SPD = ivSpd;
            pk.IV_SPE = ivSpe;

            pk.EV_HP = evHp;
            pk.EV_ATK = evAtk;
            pk.EV_DEF = evDef;
            pk.EV_SPA = evSpa;
            pk.EV_SPD = evSpd;
            pk.EV_SPE = evSpe;

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
