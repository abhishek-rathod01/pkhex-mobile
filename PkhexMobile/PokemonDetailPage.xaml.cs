using CommunityToolkit.Maui.Storage;
using PKHeX.Core;

namespace PkhexMobile;

public partial class PokemonDetailPage : ContentPage
{
    PKM? pk;
    SaveFile? parentSave;
    int partyIndex;
    // Gen1/2 STRUCTURAL flag only (HP IV derived, SpA/SpD linked) - the numeric caps below are the
    // library's own per-format answers, not a generation branch. See LoadPokemonCore.
    bool isGen12;
    int ivMax = 31;
    int evMax = 252;

    // The Gen3+ sum-of-all-EVs budget, taken from PKHeX.Core rather than typed as 510, so the
    // readout tracks the library if it ever changes. Advisory only - see RefreshEvTotal.
    const int EvBudgetTotal = EffortValues.Max510;

    // Whether the Form/Ability/Nature pickers actually do anything on Write() for the currently
    // loaded Pokemon's format - see the "Form + Nature + Ability editing" section of PROGRESS.md
    // for the empirical, per-generation basis for each of these three booleans. False means the
    // picker is disabled (still shows the correct current value where one exists - matches the
    // Gen1/2 IV field disabling precedent), not hidden.
    bool formEditable;
    bool abilityEditable;
    bool natureEditable;

    // Dirty/clean Save button tracking (design-notes.md "Save button (dirty/clean)"):
    // disabled while the screen has no unsaved changes, enabled the instant any field is
    // edited, disabled again immediately after a successful save. isLoading suppresses the
    // TextChanged/SelectedIndexChanged noise LoadPokemon's own field population fires.
    bool isLoading;
    bool isDirty;

    // Picker index -> game ID maps, rebuilt per Pokémon from its format's MaxSpeciesID/MaxMoveID.
    // Species list starts at ID 1 (0 = empty, never valid for a stored mon); move list starts at
    // ID 0 so "(None)" is selectable to clear a slot.
    readonly List<ushort> speciesIds = new();
    readonly List<ushort> moveIds = new();
    readonly List<byte> formValues = new();
    readonly List<int> abilityIds = new();
    readonly List<Nature> natureValues = new();

    // Move type byte -> Colors.xaml "Type*" token name, in PKHeX's OWN type-byte order (i.e. the
    // index order of GameInfo.Strings.Types, verified in verify/MoveTypes). This deliberately is
    // NOT the order the design bundle's TypeBadge.jsx lists its types in - that list starts
    // Normal/Fire/Water/Electric, PKHeX's starts Normal/Fighting/Flying/Poison - so indexing one by
    // the other would silently mis-colour almost every chip (Fighting would render as Fire).
    // Hardcoded English keys rather than GameInfo.Strings.Types, so the colour lookup is
    // independent of UI language: a localized Types[9] of "Feu" would never resolve "TypeFire".
    // Types[18] ("Stellar", the Gen9 Terastal type) has no design token and is deliberately absent -
    // verify/MoveTypes confirms no move in any generation's type table is that type (max is 17).
    static readonly string[] TypeColorKeys =
    [
        "Normal", "Fighting", "Flying", "Poison", "Ground", "Rock", "Bug", "Ghost", "Steel",
        "Fire", "Water", "Grass", "Electric", "Psychic", "Ice", "Dragon", "Dark", "Fairy",
    ];

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

