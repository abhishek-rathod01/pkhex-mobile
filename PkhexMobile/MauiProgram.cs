using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;

namespace PkhexMobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("SpaceGrotesk-Medium.ttf", "SpaceGroteskMedium");
				fonts.AddFont("SpaceGrotesk-SemiBold.ttf", "SpaceGroteskSemiBold");
				fonts.AddFont("SpaceGrotesk-Bold.ttf", "SpaceGroteskBold");

				fonts.AddFont("Manrope-Regular.ttf", "ManropeRegular");
				fonts.AddFont("Manrope-Medium.ttf", "ManropeMedium");
				fonts.AddFont("Manrope-SemiBold.ttf", "ManropeSemiBold");
				fonts.AddFont("Manrope-Bold.ttf", "ManropeBold");
				fonts.AddFont("Manrope-ExtraBold.ttf", "ManropeExtraBold");

				fonts.AddFont("JetBrainsMono-Regular.ttf", "JetBrainsMonoRegular");
				fonts.AddFont("JetBrainsMono-Medium.ttf", "JetBrainsMonoMedium");
				fonts.AddFont("JetBrainsMono-SemiBold.ttf", "JetBrainsMonoSemiBold");
				fonts.AddFont("JetBrainsMono-Bold.ttf", "JetBrainsMonoBold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
