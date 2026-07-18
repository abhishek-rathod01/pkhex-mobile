using CommunityToolkit.Maui.Storage;
using PKHeX.Core;

namespace PkhexMobile;

public partial class PokemonDetailPage : ContentPage
{
    PKM? pk;
    SaveFile? parentSave;
    int partyIndex;
    bool isGen12;
    int ivMax = 31;

    public PokemonDetailPage()
    {
        InitializeComponent();

        IvAtkEntry.TextChanged += OnIvIndependentEntryTextChanged;
        IvDefEntry.TextChanged += OnIvIndependentEntryTextChanged;
        IvSpaEntry.TextChanged += OnIvIndependentEntryTextChanged;
        IvSpeEntry.TextChanged += OnIvIndependentEntryTextChanged;
        // HP/SpD are only independently editable on Gen3+ (disabled for Gen1/2, see
        // LoadPokemon) - still wire the same live clamp so typing past ivMax is caught
        // there too, not just at save time.
        IvHpEntry.TextChanged += OnIvIndependentEntryTextChanged;
        IvSpdEntry.TextChanged += OnIvIndependentEntryTextChanged;
        EvSpaEntry.TextChanged += OnEvSpaEntryTextChanged;
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
        // Gen1/2 IVs are 4-bit hardware DVs (0-15), not the 5-bit 0-31 range Gen3+ uses.
        // This mirrors PKHeX.WinForms, which sets NumericUpDown.Maximum to 15 for these
        // generations' IV controls.
        isGen12 = p.Generation is 1 or 2;
        ivMax = isGen12 ? 15 : 31;

        TitleLabel.Text = PkmDisplayHelper.GetDisplayName(p);
        NicknameEntry.Text = p.Nickname;
        LevelEntry.Text = p.CurrentLevel.ToString();

        // Defensive clamp (backstop): display value can never exceed this generation's real
        // hardware range even if the loaded PKM somehow holds something outside it.
        IvHpEntry.Text = Math.Clamp(p.IV_HP, 0, ivMax).ToString();
        IvAtkEntry.Text = Math.Clamp(p.IV_ATK, 0, ivMax).ToString();
        IvDefEntry.Text = Math.Clamp(p.IV_DEF, 0, ivMax).ToString();
        IvSpaEntry.Text = Math.Clamp(p.IV_SPA, 0, ivMax).ToString();
        IvSpdEntry.Text = Math.Clamp(p.IV_SPD, 0, ivMax).ToString();
        IvSpeEntry.Text = Math.Clamp(p.IV_SPE, 0, ivMax).ToString();

        EvHpEntry.Text = p.EV_HP.ToString();
        EvAtkEntry.Text = p.EV_ATK.ToString();
        EvDefEntry.Text = p.EV_DEF.ToString();
        EvSpaEntry.Text = p.EV_SPA.ToString();
        EvSpdEntry.Text = p.EV_SPD.ToString();
        EvSpeEntry.Text = p.EV_SPE.ToString();

        // Gen1/2: HP IV has no independent storage (derived from the low bit of the other
        // four DVs) and SpA/SpD share one "Special" DV/stat-exp value - grey both out so they
        // can't be typed into and silently diverge from what will actually be saved.
        IvHpEntry.IsEnabled = !isGen12;
        IvSpdEntry.IsEnabled = !isGen12;
        EvSpdEntry.IsEnabled = !isGen12;
        IvRangeLabel.Text = isGen12
            ? "IVs / DVs (0-15 each; HP derived, SpD linked to SpA)"
            : "IVs (0-31 each)";

        RefreshGen12DerivedFields();

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

    private void OnIvIndependentEntryTextChanged(object? sender, TextChangedEventArgs e)
    {
        ClampEntryToMax(sender as Entry, ivMax);
        RefreshGen12DerivedFields();
    }

    private void OnEvSpaEntryTextChanged(object? sender, TextChangedEventArgs e) => RefreshGen12DerivedFields();

    private static void ClampEntryToMax(Entry? entry, int max)
    {
        if (entry is null || !byte.TryParse(entry.Text, out var value) || value <= max)
            return;
        entry.Text = max.ToString();
        entry.CursorPosition = entry.Text.Length;
    }

    private void RefreshGen12DerivedFields()
    {
        if (!isGen12)
            return;

        if (byte.TryParse(IvAtkEntry.Text, out var atk) && byte.TryParse(IvDefEntry.Text, out var def) &&
            byte.TryParse(IvSpeEntry.Text, out var spe) && byte.TryParse(IvSpaEntry.Text, out var spa))
        {
            var hp = ((atk & 1) << 3) | ((def & 1) << 2) | ((spe & 1) << 1) | (spa & 1);
            IvHpEntry.Text = hp.ToString();
        }

        IvSpdEntry.Text = IvSpaEntry.Text;
        EvSpdEntry.Text = EvSpaEntry.Text;
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

        if (!TryParseStat(IvHpEntry.Text, ivMax, out var ivHp) || !TryParseStat(IvAtkEntry.Text, ivMax, out var ivAtk) ||
            !TryParseStat(IvDefEntry.Text, ivMax, out var ivDef) || !TryParseStat(IvSpaEntry.Text, ivMax, out var ivSpa) ||
            !TryParseStat(IvSpdEntry.Text, ivMax, out var ivSpd) || !TryParseStat(IvSpeEntry.Text, ivMax, out var ivSpe))
        {
            // Defensive clamp backstop: the live TextChanged handlers above already prevent
            // typing past ivMax, so this should be unreachable in normal use - but re-validate
            // here too rather than trusting the UI state alone before writing to the save.
            SaveStatusLabel.Text = $"IVs must be numbers between 0 and {ivMax}.";
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
