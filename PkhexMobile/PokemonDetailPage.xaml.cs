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
    // Held item is real from Gen2 on; Gen1's setter is a hard no-op (PK1.cs:157 - that byte is the
    // catch rate). Same disable-but-show-the-truth treatment as the three above.
    bool heldItemEditable;
    // Ball is real from Gen3 on (incl. Gen4's composite BallDPPt/BallHGSS, handled internally by
    // the sealed override); Gen1/2 share GBPKM's hard no-op (GBPKM.cs:135). Friendship is real from
    // Gen2 on; Gen1 is a hard no-op (PK1.cs:155). Same treatment as the four above.
    bool ballEditable;
    bool friendshipEditable;
    // Gender is real/independently-stored from Gen4 on (PK4.cs:170, Data[0x40]). Gen1/2 derive it
    // from IV_ATK vs. the gender-ratio threshold (GBPKM.cs:106-119) and Gen3 from PID vs. the same
    // threshold (G3PKM.cs:37) - both hard no-op setters, same treatment as the four fields above.
    bool genderEditable;
    // Pokerus is real from Gen2 on (one packed byte, upper nibble strain / lower nibble days -
    // PK2.cs:83-84). Gen1 is a hard no-op (PK1.cs:149-150) - RBY has no Pokerus mechanic at all.
    bool pokerusEditable;

    // Markings: no marking concept exists at all in Gen1/2 (neither interface implemented - not
    // merely a no-op). None = card disabled/explained; Bool = Gen3 (4 markings)/Gen4-6 (6
    // markings), on/off, tap toggles; Color = Gen7+ (6 markings), None/Blue/Pink, tap cycles.
    enum MarkingsMode { None, Bool, Color }
    MarkingsMode markingsMode;
    int markingsCount;
    // Parallel to markingsCount - only the first markingsCount entries are meaningful/visible.
    readonly bool[] boolMarkings = new bool[6];
    readonly MarkingColor[] colorMarkings = new MarkingColor[6];

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
    // Item IDs in the loaded Pokemon's OWN format ID space (see PopulateHeldItem) - blank/unused
    // IDs are filtered out of the visible list, so this parallel list is what maps a picker index
    // back to a real item ID.
    readonly List<int> itemIds = new();
    // Ball enum values in balllist's own index order (0 = None) - see PopulateBallPicker.
    readonly List<int> ballIds = new();

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
        // Like the move chips, the item icon must repaint on programmatic selection changes during
        // LoadPokemon too, while MarkDirty stays suppressed by isLoading - so both live in one
        // handler rather than as two subscriptions.
        ItemPicker.SelectedIndexChanged += (_, _) => OnHeldItemSelectionChanged();
        BallPicker.SelectedIndexChanged += (_, _) => MarkDirty();
        FriendshipEntry.TextChanged += (_, _) => { ClampEntryToMax(FriendshipEntry, 255); MarkDirty(); };
        GenderPicker.SelectedIndexChanged += (_, _) => MarkDirty();
        // Structural hardware range (one nibble each, 0-15) - same defensive-clamp precedent as
        // the IV/EV/PP fields, not a legality cap (the 0-8 "really obtainable" strain subset is
        // the read-only LegalityAnalysis badge's job to flag, not a picker guard's).
        PokerusStrainEntry.TextChanged += (_, _) => { ClampEntryToMax(PokerusStrainEntry, 15); MarkDirty(); };
        PokerusDaysEntry.TextChanged += (_, _) => { ClampEntryToMax(PokerusDaysEntry, 15); MarkDirty(); };
        // PP is stored as a single byte in every generation (e.g. PK9.cs:340 `Data[0x7A] = (byte)value`),
        // so 0-255 is the real hard ceiling everywhere - clamped live like the IV/EV fields, same
        // defensive-backstop precedent. PP Ups' real ceiling is 3 (the "PP Up" item's max stack
        // effect in every generation). Deliberately NOT clamped to the selected move's own max PP at
        // *this* max PP-Ups - unlike the IV/EV/species fields, PP has no legality-reporting mechanism
        // in this app to fall back on, and an unusually-high-for-the-move PP value is harmless (no
        // derived stat depends on it), so it's treated like the EV-over-budget case: allowed, not
        // blocked.
        Move1PpEntry.TextChanged += (_, _) => { ClampEntryToMax(Move1PpEntry, 255); MarkDirty(); };
        Move2PpEntry.TextChanged += (_, _) => { ClampEntryToMax(Move2PpEntry, 255); MarkDirty(); };
        Move3PpEntry.TextChanged += (_, _) => { ClampEntryToMax(Move3PpEntry, 255); MarkDirty(); };
        Move4PpEntry.TextChanged += (_, _) => { ClampEntryToMax(Move4PpEntry, 255); MarkDirty(); };
        Move1PpUpsEntry.TextChanged += (_, _) => { ClampEntryToMax(Move1PpUpsEntry, 3); MarkDirty(); };
        Move2PpUpsEntry.TextChanged += (_, _) => { ClampEntryToMax(Move2PpUpsEntry, 3); MarkDirty(); };
        Move3PpUpsEntry.TextChanged += (_, _) => { ClampEntryToMax(Move3PpUpsEntry, 3); MarkDirty(); };
        Move4PpUpsEntry.TextChanged += (_, _) => { ClampEntryToMax(Move4PpUpsEntry, 3); MarkDirty(); };
        // One handler per move Picker, not two - the type chip has to refresh on every selection
        // change (including the programmatic ones LoadPokemon fires), while MarkDirty must stay
        // suppressed during load. Those are different concerns on the same event, so they're
        // combined in OnMoveSelectionChanged rather than stacked as a second subscription.
        Move1Picker.SelectedIndexChanged += (_, _) => OnMoveSelectionChanged(Move1Picker, Move1TypeChip, Move1TypeLabel, Move1PpEntry, Move1PpUpsEntry);
        Move2Picker.SelectedIndexChanged += (_, _) => OnMoveSelectionChanged(Move2Picker, Move2TypeChip, Move2TypeLabel, Move2PpEntry, Move2PpUpsEntry);
        Move3Picker.SelectedIndexChanged += (_, _) => OnMoveSelectionChanged(Move3Picker, Move3TypeChip, Move3TypeLabel, Move3PpEntry, Move3PpUpsEntry);
        Move4Picker.SelectedIndexChanged += (_, _) => OnMoveSelectionChanged(Move4Picker, Move4TypeChip, Move4TypeLabel, Move4PpEntry, Move4PpUpsEntry);
    }

    private void OnMoveSelectionChanged(Picker picker, Border chip, Label label, Entry ppEntry, Entry ppUpsEntry)
    {
        RefreshMoveTypeChip(picker, chip, label);
        // A newly-selected move gets full PP at 0 PP Ups, matching real-game "learn a new move"
        // behavior (SetMoves does the same auto-max at save time) - the load path calls
        // PopulatePpFields immediately afterward, which overwrites this with the mon's real stored
        // values, so this only has a visible effect on a genuine interactive move change.
        if (pk is not null)
        {
            ushort move = MoveIdFor(picker);
            ppUpsEntry.Text = "0";
            ppEntry.Text = (move == 0 ? 0 : pk.GetMovePP(move, 0)).ToString();
        }
        // MarkDirty gates itself on isLoading, so the chip above still refreshes during load while
        // the Save button correctly stays clean.
        MarkDirty();
    }

    // Repaints the held-item icon for the CURRENTLY SELECTED item, before anything is written to
    // the PKM. The stored ID must be converted into the modern sprite ID space the vendored icons
    // are numbered in - pk.SpriteItem does that for the saved value, but a pending selection isn't
    // saved yet, so the same conversion is applied explicitly via ItemConverter.GetItemForFormat
    // (which is exactly what PKM.SpriteItem's overrides call underneath).
    private void OnHeldItemSelectionChanged()
    {
        if (pk is not null)
        {
            int pending = HeldItemIdFor();
            RefreshHeldItemIcon(ItemConverter.GetItemForFormat(pending, pk.Context, EntityContext.Gen9));
        }
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

    // Hands off to PokemonTransferPage using the exact same NavigationState contract this page
    // itself was reached with. parentSave/partyIndex are forwarded as-is (including null, for a
    // box-opened read-only mon) - PokemonTransferPage.ConfigureImportAvailability already handles
    // "no writable slot" by hiding the import cards and keeping export available.
    private async void OnTransferClicked(object? sender, EventArgs e)
    {
        if (pk is null)
            return;

        NavigationState.PendingPokemon = pk;
        NavigationState.PendingPokemonSave = parentSave;
        NavigationState.PendingPokemonIndex = partyIndex;
        await Shell.Current.GoToAsync(nameof(PokemonTransferPage));
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
        PopulateBallPicker(p);
        PopulateFriendship(p);
        PopulateGenderPicker(p);
        PopulatePokerus(p);
        PopulateMarkings(p);
        PopulatePpFields(p);
        RefreshComputed(p);
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

    // Held item. Editable from Gen2 on via pk.ApplyHeldItem; a hard no-op on Gen1.
    //
    // Two ID-space traps here, both real and both easy to get silently wrong:
    //
    // (a) The PICKER must be built from GameInfo.Strings.GetItemStrings(p.Context), not from the
    //     modern GameInfo.Strings.Item list. Gen1, Gen2, Gen3, Gen4, Gen8b and Gen9 each get their
    //     own array (GameStrings.cs:794-805) because those formats number items differently. Using
    //     the modern list would offer names that don't correspond to the IDs actually being stored.
    //     Because the returned array is indexed by that format's OWN item IDs, the picker index maps
    //     straight to the value pk.HeldItem takes.
    //
    // (b) The SPRITE must be looked up from p.SpriteItem, not p.HeldItem. PK2.cs:59 routes through
    //     ItemConverter.GetItemFuture2 and PK3/CK3/XK3 through GetItemFuture3, converting the stored
    //     old-format ID into the Gen4+ ID space. The app's vendored item icons are numbered in that
    //     modern space, so passing the raw HeldItem would render the wrong icon on Gen2 AND Gen3.
    //     (CAPABILITY-AUDIT.md 3.3 only warns about Gen2; Gen3 has the same problem - verified
    //     against the source and in verify/DetailFieldEdits part B.)
    private void PopulateHeldItem(PKM p)
    {
        var names = GameInfo.Strings.GetItemStrings(p.Context);
        int max = Math.Min(p.MaxItemID, names.Length - 1);

        itemIds.Clear();
        var items = new List<string> { "(None)" };
        itemIds.Add(0);
        for (int id = 1; id <= max; id++)
        {
            // Unused IDs come back blank in these arrays; skipping them keeps the list navigable
            // without needing a hardcoded valid-ID table to maintain. itemIds preserves the real
            // ID for every surviving row, so the filtering can never shift the mapping.
            if (string.IsNullOrWhiteSpace(names[id]))
                continue;
            itemIds.Add(id);
            items.Add(names[id]);
        }
        ItemPicker.ItemsSource = items;

        heldItemEditable = p.Generation >= 2;
        ItemPicker.IsEnabled = heldItemEditable;
        ItemCaptionLabel.Text = heldItemEditable
            ? "Held item"
            : "Held item (Gen 1 has no held items - that byte is the catch rate, see PROGRESS.md)";
        ItemPicker.Title = heldItemEditable
            ? "Select held item"
            : "N/A (no held items in Gen 1)";

        // A stored ID with no name in this format's table (corrupt or out of range) falls back to
        // "(None)" rather than crashing on an unfound index, same as the move/form picker fallbacks.
        int sel = itemIds.IndexOf(p.HeldItem);
        ItemPicker.SelectedIndex = sel >= 0 ? sel : 0;

        RefreshHeldItemIcon(p.SpriteItem);
    }

    private void RefreshHeldItemIcon(int spriteItemId)
    {
        bool hasItem = spriteItemId > 0;
        ItemIconBorder.IsVisible = hasItem;
        ItemSpriteImage.Source = hasItem ? SpriteHelper.ItemSpriteFile(spriteItemId) : null;
    }

    private int HeldItemIdFor()
    {
        int idx = ItemPicker.SelectedIndex;
        return idx >= 0 && idx < itemIds.Count ? itemIds[idx] : 0;
    }

    // Ball: real, independently-stored, working setter from Gen3 on - GameInfo.Strings.balllist is
    // already indexed by the Ball enum's own byte values (0 = None), not a separate item ID space
    // like held items, so no ID-space conversion is needed here. Gen1/2 share GBPKM's hard no-op
    // (GBPKM.cs:135), so the getter always reads 0 ("None") there - shown and disabled, same
    // precedent as the other four disable-but-show-the-truth fields on this page.
    private void PopulateBallPicker(PKM p)
    {
        var names = GameInfo.Strings.balllist;
        int max = Math.Min(p.MaxBallID, names.Length - 1);

        ballIds.Clear();
        var items = new List<string>(max + 1);
        for (int id = 0; id <= max; id++)
        {
            ballIds.Add(id);
            items.Add(names[id]);
        }
        BallPicker.ItemsSource = items;

        ballEditable = p.Generation >= 3;
        BallPicker.IsEnabled = ballEditable;
        BallCaptionLabel.Text = ballEditable
            ? "Ball"
            : "Ball (not stored before Gen 3 - see PROGRESS.md)";

        int sel = ballIds.IndexOf(p.Ball);
        BallPicker.SelectedIndex = sel >= 0 ? sel : 0;
    }

    private int BallIdFor()
    {
        int idx = BallPicker.SelectedIndex;
        return idx >= 0 && idx < ballIds.Count ? ballIds[idx] : 0;
    }

    // Friendship: real, independently-stored, working setter from Gen2 on. Gen3/4/5 alias
    // OriginalTrainerFriendship (G3PKM.cs:39, G4PKM.cs:52, PK5.cs:56) - a genuine write to real
    // storage, just under a differently-purposed field name, not a no-op. Gen1 is a hard no-op
    // (PK1.cs:155, get => 0; set { } - RBY has no friendship stat at all).
    private void PopulateFriendship(PKM p)
    {
        friendshipEditable = p.Generation >= 2;
        FriendshipEntry.IsEnabled = friendshipEditable;
        FriendshipEntry.Text = p.CurrentFriendship.ToString();
        FriendshipCaptionLabel.Text = friendshipEditable
            ? "Friendship (0-255)"
            : "Friendship (not stored in Gen 1 - see PROGRESS.md)";
    }

    // Gender: real, independently-stored, working setter starting Gen4 (PK4.cs:170, Data[0x40]).
    // Gen1/2 derive it from IV_ATK vs. the species' gender-ratio threshold (GBPKM.cs:106-119);
    // Gen3 derives it from PID vs. the same threshold (G3PKM.cs:37) - both hard no-op setters, same
    // disable-but-show-the-truth treatment as Ball/Friendship/Ability/Nature above. Unlike those
    // fields there is no format-specific ID list to build - Male/Female/Genderless is the complete,
    // fixed set in every generation (pk.Gender is a byte 0/1/2 uniformly).
    private void PopulateGenderPicker(PKM p)
    {
        GenderPicker.ItemsSource = new List<string> { "Male", "Female", "Genderless" };

        genderEditable = p.Generation >= 4;
        GenderPicker.IsEnabled = genderEditable;
        GenderCaptionLabel.Text = genderEditable
            ? "Gender"
            : "Gender (derived from IVs/PID before Gen 4, not directly editable - see PROGRESS.md)";
        GenderPicker.Title = "Select gender";
        GenderPicker.SelectedIndex = p.Gender switch { 0 => 0, 1 => 1, _ => 2 };
    }

    // Pokerus: real, independently-stored, working setter from Gen2 on - one packed byte, upper
    // nibble strain / lower nibble days, identical layout confirmed across PK2/PK3/PK9 in
    // verify/PokerusEdit. Gen1 is a hard no-op (PK1.cs:149-150, get => 0; set { } - RBY has no
    // Pokerus mechanic at all), disabled there with the reason inline, same precedent as the
    // fields above. No SPLIT beyond that single Gen1 gate - PA8/PK9/PA9 never naturally produce a
    // nonzero value in the real games (Editing/Pokerus.cs's IsObtainable), but the storage itself
    // is real there too, so this is left editable and applied-as-is (the read-only legality badge
    // is what reports an implausible value, not a picker guard) - consistent with how Nature/
    // Gender/Ability already work on this page.
    private void PopulatePokerus(PKM p)
    {
        pokerusEditable = p.Generation >= 2;
        PokerusStrainEntry.IsEnabled = pokerusEditable;
        PokerusDaysEntry.IsEnabled = pokerusEditable;
        PokerusStrainEntry.Text = p.PokerusStrain.ToString();
        PokerusDaysEntry.Text = p.PokerusDays.ToString();
        PokerusCaptionLabel.Text = pokerusEditable
            ? "Pokerus"
            : "Pokerus (not stored in Gen 1 - see PROGRESS.md)";
    }

    (Border Chip, Label Glyph)[] MarkingChips => new[]
    {
        (MarkingCircleChip, MarkingCircleGlyph), (MarkingTriangleChip, MarkingTriangleGlyph),
        (MarkingSquareChip, MarkingSquareGlyph), (MarkingHeartChip, MarkingHeartGlyph),
        (MarkingStarChip, MarkingStarGlyph), (MarkingDiamondChip, MarkingDiamondGlyph),
    };

    // Markings: the 6 shapes shown under a Pokemon in-game, no legality/battle effect - purely a
    // player-sorting aid. IAppliedMarkings<bool>/<MarkingColor> from PKM.cs, implemented by G3PKM
    // (bool, MarkingCount=4)/G4PKM+PK5/PK6 (bool, MarkingCount=6)/PK7+ (MarkingColor,
    // MarkingCount=6) - verified in verify/MarkingsEdit. Gen1/2 implement NEITHER interface at
    // all - no marking concept exists there, not merely unstored, so the whole card is disabled
    // with an inline explanation rather than showing dead chips.
    private void PopulateMarkings(PKM p)
    {
        var chips = MarkingChips;
        if (p is IAppliedMarkings<MarkingColor> colorMarks)
        {
            markingsMode = MarkingsMode.Color;
            markingsCount = colorMarks.MarkingCount;
            for (int i = 0; i < markingsCount; i++)
                colorMarkings[i] = colorMarks.GetMarking(i);
            MarkingsCaptionLabel.Text = "Tap to cycle: none -> blue -> pink";
        }
        else if (p is IAppliedMarkings<bool> boolMarks)
        {
            markingsMode = MarkingsMode.Bool;
            markingsCount = boolMarks.MarkingCount;
            for (int i = 0; i < markingsCount; i++)
                boolMarkings[i] = boolMarks.GetMarking(i);
            MarkingsCaptionLabel.Text = markingsCount < 6
                ? "Tap to toggle (Star/Diamond not available before Gen 4)"
                : "Tap to toggle";
        }
        else
        {
            markingsMode = MarkingsMode.None;
            markingsCount = 0;
            MarkingsCaptionLabel.Text = "Markings (no marking slots exist before Gen 3 - see PROGRESS.md)";
        }

        for (int i = 0; i < chips.Length; i++)
        {
            bool available = i < markingsCount;
            chips[i].Chip.IsEnabled = available;
            chips[i].Chip.Opacity = available ? 1.0 : 0.35;
            RefreshMarkingChipVisual(i);
        }
    }

    private void RefreshMarkingChipVisual(int index)
    {
        var chips = MarkingChips;
        var (chip, glyph) = chips[index];
        var resources = Application.Current?.Resources;
        if (resources is null || index >= markingsCount)
            return;

        string colorKey;
        string glyphColorKey;
        if (markingsMode == MarkingsMode.Color)
        {
            colorKey = colorMarkings[index] switch
            {
                MarkingColor.Blue => "Blue500",
                MarkingColor.Pink => "TypeFairy",
                _ => "SurfaceSunken",
            };
            glyphColorKey = colorMarkings[index] == MarkingColor.None ? "TextTertiary" : "TextOnAccent";
        }
        else
        {
            bool on = boolMarkings[index];
            colorKey = on ? "Slate700" : "SurfaceSunken";
            glyphColorKey = on ? "TextOnAccent" : "TextTertiary";
        }
        chip.BackgroundColor = (Color)resources[colorKey];
        glyph.TextColor = (Color)resources[glyphColorKey];
    }

    private void OnMarkingChipTapped(object? sender, TappedEventArgs e)
    {
        if (markingsMode == MarkingsMode.None || e.Parameter is not string paramStr || !int.TryParse(paramStr, out int index) || index >= markingsCount)
            return;

        if (markingsMode == MarkingsMode.Color)
            colorMarkings[index] = colorMarkings[index] switch
            {
                MarkingColor.None => MarkingColor.Blue,
                MarkingColor.Blue => MarkingColor.Pink,
                _ => MarkingColor.None,
            };
        else
            boolMarkings[index] = !boolMarkings[index];

        RefreshMarkingChipVisual(index);
        MarkDirty();
    }

    // PP / PP Ups: pk.MoveN_PP / pk.MoveN_PPUps, uniform abstract members with no per-generation
    // split (PKM.cs:133-140). The app previously only ever called SetMoves (auto-maxes PP on a
    // move change, still does at save time here too) - this displays and allows overriding the
    // actual stored current PP and PP Up count independently.
    private void PopulatePpFields(PKM p)
    {
        Move1PpEntry.Text = p.Move1_PP.ToString();
        Move1PpUpsEntry.Text = p.Move1_PPUps.ToString();
        Move2PpEntry.Text = p.Move2_PP.ToString();
        Move2PpUpsEntry.Text = p.Move2_PPUps.ToString();
        Move3PpEntry.Text = p.Move3_PP.ToString();
        Move3PpUpsEntry.Text = p.Move3_PPUps.ToString();
        Move4PpEntry.Text = p.Move4_PP.ToString();
        Move4PpUpsEntry.Text = p.Move4_PPUps.ToString();
    }

    // Read-only "Computed" card: battle stats freshly calculated from the PKM's own current
    // IV/EV/level/nature/species via PKM.GetStats(PersonalInfo) - this does NOT read or require the
    // stored party stat block (Stat_HPMax etc.), so it is correct for both a party mon and a box mon
    // without needing CLAUDE.md's "box slots can carry a stale/zeroed party stat block" workaround
    // that ResetPartyStats-on-write already handles elsewhere on this page. Type chip(s) are sourced
    // from PersonalTable.SV (the same current-games source PokedexService uses), not the per-save-
    // format pk.PersonalInfo.Type1/Type2 - that raw byte is Gen1-format-specific internal encoding
    // (PersonalInfo1.Type1 = Data[0x06], includes an unused "Bird" type slot) and is NOT safe to
    // index into GameInfo.Strings.Types directly; confirmed by a real ArgumentOutOfRangeException
    // crash in verify/GenderPPEdit before this was fixed to go through PersonalTable.SV instead. This
    // deliberately shows the CURRENT-games type (e.g. Fairy for Clefairy) rather than this mon's
    // origin-generation type - same tradeoff the Pokedex feature already made.
    private void RefreshComputed(PKM p)
    {
        var stats = p.GetStats(p.PersonalInfo);
        ComputedHpLabel.Text = stats[0].ToString();
        ComputedAtkLabel.Text = stats[1].ToString();
        ComputedDefLabel.Text = stats[2].ToString();
        ComputedSpeLabel.Text = stats[3].ToString();
        ComputedSpaLabel.Text = stats[4].ToString();
        ComputedSpdLabel.Text = stats[5].ToString();

        var resources = Application.Current?.Resources;
        if (resources is not null)
        {
            var pi = PersonalTable.SV.GetFormEntry(p.Species, p.Form);
            SetComputedTypeChip(ComputedType1Chip, ComputedType1Label, pi.Type1, resources);
            bool hasSecond = pi.Type2 != pi.Type1;
            ComputedType2Chip.IsVisible = hasSecond;
            if (hasSecond)
                SetComputedTypeChip(ComputedType2Chip, ComputedType2Label, pi.Type2, resources);
        }

        // Hidden Power: Gen3+ only here - see verify/GenderPPEdit for why the Gen1/2 (GB-era) path
        // is deliberately excluded (its raw type-index encoding is a different, unverified mapping;
        // Gen1 doesn't have the Hidden Power move at all).
        Span<int> ivs = stackalloc int[6];
        p.GetIVs(ivs);
        if (p.Generation >= 3)
        {
            int typeIndex = 1 + HiddenPower.GetType(ivs, p.Context); // skip Normal - see ShowdownSet.cs
            HiddenPowerLabel.Text = $"Hidden Power: {GameInfo.Strings.Types[typeIndex]}";
            HiddenPowerLabel.IsVisible = true;
        }
        else
        {
            HiddenPowerLabel.IsVisible = false;
        }

        // Characteristic: the "It takes plenty of siestas!"-style flavor line derived from
        // whichever IV happens to be highest and the mon's own EncryptionConstant (real games use
        // this exact mon-specific tiebreak so two same-IV mons don't always show the same stat).
        // Gen3+ only - EncryptionConstant is a hard 0 on Gen1/2 (GBPKM.cs:124, sealed no-op
        // setter), and Gen1/2 never had a Characteristic mechanic in the real games at all, so
        // this is a structural gate, not a display preference (verified in verify/
        // CharacteristicDisplay - Gen3-5 alias EncryptionConstant to PID, a real working value,
        // not a no-op, so the calculation is meaningful there too despite EC not being
        // independently stored pre-Gen6). GameInfo.Strings.characteristics is PKHeX.Core's own
        // text table (GameStrings.cs "character" localization key) - not hand-written, unlike
        // the Pokedex's Dex Entries flavor text which genuinely isn't in the library.
        if (p.Generation >= 3)
        {
            int charIndex = EntityCharacteristic.GetCharacteristic(p.EncryptionConstant, ivs);
            CharacteristicLabel.Text = charIndex >= 0 && charIndex < GameInfo.Strings.characteristics.Length
                ? GameInfo.Strings.characteristics[charIndex]
                : string.Empty;
            CharacteristicLabel.IsVisible = !string.IsNullOrEmpty(CharacteristicLabel.Text);
        }
        else
        {
            CharacteristicLabel.IsVisible = false;
        }
    }

    private static void SetComputedTypeChip(Border chip, Label label, byte typeId, ResourceDictionary resources)
    {
        string key = typeId < TypeColorKeys.Length ? TypeColorKeys[typeId] : "Normal";
        chip.BackgroundColor = (Color)resources[$"Type{key}"];
        label.TextColor = Colors.White;
        label.Text = GameInfo.Strings.Types[typeId].ToUpperInvariant();
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

        // Per-move breakdown, reusing this SAME LegalityAnalysis instance rather than constructing a
        // second one - la.Info.Moves[i] is the library's own per-slot verdict (move-learn-method
        // matching against the resolved encounter), not a re-implementation. Same refresh cadence as
        // the banner above (load + after save, never per-keystroke - LegalityAnalysis does full
        // encounter matching, which isn't free).
        var ctx = LegalityLocalizationContext.Create(la);
        var moves = la.Info.Moves;
        RefreshMoveLegality(Move1LegalityDot, Move1LegalityGlyph, Move1LegalityCaption, moves, 0, p.Move1, ctx);
        RefreshMoveLegality(Move2LegalityDot, Move2LegalityGlyph, Move2LegalityCaption, moves, 1, p.Move2, ctx);
        RefreshMoveLegality(Move3LegalityDot, Move3LegalityGlyph, Move3LegalityCaption, moves, 2, p.Move3, ctx);
        RefreshMoveLegality(Move4LegalityDot, Move4LegalityGlyph, Move4LegalityCaption, moves, 3, p.Move4, ctx);
    }

    // One move slot's pass/fail dot + (fail-only) reason caption. moves.Length can be less than 4 on
    // formats with fewer move slots (none currently reach this UI, but MoveResult[] is not
    // fixed-length by contract) - index-guarded rather than assumed.
    private static void RefreshMoveLegality(Border dot, Label glyph, Label caption, MoveResult[] moves, int index, ushort moveId, in LegalityLocalizationContext ctx)
    {
        var resources = Application.Current!.Resources;

        // Cleared slot: same neutral treatment as the type chip (RefreshMoveTypeChip) - a "valid
        // empty slot" checkmark would read as a real verdict on a move that isn't there at all.
        if (moveId == 0 || index >= moves.Length)
        {
            dot.IsVisible = false;
            caption.IsVisible = false;
            return;
        }

        var result = moves[index];
        dot.IsVisible = true;
        var suffix = result.Valid ? "Pass" : "Fail";
        dot.Style = (Style)resources[$"MoveLegalityDot{suffix}Style"];
        glyph.Style = (Style)resources[$"MoveLegalityDotLabel{suffix}Style"];
        glyph.Text = result.Valid ? "✓" : "✕"; // check / cross

        // The reason THIS move is flagged illegal, not just that it is - e.g. "Unobtainable" for a
        // move this encounter can't produce, or "Expect <move>" when the library knows what should be
        // there instead.
        caption.Text = result.Valid ? string.Empty : result.Summary(ctx);
        caption.IsVisible = !result.Valid && !string.IsNullOrEmpty(caption.Text);
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

        int newHeldItem = pk.HeldItem;
        if (heldItemEditable)
            newHeldItem = HeldItemIdFor();

        int newBall = pk.Ball;
        if (ballEditable)
        {
            if (BallPicker.SelectedIndex < 0 || BallPicker.SelectedIndex >= ballIds.Count)
            {
                SaveStatusLabel.Text = "Select a ball before saving.";
                return;
            }
            newBall = ballIds[BallPicker.SelectedIndex];
        }

        int newFriendship = pk.CurrentFriendship;
        if (friendshipEditable)
        {
            if (!TryParseStat(FriendshipEntry.Text, 255, out var friendship))
            {
                // Defensive backstop: the live clamp handler already prevents typing past 255.
                SaveStatusLabel.Text = "Friendship must be a number between 0 and 255.";
                return;
            }
            newFriendship = friendship;
        }

        byte newGender = pk.Gender;
        if (genderEditable)
        {
            if (GenderPicker.SelectedIndex < 0)
            {
                SaveStatusLabel.Text = "Select a gender before saving.";
                return;
            }
            newGender = (byte)GenderPicker.SelectedIndex;
        }

        int newPokerusStrain = pk.PokerusStrain;
        int newPokerusDays = pk.PokerusDays;
        if (pokerusEditable)
        {
            if (!TryParseStat(PokerusStrainEntry.Text, 15, out newPokerusStrain) || !TryParseStat(PokerusDaysEntry.Text, 15, out newPokerusDays))
            {
                // Defensive clamp backstop: the live TextChanged handlers already prevent typing
                // past 15 (one nibble each, the real hardware range).
                SaveStatusLabel.Text = "Pokerus Strain and Days must each be 0-15.";
                return;
            }
        }

        // PP/PP-Ups are uniform across every generation (no editable-gate needed, unlike the fields
        // above) - resolved and validated the same way regardless of format.
        if (!TryParseStat(Move1PpEntry.Text, 255, out var move1Pp) || !TryParseStat(Move1PpUpsEntry.Text, 3, out var move1PpUps) ||
            !TryParseStat(Move2PpEntry.Text, 255, out var move2Pp) || !TryParseStat(Move2PpUpsEntry.Text, 3, out var move2PpUps) ||
            !TryParseStat(Move3PpEntry.Text, 255, out var move3Pp) || !TryParseStat(Move3PpUpsEntry.Text, 3, out var move3PpUps) ||
            !TryParseStat(Move4PpEntry.Text, 255, out var move4Pp) || !TryParseStat(Move4PpUpsEntry.Text, 3, out var move4PpUps))
        {
            // Defensive clamp backstop: the live TextChanged handlers above already prevent typing
            // past these ranges - re-validate here too rather than trusting the UI state alone.
            SaveStatusLabel.Text = "PP must be 0-255 and PP Ups must be 0-3.";
            return;
        }

        bool heldItemChanged = heldItemEditable && newHeldItem != pk.HeldItem;
        bool speciesChanged = newSpecies != pk.Species;
        bool formChanged = formEditable && newForm != pk.Form;
        bool abilityChanged = abilityEditable && newAbility != pk.Ability;
        bool natureChanged = natureEditable && newNature != pk.Nature;
        bool genderChanged = genderEditable && newGender != pk.Gender;
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

            // ApplyHeldItem, not `pk.HeldItem = id` (CommonEdits.cs:286): it runs the value through
            // ItemConverter.GetItemForFormat and zeroes anything past pk.MaxItemID, so an item that
            // this format cannot represent becomes "no item" instead of a corrupt ID. Source context
            // is pk.Context because the picker is already built in this format's own ID space, which
            // makes the conversion an identity and leaves just the bounds check. Held item does not
            // feed the stat block, so it is deliberately absent from statsAffected (same as Ability).
            if (heldItemEditable)
                pk.ApplyHeldItem(newHeldItem, pk.Context);

            // Ball and Friendship don't feed the stat block (battle mechanics / an OT-relationship
            // counter, not a computed stat), so - like Ability - deliberately absent from
            // statsAffected.
            if (ballEditable)
                pk.Ball = (byte)newBall;
            if (friendshipEditable)
                pk.CurrentFriendship = (byte)newFriendship;
            // Gender does not feed the stat block either (battle mechanics only) - same
            // statsAffected exclusion as Ability/Ball/Friendship above.
            if (genderEditable)
                pk.Gender = newGender;
            // Pokerus is a battle-mechanic counter (EV-gain multiplier/infectiousness), not a
            // computed stat - same statsAffected exclusion as Ball/Friendship/Gender above.
            if (pokerusEditable)
            {
                pk.PokerusStrain = newPokerusStrain;
                pk.PokerusDays = newPokerusDays;
            }
            // Markings are cosmetic (player-sorting aid), no stat/legality effect at all - same
            // statsAffected exclusion as everything else above, but doesn't even need a "changed"
            // flag since there's no legality-warning note tied to this field.
            if (markingsMode == MarkingsMode.Color && pk is IAppliedMarkings<MarkingColor> colorMarks)
            {
                for (int i = 0; i < markingsCount; i++)
                    colorMarks.SetMarking(i, colorMarkings[i]);
            }
            else if (markingsMode == MarkingsMode.Bool && pk is IAppliedMarkings<bool> boolMarks)
            {
                for (int i = 0; i < markingsCount; i++)
                    boolMarks.SetMarking(i, boolMarkings[i]);
            }

            // PP/PP-Ups applied AFTER SetMoves (which already ran above and auto-maxed PP for the
            // new move IDs) so these explicit values are what actually stick, not the auto-max.
            // Uniform across every generation, no editable-gate needed.
            pk.Move1_PP = move1Pp; pk.Move1_PPUps = move1PpUps;
            pk.Move2_PP = move2Pp; pk.Move2_PPUps = move2PpUps;
            pk.Move3_PP = move3Pp; pk.Move3_PPUps = move3PpUps;
            pk.Move4_PP = move4Pp; pk.Move4_PPUps = move4PpUps;

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

            // EntityImportSettings.None, not the implicit default: SaveFile.SetPartySlotAtIndex's
            // default setting resolves to SaveFile.SetUpdatePKM (a static, defaults to Enable),
            // which calls SetPKM -> pk.UpdateHandler(this) - logic that conditions the entity "as
            // if it was traded" into this save (SaveFile.cs's own doc comment on AdaptToSaveFile).
            // For a mon that already lives in this exact party slot and is only being edited in
            // place, that's wrong: found on-device that even a NICKNAME-ONLY edit could flip
            // CurrentHandler and fabricate Handling Trainer data (name/gender/language/friendship)
            // on a real Gen9 save, silently, on every single save. PokemonSlotMover.cs already
            // established the correct fix for the same-save case (see its own comment); this path
            // needed the identical guard and didn't have it until now.
            parentSave.SetPartySlotAtIndex(pk, partyIndex, EntityImportSettings.None);

            var bytes = parentSave.Write().ToArray();

            using var stream = new MemoryStream(bytes);
            var fileName = $"edited_{DateTime.Now:yyyyMMdd_HHmmss}.sav";
            var result = await FileSaver.Default.SaveAsync(fileName, stream, CancellationToken.None);

            if (result.IsSuccessful)
            {
                // Reinforce the caveat at the moment it matters: only when the user actually
                // changed species/form/ability/nature/moves, so a plain nickname/stat edit isn't
                // nagged.
                var note = (speciesChanged || formChanged || abilityChanged || natureChanged || genderChanged || movesChanged)
                    ? " (Species/form/ability/nature/gender/move edits are applied as-is; other tools may flag this as illegal.)"
                    : string.Empty;
                SaveStatusLabel.Text = $"Saved to: {result.FilePath}{note}";
                RefreshHero(pk);
                PopulateHeldItem(pk);
                PopulatePokerus(pk);
                PopulateMarkings(pk);
                PopulatePpFields(pk);
                RefreshComputed(pk);
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
