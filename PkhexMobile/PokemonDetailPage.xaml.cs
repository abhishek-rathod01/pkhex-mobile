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
    int evMax = 252;

    // Picker index -> game ID maps, rebuilt per Pokémon from its format's MaxSpeciesID/MaxMoveID.
    // Species list starts at ID 1 (0 = empty, never valid for a stored mon); move list starts at
    // ID 0 so "(None)" is selectable to clear a slot.
    readonly List<ushort> speciesIds = new();
    readonly List<ushort> moveIds = new();

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

        EvHpEntry.TextChanged += OnEvIndependentEntryTextChanged;
        EvAtkEntry.TextChanged += OnEvIndependentEntryTextChanged;
        EvDefEntry.TextChanged += OnEvIndependentEntryTextChanged;
        EvSpaEntry.TextChanged += OnEvIndependentEntryTextChanged;
        EvSpeEntry.TextChanged += OnEvIndependentEntryTextChanged;
        // SpD is disabled for Gen1/2 (mirrors SpA, see LoadPokemon) - harmless no-op there,
        // same reasoning as IvSpdEntry above.
        EvSpdEntry.TextChanged += OnEvIndependentEntryTextChanged;
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
        // Gen1/2 "EVs" are real 16-bit Stat Exp (0-65535), not the modern 0-252 EV system -
        // confirmed against real Gen1/2 saves with maxed stat exp (see PROGRESS.md).
        evMax = isGen12 ? 65535 : 252;

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

        // Defensive clamp (backstop): same reasoning as the IV fields above - display value
        // can never exceed this generation's real range even if the loaded PKM somehow holds
        // something outside it.
        EvHpEntry.Text = Math.Clamp(p.EV_HP, 0, evMax).ToString();
        EvAtkEntry.Text = Math.Clamp(p.EV_ATK, 0, evMax).ToString();
        EvDefEntry.Text = Math.Clamp(p.EV_DEF, 0, evMax).ToString();
        EvSpaEntry.Text = Math.Clamp(p.EV_SPA, 0, evMax).ToString();
        EvSpdEntry.Text = Math.Clamp(p.EV_SPD, 0, evMax).ToString();
        EvSpeEntry.Text = Math.Clamp(p.EV_SPE, 0, evMax).ToString();

        // Gen1/2: HP IV has no independent storage (derived from the low bit of the other
        // four DVs) and SpA/SpD share one "Special" DV/stat-exp value - grey both out so they
        // can't be typed into and silently diverge from what will actually be saved.
        IvHpEntry.IsEnabled = !isGen12;
        IvSpdEntry.IsEnabled = !isGen12;
        EvSpdEntry.IsEnabled = !isGen12;
        IvRangeLabel.Text = isGen12
            ? "IVs / DVs (0-15 each; HP derived, SpD linked to SpA)"
            : "IVs (0-31 each)";
        EvRangeLabel.Text = isGen12
            ? "EVs / Stat Exp (0-65535 each; SpD linked to SpA)"
            : "EVs (0-252 each)";

        RefreshGen12DerivedFields();

        PopulateSpeciesPicker(p);
        PopulateMovePickers(p);

        SaveStatusLabel.Text = string.Empty;
        SaveChangesBtn.IsVisible = parentSave is not null;

        // Nature/Ability stay read-only here: they're PID-derived on several generations
        // (see verify/Gen3), so exposing them as free edits would be misleading. Species and
        // moves are now editable via the pickers above, so they're no longer in this list.
        var rows = new List<StatRow>
        {
            new("Nature", PkmDisplayHelper.GetNatureName(p.Nature)),
            new("Ability", PkmDisplayHelper.GetAbilityName(p.Ability)),
        };

        StatsList.ItemsSource = rows;
    }

    private void PopulateSpeciesPicker(PKM p)
    {
        // Bound by this format's MaxSpeciesID so a species that structurally can't be stored in
        // the save (e.g. a Gen9 'mon in a Gen1 file) is never even offered - this is a format
        // constraint, not a legality judgement.
        var names = GameInfo.Strings.Species;
        int max = Math.Min(p.MaxSpeciesID, (ushort)(names.Count - 1));

        speciesIds.Clear();
        var items = new List<string>(max);
        for (ushort id = 1; id <= max; id++)
        {
            speciesIds.Add(id);
            items.Add(names[id]);
        }
        SpeciesPicker.ItemsSource = items;

        int sel = speciesIds.IndexOf(p.Species);
        SpeciesPicker.SelectedIndex = sel >= 0 ? sel : (items.Count > 0 ? 0 : -1);
    }

    private void PopulateMovePickers(PKM p)
    {
        var names = GameInfo.Strings.Move;
        int max = Math.Min(p.MaxMoveID, (ushort)(names.Count - 1));

        moveIds.Clear();
        var items = new List<string>(max + 1);
        // ID 0 is the empty "(None)" slot; GameInfo's own string for it is blank, so give it a
        // readable label instead of an empty picker row.
        moveIds.Add(0);
        items.Add("(None)");
        for (ushort id = 1; id <= max; id++)
        {
            moveIds.Add(id);
            items.Add(names[id]);
        }

        SetMovePicker(Move1Picker, items, p.Move1);
        SetMovePicker(Move2Picker, items, p.Move2);
        SetMovePicker(Move3Picker, items, p.Move3);
        SetMovePicker(Move4Picker, items, p.Move4);
    }

    private void SetMovePicker(Picker picker, List<string> items, ushort current)
    {
        picker.ItemsSource = items;
        int sel = moveIds.IndexOf(current);
        // A move the current format can't represent (out of range) falls back to "(None)" rather
        // than crashing on an unfound index.
        picker.SelectedIndex = sel >= 0 ? sel : 0;
    }

    private void OnIvIndependentEntryTextChanged(object? sender, TextChangedEventArgs e)
    {
        ClampEntryToMax(sender as Entry, ivMax);
        RefreshGen12DerivedFields();
    }

    private void OnEvIndependentEntryTextChanged(object? sender, TextChangedEventArgs e)
    {
        ClampEntryToMax(sender as Entry, evMax);
        RefreshGen12DerivedFields();
    }

    private static void ClampEntryToMax(Entry? entry, int max)
    {
        if (entry is null || !int.TryParse(entry.Text, out var value) || value <= max)
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

    private ushort MoveIdFor(Picker picker)
    {
        int idx = picker.SelectedIndex;
        return idx >= 0 && idx < moveIds.Count ? moveIds[idx] : (ushort)0;
    }

    private static bool TryParseStat(string? text, int max, out int value)
    {
        value = 0;
        if (!int.TryParse(text, out var parsed) || parsed < 0 || parsed > max)
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

        if (!TryParseStat(EvHpEntry.Text, evMax, out var evHp) || !TryParseStat(EvAtkEntry.Text, evMax, out var evAtk) ||
            !TryParseStat(EvDefEntry.Text, evMax, out var evDef) || !TryParseStat(EvSpaEntry.Text, evMax, out var evSpa) ||
            !TryParseStat(EvSpdEntry.Text, evMax, out var evSpd) || !TryParseStat(EvSpeEntry.Text, evMax, out var evSpe))
        {
            // Defensive clamp backstop: the live TextChanged handlers above already prevent
            // typing past evMax, so this should be unreachable in normal use - but re-validate
            // here too rather than trusting the UI state alone before writing to the save.
            SaveStatusLabel.Text = $"EVs must be numbers between 0 and {evMax}.";
            return;
        }

        // Resolve species/move selections up front so a bad picker state is reported before any
        // mutation happens.
        if (SpeciesPicker.SelectedIndex < 0 || SpeciesPicker.SelectedIndex >= speciesIds.Count)
        {
            SaveStatusLabel.Text = "Select a species before saving.";
            return;
        }
        ushort newSpecies = speciesIds[SpeciesPicker.SelectedIndex];
        ushort[] newMoves =
        {
            MoveIdFor(Move1Picker),
            MoveIdFor(Move2Picker),
            MoveIdFor(Move3Picker),
            MoveIdFor(Move4Picker),
        };

        bool speciesChanged = newSpecies != pk.Species;
        bool movesChanged = newMoves[0] != pk.Move1 || newMoves[1] != pk.Move2 ||
                            newMoves[2] != pk.Move3 || newMoves[3] != pk.Move4;

        // A change to any of these makes the stored party stat block (HP/Atk/.../Stat_Level)
        // stale, so it must be recomputed below. Captured before mutation.
        bool statsAffected = speciesChanged ||
                             level != pk.CurrentLevel ||
                             ivHp != pk.IV_HP || ivAtk != pk.IV_ATK || ivDef != pk.IV_DEF ||
                             ivSpa != pk.IV_SPA || ivSpd != pk.IV_SPD || ivSpe != pk.IV_SPE ||
                             evHp != pk.EV_HP || evAtk != pk.EV_ATK || evDef != pk.EV_DEF ||
                             evSpa != pk.EV_SPA || evSpd != pk.EV_SPD || evSpe != pk.EV_SPE;

        try
        {
            pk.Nickname = NicknameEntry.Text ?? string.Empty;
            pk.IsNicknamed = true;

            // Species BEFORE level: CurrentLevel is stored as EXP, and EXP<->level depends on the
            // species' growth-rate group. Setting the level first (under the old species' growth
            // rate) and then changing species would reinterpret that EXP under the new growth rate
            // and land on the wrong level (e.g. a level-50 Skeledirge becoming a level-45 Garchomp).
            // The species setter also updates format-specific derived fields (e.g. Gen1 internal
            // index + stored types).
            pk.Species = newSpecies;
            pk.CurrentLevel = level;
            // SetMoves (not raw Move1..4) so current PP is recomputed for the new moves - setting
            // the move IDs alone would leave stale PP from the previous moves.
            pk.SetMoves(newMoves);

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

            // Recompute the party stat block from the (possibly new) species/level/IVs/EVs/nature.
            // SaveFile.SetPartyValues only calls this when no stats are present (Stat_HPMax == 0);
            // an existing party mon already has stats, so without this an edited mon would export
            // with a stale stat block - e.g. a species changed to Charizard while still carrying
            // the previous species' HP. ResetPartyStats also syncs Stat_Level to CurrentLevel.
            // Gated on statsAffected so a nickname-only edit doesn't heal/clear status as a side
            // effect. LoadStats (called inside) is generation-aware (Gen1/2 DVs + stat exp, etc.).
            if (statsAffected)
                pk.ResetPartyStats();

            parentSave.SetPartySlotAtIndex(pk, partyIndex);

            var bytes = parentSave.Write().ToArray();

            using var stream = new MemoryStream(bytes);
            var fileName = $"edited_{DateTime.Now:yyyyMMdd_HHmmss}.sav";
            var result = await FileSaver.Default.SaveAsync(fileName, stream, CancellationToken.None);

            if (result.IsSuccessful)
            {
                // Reinforce the caveat at the moment it matters: only when the user actually
                // changed species/moves, so a plain nickname/stat edit isn't nagged.
                var note = (speciesChanged || movesChanged)
                    ? " (Species/move edits are applied as-is; other tools may flag this as illegal.)"
                    : string.Empty;
                SaveStatusLabel.Text = $"Saved to: {result.FilePath}{note}";
                TitleLabel.Text = PkmDisplayHelper.GetDisplayName(pk);
            }
            else
            {
                SaveStatusLabel.Text = result.IsCancelled
                    ? "Save cancelled."
                    : $"Save failed: {result.Exception?.Message}";
            }
        }
        catch (Exception ex)
        {
            SaveStatusLabel.Text = $"Error: {ex.Message}";
        }
    }
}
