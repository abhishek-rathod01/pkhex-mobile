namespace PkhexMobile;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute(nameof(PartyListPage), typeof(PartyListPage));
		Routing.RegisterRoute(nameof(PokemonDetailPage), typeof(PokemonDetailPage));
		Routing.RegisterRoute(nameof(BoxListPage), typeof(BoxListPage));
		Routing.RegisterRoute(nameof(TrainerInfoPage), typeof(TrainerInfoPage));
		Routing.RegisterRoute(nameof(PokemonTransferPage), typeof(PokemonTransferPage));
		Routing.RegisterRoute(nameof(PokedexListPage), typeof(PokedexListPage));
		Routing.RegisterRoute(nameof(PokedexDetailPage), typeof(PokedexDetailPage));
	}
}