        NicknameEntry.TextChanged += (_, _) => MarkDirty();
        LevelEntry.TextChanged += (_, _) => MarkDirty();
        SpeciesPicker.SelectedIndexChanged += (_, _) => MarkDirty();
        FormPicker.SelectedIndexChanged += (_, _) => MarkDirty();
        AbilityPicker.SelectedIndexChanged += (_, _) => MarkDirty();
        NaturePicker.SelectedIndexChanged += (_, _) => MarkDirty();
        // One handler per move Picker, not two - the type chip has to refresh on every selection
        // change (including the programmatic ones LoadPokemon fires), while MarkDirty must stay
        // suppressed during load. Those are different concerns on the same event, so they're
        // combined in OnMoveSelectionChanged rather than stacked as a second subscription.
        Move1Picker.SelectedIndexChanged += (_, _) => OnMoveSelectionChanged(Move1Picker, Move1TypeChip, Move1TypeLabel);
        Move2Picker.SelectedIndexChanged += (_, _) => OnMoveSelectionChanged(Move2Picker, Move2TypeChip, Move2TypeLabel);
        Move3Picker.SelectedIndexChanged += (_, _) => OnMoveSelectionChanged(Move3Picker, Move3TypeChip, Move3TypeLabel);
        Move4Picker.SelectedIndexChanged += (_, _) => OnMoveSelectionChanged(Move4Picker, Move4TypeChip, Move4TypeLabel);
    }

    private void OnMoveSelectionChanged(Picker picker, Border chip, Label label)
    {
        RefreshMoveTypeChip(picker, chip, label);
        // MarkDirty gates itself on isLoading, so the chip above still refreshes during load while
        // the Save button correctly stays clean.
        MarkDirty();
    }

    private void MarkDirty()
    {
        if (isLoading)
            return;
        isDirty = true;
        UpdateSaveButtonState();
    }

    private void UpdateSaveButtonState()
    {
        SaveChangesBtn.IsEnabled = isDirty && parentSave is not null;
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
        // Programmatic field population below fires the same TextChanged/SelectedIndexChanged
        // handlers a real edit would - isLoading suppresses MarkDirty() for the duration so the
        // Save button starts disabled (clean), not enabled, immediately after a fresh load.
        isLoading = true;
        try
        {
            LoadPokemonCore(p);
        }
        finally
        {
            isLoading = false;
            isDirty = false;
            UpdateSaveButtonState();
        }
    }

    private void LoadPokemonCore(PKM p)
    {
        // Numeric IV/EV bounds come from the library, not from a hand-rolled generation ternary.
        // PKM.MaxIV/MaxEV (PKM.cs:298-299) are abstract and overridden per format, so this is one
        // expression covering every generation instead of a branch this app has to keep correct:
        //   Gen1/2  MaxIV 15, MaxEV 65535 (GBPKM.cs:18-19 - real 16-bit Stat Exp, not 0-252 EVs)
        //   Gen3/4  MaxIV 31, MaxEV   255 (G3PKM.cs:23-24, G4PKM.cs:24-25)
        //   Gen5    MaxIV 31, MaxEV   255 (PK5.cs:306-307)
        //   Gen6+   MaxIV 31, MaxEV   252 (G6PKM.cs:125-126, G8PKM.cs:54-55, PK9.cs:72-73)
        // This also fixed a real defect: the previous hardcoded 252 under-capped Gen3/4/5, whose
        // formats genuinely store up to 255, so a legitimate 253-255 in an existing save was
        // silently clamped down to 252 on load and then written back at the clamped value.
        //
        // isGen12 deliberately SURVIVES this de-branching. It no longer carries any numeric
        // meaning, only the Gen1/2 *structural* facts below (HP IV derived from the other four
        // DVs' low bits, SpA/SpD sharing one "Special" value). Those have no generic library
        // handle - ISeparateIVs, the obvious candidate, is implemented only by CK3/XK3
        // (Colosseum/XD), not by GBPKM - so that logic stays hand-rolled on purpose.
        isGen12 = p.Generation is 1 or 2;
        ivMax = p.MaxIV;
        evMax = p.MaxEV;

        RefreshHero(p);
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
        // Ranges quoted from ivMax/evMax rather than literals, so the caption can never drift from
        // the cap actually enforced (the old literal "0-252" was wrong on Gen3/4/5).
        IvRangeLabel.Text = isGen12
            ? $"IVs / DVs (0-{ivMax} each; HP derived, SpD linked to SpA)"
            : $"IVs (0-{ivMax} each)";
        EvRangeLabel.Text = isGen12
            ? $"EVs / Stat Exp (0-{evMax} each; SpD linked to SpA)"
            : $"EVs (0-{evMax} each)";

        // The 510-total budget is a Gen3+ concept only; PKHeX.Core's GetMaximumEV short-circuits
        // to a flat EffortValues.Max12 for Format < 3. Hiding the readout on Gen1/2 also sidesteps
        // a double-count that would otherwise be wrong there: Gen1/2 store ONE shared "Special"
        // stat-exp value that the UI surfaces as two mirrored SpA/SpD fields, so naively summing
        // all six on-screen fields would count it twice.
        EvTotalLabel.IsVisible = p.Format >= 3;

        RefreshGen12DerivedFields();
        RefreshEvTotal();

        PopulateSpeciesPicker(p);
        PopulateFormPicker(p);
        PopulateMovePickers(p);
        PopulateAbilityPicker(p);
        PopulateNaturePicker(p);

        PopulateHeldItem(p);
        RefreshLegality(p);

        SaveStatusLabel.Text = string.Empty;
        SaveChangesBtn.IsVisible = parentSave is not null;
    }

    // Sprite/name/shiny hero block - re-run after a successful save too, since species and
    // shiny-affecting fields (IVs, on some gens) can change from the edit that was just saved.
    private void RefreshHero(PKM p)
    {
        TitleLabel.Text = PkmDisplayHelper.GetDisplayName(p);
        ShinyStarLabel.IsVisible = p.IsShiny;
        SpriteImage.Source = SpriteHelper.SpeciesSpriteFile(p.Species, p.IsShiny);

        var speciesName = PkmDisplayHelper.GetSpeciesName(p.Species);
        SubtitleLabel.Text = TitleLabel.Text == speciesName
            ? $"Lv {p.CurrentLevel}"
            : $"{speciesName} · Lv {p.CurrentLevel}";
    }

    private void PopulateHeldItem(PKM p)
    {
        bool hasItem = p.HeldItem != 0;
        ItemIconBorder.IsVisible = hasItem;
        ItemSpriteImage.Source = hasItem ? SpriteHelper.ItemSpriteFile(p.HeldItem) : null;
        ItemNameLabel.Text = hasItem ? PkmDisplayHelper.GetItemName(p.HeldItem) : "None";
    }

    // Read-only legality snapshot (PKHeX.Core's own LegalityAnalysis, no changes made to it and no
    // auto-fix applied anywhere). Recomputed here and again after a successful save, since species/
    // move/stat edits change the result - never live per-keystroke, matching the existing
    // hero/title refresh cadence.
    private void RefreshLegality(PKM p)
    {
        var la = new LegalityAnalysis(p);
        var suffix = la.Valid ? "Pass" : "Fail";
        var resources = Application.Current!.Resources;

        LegalityBanner.Style = (Style)resources[$"LegalityBanner{suffix}Style"];
        LegalityBadgeBorder.Style = (Style)resources[$"LegalityBadge{suffix}Style"];
        LegalityBadgeLabel.Style = (Style)resources[$"LegalityBadgeLabel{suffix}Style"];
        LegalityMessageLabel.Style = (Style)resources[$"LegalityMessage{suffix}Style"];

        LegalityBadgeLabel.Text = la.Valid ? "LEGAL" : "ILLEGAL";
        LegalityMessageLabel.Text = la.Report();
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
        var typeNames = GameInfo.Strings.Types;
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
            // The type is appended to the item text as well as shown on the chip: MAUI renders
            // Picker dropdown rows as plain strings, so a styled chip cannot appear inside the open
            // dropdown, and the dropdown is exactly where the type matters most (choosing among
            // ~900 moves). The chip covers the collapsed state, this covers the open one. Purely
            // cosmetic - MoveIdFor resolves the selection through moveIds by index, never by text.
            items.Add($"{names[id]} ({typeNames[MoveInfo.GetType(id, p.Context)]})");
        }

        SetMovePicker(Move1Picker, items, p.Move1);
        SetMovePicker(Move2Picker, items, p.Move2);
        SetMovePicker(Move3Picker, items, p.Move3);
        SetMovePicker(Move4Picker, items, p.Move4);

        // Refresh the chips explicitly rather than relying on the SelectedIndexChanged handlers
        // above having fired: assigning SelectedIndex the value it already holds (a page instance
        // reused for a different Pokemon whose move happens to sit at the same index) is a no-op
        // that raises no event, which would leave the previous Pokemon's chip on screen.
        RefreshMoveTypeChip(Move1Picker, Move1TypeChip, Move1TypeLabel);
        RefreshMoveTypeChip(Move2Picker, Move2TypeChip, Move2TypeLabel);
        RefreshMoveTypeChip(Move3Picker, Move3TypeChip, Move3TypeLabel);
        RefreshMoveTypeChip(Move4Picker, Move4TypeChip, Move4TypeLabel);
    }

    // Repaints one move slot's type chip from that slot's currently-selected move. Display only -
    // nothing here reads or writes the underlying PKM.
    private void RefreshMoveTypeChip(Picker picker, Border chip, Label label)
    {
        var resources = Application.Current?.Resources;
        if (pk is null || resources is null)
            return;

        ushort move = MoveIdFor(picker);

        // The empty slot has to be detected from the move ID, never from the type byte:
        // MoveInfo.GetType(0, ctx) returns 0 in every context (verified in verify/MoveTypes), which
        // is indistinguishable from a genuine Normal-type move - so branching on the type byte would
        // paint a cleared slot with a bogus "NORMAL" chip. Neutral placeholder rather than hiding
        // the chip, so clearing a move doesn't shift the row's layout.
        if (move == 0)
        {
            chip.BackgroundColor = (Color)resources["SurfaceSunken"];
            label.TextColor = (Color)resources["TextTertiary"];
            label.Text = "—";
            return;
        }

        // p.Context, not a hardcoded context: several moves changed type between generations, so a
        // Gen1 Pokemon's Gust/Bite/Karate Chop/Sand Attack must read Normal (their Gen1 typing) and
        // pre-Gen6 Charm/Moonlight/Sweet Kiss likewise - see verify/MoveTypes section 3.
        byte type = MoveInfo.GetType(move, pk.Context);
        // "solid" TypeBadge variant per the bundle's MoveRow: white text on the type accent.
        // The TypeXBg tokens are the soft variant, unused here but present for a one-line switch.
        string key = type < TypeColorKeys.Length ? TypeColorKeys[type] : "Normal";
        chip.BackgroundColor = (Color)resources[$"Type{key}"];
        label.TextColor = Colors.White;
        label.Text = GameInfo.Strings.Types[type].ToUpperInvariant();
    }

    private void SetMovePicker(Picker picker, List<string> items, ushort current)
    {
        picker.ItemsSource = items;
        int sel = moveIds.IndexOf(current);
        // A move the current format can't represent (out of range) falls back to "(None)" rather
        // than crashing on an unfound index.
        picker.SelectedIndex = sel >= 0 ? sel : 0;
    }

    // Form: real, independently-stored, working setter starting Gen4 (PK4.Form/G8PKM.Form/PK9.Form
    // etc. are plain Data[] byte fields) - see PROGRESS.md "Form + Nature + Ability editing" for the
    // per-generation source reads and round-trip harness (verify/FormNatureAbilityEdit) this is
    // based on. Gen1-3's Form setter (GBPKM.cs/G3PKM.cs) only ever does anything for Unown, via a
    // PID/DV rejection-sampling side effect - deliberately not exposed here, disabled instead.
    private void PopulateFormPicker(PKM p)
    {
        var formNames = FormConverter.GetFormList(p.Species, GameInfo.Strings.Types, GameInfo.Strings.forms, p.Context);

        formValues.Clear();
        var items = new List<string>(formNames.Length);
        for (byte i = 0; i < formNames.Length; i++)
        {
            formValues.Add(i);
            var name = formNames[i];
            items.Add(string.IsNullOrEmpty(name) ? $"Form {i}" : name);
        }
        FormPicker.ItemsSource = items;

        // A 1-option picker (species has no alternate forms) is disabled too - nothing to pick.
        formEditable = p.Generation >= 4 && formValues.Count > 1;
        FormPicker.IsEnabled = formEditable;
        FormPicker.Title = "Select form";
        FormCaptionLabel.Text = formEditable
            ? "Form"
            : p.Generation >= 4
                ? "Form (this species has no alternate forms)"
                : "Form (not independently editable before Gen 4 - see PROGRESS.md)";

        // A stale/out-of-range Form (e.g. carried over from a species change made elsewhere) falls
        // back to index 0 rather than crashing on an unfound index, same as the move picker fallback.
        int sel = formValues.IndexOf(p.Form);
        FormPicker.SelectedIndex = sel >= 0 ? sel : 0;
    }

    // Ability: real, independently-stored, working setter starting Gen4 (PK4.cs stores it at
    // Data[0x15] even though AbilityNumber/the PID-derived slot indicator is not - see
    // PROGRESS.md). Gen3's Ability getter is real/meaningful (PersonalInfo.GetAbility(AbilityBit))
    // but the direct-by-ID setter is a no-op (G3PKM.cs). Gen1/2 has no Ability concept at all
    // (sentinel -1, GBPKM.cs).
    private void PopulateAbilityPicker(PKM p)
    {
        var names = GameInfo.Strings.Ability;
        int max = Math.Min(p.MaxAbilityID, names.Count - 1);

        abilityIds.Clear();
        var items = new List<string>(max);
        for (int id = 1; id <= max; id++)
        {
            abilityIds.Add(id);
            items.Add(names[id]);
        }
        AbilityPicker.ItemsSource = items;

        abilityEditable = p.Generation >= 4;
        AbilityPicker.IsEnabled = abilityEditable;
        AbilityCaptionLabel.Text = abilityEditable
            ? "Ability"
            : "Ability (not stored independently before Gen 4 - see PROGRESS.md)";

        if (p.Generation <= 2)
        {
            // True sentinel (-1) - nothing meaningful to show, unlike Gen3's real derived value.
            AbilityPicker.Title = "N/A (no Ability concept before Gen 3)";
            AbilityPicker.SelectedIndex = -1;
        }
        else
        {
            AbilityPicker.Title = "Select ability";
            int sel = abilityIds.IndexOf(p.Ability);
            AbilityPicker.SelectedIndex = sel >= 0 ? sel : 0;
        }
    }

    // Nature: real, independently-stored, working setter starting Gen5 (PK5.cs stores it at
    // Data[0x41] - contrary to the "PID-derived Gen3-5" folklore, confirmed empirically against a
    // real Gen5 save in verify/FormNatureAbilityEdit). Gen3/4's Nature getter is real/meaningful
    // (PID % 25, G3PKM.cs/G4PKM.cs) but the setter is a no-op; PKHeX.Core does have a
    // PID-rerolling workaround (SetPIDNature) but it also de-shinies the mon as a side effect,
    // which is too surprising to wire into a plain picker - deliberately not used. Gen1/2 has no
    // Nature concept at all (sentinel Hardy/0, GBPKM.cs).
    private void PopulateNaturePicker(PKM p)
    {
        var names = GameInfo.Strings.Natures;
        int max = Math.Min(24, names.Count - 1); // 25 real natures (Hardy..Quirky), ignore anything past

        natureValues.Clear();
        var items = new List<string>(max + 1);
        for (int i = 0; i <= max; i++)
        {
            natureValues.Add((Nature)i);
            items.Add(names[i]);
        }
        NaturePicker.ItemsSource = items;

        natureEditable = p.Generation >= 5;
        NaturePicker.IsEnabled = natureEditable;
        NatureCaptionLabel.Text = natureEditable
            ? "Nature"
            : "Nature (PID-derived before Gen 5 - not directly editable, see PROGRESS.md)";

        if (p.Generation <= 2)
        {
            NaturePicker.Title = "N/A (no Nature concept before Gen 3)";
            NaturePicker.SelectedIndex = -1;
        }
        else
        {
            NaturePicker.Title = "Select nature";
            int sel = natureValues.IndexOf(p.Nature);
            NaturePicker.SelectedIndex = sel >= 0 ? sel : 0;
        }
    }

    private void OnIvIndependentEntryTextChanged(object? sender, TextChangedEventArgs e)
    {
        ClampEntryToMax(sender as Entry, ivMax);
        RefreshGen12DerivedFields();
        MarkDirty();
    }

    private void OnEvIndependentEntryTextChanged(object? sender, TextChangedEventArgs e)
    {
        ClampEntryToMax(sender as Entry, evMax);
        RefreshGen12DerivedFields();
        RefreshEvTotal();
        MarkDirty();
    }

    // Live "EV total: n / 510" readout, computed from what's currently typed in the six fields
    // (not from pk.EVTotal, which is the last-saved state and would lag every keystroke).
    //
    // Deliberately ADVISORY. PKHeX.Core does expose a hard answer - CommonEdits.GetMaximumEV(i)
    // returns Clamp(510 - (EVTotal - thisEV), 0, 252) - but wiring that in as the field cap would
    // do two wrong things here. It would (a) re-impose a 252 ceiling on Gen3/4/5, undoing the
    // MaxEV fix above, since GetMaximumEV clamps to EffortValues.Max252 regardless of format, and
    // (b) make the editor refuse an over-budget keystroke, i.e. enforce legality - which
    // contradicts this app's stated stance (species/move/nature edits are applied exactly as
    // chosen and reported on by the read-only legality badge, never blocked). So: surface it,
    // recolour it, don't prevent it.
    private void RefreshEvTotal()
    {
        if (!EvTotalLabel.IsVisible)
            return;

        int total = 0;
        foreach (var entry in new[] { EvHpEntry, EvAtkEntry, EvDefEntry, EvSpaEntry, EvSpdEntry, EvSpeEntry })
        {
            if (int.TryParse(entry.Text, out var v) && v > 0)
                total += v;
        }

        bool over = total > EvBudgetTotal;
        EvTotalLabel.Text = over
            ? $"EV total: {total} / {EvBudgetTotal} — over budget (allowed here; the legality check will flag it)"
            : $"EV total: {total} / {EvBudgetTotal}";
        EvTotalLabel.TextColor = (Color)Application.Current!.Resources[over ? "StatusWarnFg" : "TextTertiary"];
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

        // Form/Ability/Nature: only resolved (and later applied) when this generation's format
        // actually stores the field independently - see PopulateFormPicker/PopulateAbilityPicker/
        // PopulateNaturePicker and PROGRESS.md for the per-generation basis. When not editable the
        // picker is disabled and its selection is never read, so the original value is left alone.
        byte newForm = pk.Form;
        if (formEditable)
        {
            if (FormPicker.SelectedIndex < 0 || FormPicker.SelectedIndex >= formValues.Count)
            {
                SaveStatusLabel.Text = "Select a form before saving.";
                return;
            }
            newForm = formValues[FormPicker.SelectedIndex];
        }

        int newAbility = pk.Ability;
        if (abilityEditable)
        {
            if (AbilityPicker.SelectedIndex < 0 || AbilityPicker.SelectedIndex >= abilityIds.Count)
            {
                SaveStatusLabel.Text = "Select an ability before saving.";
                return;
            }
            newAbility = abilityIds[AbilityPicker.SelectedIndex];
        }

        Nature newNature = pk.Nature;
        if (natureEditable)
        {
            if (NaturePicker.SelectedIndex < 0 || NaturePicker.SelectedIndex >= natureValues.Count)
            {
                SaveStatusLabel.Text = "Select a nature before saving.";
                return;
            }
            newNature = natureValues[NaturePicker.SelectedIndex];
        }

        bool speciesChanged = newSpecies != pk.Species;
        bool formChanged = formEditable && newForm != pk.Form;
        bool abilityChanged = abilityEditable && newAbility != pk.Ability;
        bool natureChanged = natureEditable && newNature != pk.Nature;
        bool movesChanged = newMoves[0] != pk.Move1 || newMoves[1] != pk.Move2 ||
                            newMoves[2] != pk.Move3 || newMoves[3] != pk.Move4;

        // A change to any of these makes the stored party stat block (HP/Atk/.../Stat_Level)
        // stale, so it must be recomputed below. Captured before mutation. Form and Nature both
        // feed PKM.LoadStats (PersonalInfo.GetFormEntry and StatAlignment.ModifyStatsForAlignment
        // respectively - confirmed empirically in verify/FormNatureAbilityEdit); Ability does not
        // affect the stat block at all (battle-time only), so it's deliberately excluded here.
        bool statsAffected = speciesChanged || formChanged || natureChanged ||
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
            if (formEditable)
                pk.Form = newForm;
            pk.CurrentLevel = level;
            // SetMoves (not raw Move1..4) so current PP is recomputed for the new moves - setting
            // the move IDs alone would leave stale PP from the previous moves.
            pk.SetMoves(newMoves);

            if (abilityEditable)
                pk.Ability = newAbility;

            if (natureEditable)
            {
                pk.Nature = newNature;
                // Gen8+ (G8PKM/PA8/PA9/PK9) stores a SECOND independent nature-shaped byte,
                // StatAlignment (the "Mint" mechanic) - PKM.LoadStats/ResetPartyStats reads
                // StatAlignment for the stat-boost calculation, not Nature. This app has one
                // Nature field, not two Mint-aware ones, so both are kept in sync: a user picking
                // "Adamant" expects the displayed Nature AND the stat block to both reflect
                // Adamant, not a legal-but-confusing Mint-vs-original mismatch. Discovered
                // empirically in verify/FormNatureAbilityEdit (first pass only set Nature and the
                // Gen9 stat-block assertion failed - stats didn't move at all).
                if (pk.Format >= 8)
                    pk.StatAlignment = newNature;
            }

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
                // changed species/form/ability/nature/moves, so a plain nickname/stat edit isn't
                // nagged.
                var note = (speciesChanged || formChanged || abilityChanged || natureChanged || movesChanged)
                    ? " (Species/form/ability/nature/move edits are applied as-is; other tools may flag this as illegal.)"
                    : string.Empty;
                SaveStatusLabel.Text = $"Saved to: {result.FilePath}{note}";
                RefreshHero(pk);
                PopulateHeldItem(pk);
                RefreshLegality(pk);

                // Design-notes.md Save button rule 3: return to disabled immediately after a
                // successful save.
                isDirty = false;
                UpdateSaveButtonState();
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
