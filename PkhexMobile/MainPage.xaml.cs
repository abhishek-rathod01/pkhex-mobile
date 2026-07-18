using PKHeX.Core;

namespace PkhexMobile;

public partial class MainPage : ContentPage
{
	int count = 0;
	byte[]? lastPickedBytes;
	SaveFile? loadedSave;

	public MainPage()
	{
		InitializeComponent();
	}

	private void OnCounterClicked(object? sender, EventArgs e)
	{
		count++;

		if (count == 1)
			CounterBtn.Text = $"Clicked {count} time";
		else
			CounterBtn.Text = $"Clicked {count} times";

		SemanticScreenReader.Announce(CounterBtn.Text);
	}

	private async void OnPickFileClicked(object? sender, EventArgs e)
	{
		try
		{
			var result = await FilePicker.Default.PickAsync();
			if (result is null)
			{
				FileResultLabel.Text = "No file selected.";
				return;
			}

			using var stream = await result.OpenReadAsync();
			using var memoryStream = new MemoryStream();
			await stream.CopyToAsync(memoryStream);
			lastPickedBytes = memoryStream.ToArray();

			var text = $"Selected: {result.FileName}\nBytes: {lastPickedBytes.Length}";
			text += "\n\n" + TryParseSaveFile(lastPickedBytes);

			FileResultLabel.Text = text;
			ViewPartyBtn.IsVisible = loadedSave is { PartyCount: > 0 };
			ViewBoxesBtn.IsVisible = loadedSave is { HasBox: true };
		}
		catch (Exception ex)
		{
			FileResultLabel.Text = $"Error: {ex.Message}";
			loadedSave = null;
			ViewPartyBtn.IsVisible = false;
			ViewBoxesBtn.IsVisible = false;
		}
	}

	private string TryParseSaveFile(byte[] data)
	{
		try
		{
			var sav = SaveUtil.GetSaveFile(data);
			if (sav is null)
			{
				loadedSave = null;
				return "Not a recognized save file.";
			}

			loadedSave = sav;
			return $"Save file detected!\nTrainer: {sav.OT}\nParty count: {sav.PartyCount}";
		}
		catch (Exception ex)
		{
			loadedSave = null;
			return $"Save parse error: {ex.Message}";
		}
	}

	private async void OnViewPartyClicked(object? sender, EventArgs e)
	{
		if (loadedSave is null)
			return;

		NavigationState.PendingSave = loadedSave;
		await Shell.Current.GoToAsync(nameof(PartyListPage));
	}

	private async void OnViewBoxesClicked(object? sender, EventArgs e)
	{
		if (loadedSave is null)
			return;

		NavigationState.PendingSave = loadedSave;
		await Shell.Current.GoToAsync(nameof(BoxListPage));
	}
}
