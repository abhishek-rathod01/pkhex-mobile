using PKHeX.Core;

namespace PkhexMobile;

public partial class PokemonDetailPage : ContentPage
{
    public PokemonDetailPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        var pk = NavigationState.PendingPokemon;
        NavigationState.PendingPokemon = null;
        if (pk is not null)
            LoadPokemon(pk);
    }

    private void LoadPokemon(PKM pk)
    {
        TitleLabel.Text = PkmDisplayHelper.GetDisplayName(pk);

        var rows = new List<StatRow>
        {
            new("Species", PkmDisplayHelper.GetSpeciesName(pk.Species)),
            new("Nickname", pk.Nickname),
            new("Level", pk.CurrentLevel.ToString()),
            new("Nature", PkmDisplayHelper.GetNatureName(pk.Nature)),
            new("Ability", PkmDisplayHelper.GetAbilityName(pk.Ability)),
            new("Move 1", PkmDisplayHelper.GetMoveName(pk.Move1)),
            new("Move 2", PkmDisplayHelper.GetMoveName(pk.Move2)),
            new("Move 3", PkmDisplayHelper.GetMoveName(pk.Move3)),
            new("Move 4", PkmDisplayHelper.GetMoveName(pk.Move4)),
            new("IVs", $"HP {pk.IV_HP} / Atk {pk.IV_ATK} / Def {pk.IV_DEF} / SpA {pk.IV_SPA} / SpD {pk.IV_SPD} / Spe {pk.IV_SPE}"),
            new("EVs", $"HP {pk.EV_HP} / Atk {pk.EV_ATK} / Def {pk.EV_DEF} / SpA {pk.EV_SPA} / SpD {pk.EV_SPD} / Spe {pk.EV_SPE}"),
        };

        StatsList.ItemsSource = rows;
    }
}
