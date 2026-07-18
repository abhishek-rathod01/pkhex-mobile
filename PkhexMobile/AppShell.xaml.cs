namespace PkhexMobile;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute(nameof(PartyListPage), typeof(PartyListPage));
		Routing.RegisterRoute(nameof(PokemonDetailPage), typeof(PokemonDetailPage));
		Routing.RegisterRoute(nameof(BoxListPage), typeof(BoxListPage));
	}
}
