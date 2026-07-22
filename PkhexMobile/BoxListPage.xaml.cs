using CommunityToolkit.Maui.Storage;
using PKHeX.Core;

namespace PkhexMobile;

public partial class BoxListPage : ContentPage
{
    SaveFile? currentSave;
    int currentBox;

    // Every mutation this page can make - a move/swap, a rename, a sort, a clear - only touches the
    // in-memory SaveFile. Unlike PokemonDetailPage's per-mon edit flow there's no "Save Changes"
    // button naturally in the way, so this page needs its own explicit export action. Tracked so the
    // button doesn't invite exporting an unchanged file, and so it stays obvious that every one of
    // those actions needs a separate, deliberate export step to actually reach disk.
    //
    // Anything new that mutates currentSave MUST set this - Export is the only path to disk, so a
    // change that forgets to flip it is a change the user can never save.
    bool hasUnsavedChanges;

    // Tap-to-select-then-tap-destination state. A SlotLocation, not a bound object, so it survives
    // switching boxes via BoxPicker (the destination doesn't have to be in the box that was on
    // screen when the source was selected) and re-rendering the grids.
    SlotLocation? selected;

    // Drag-and-drop's source, captured in DragStarting and consumed in Drop. Kept as page state
    // (not the DragEventArgs payload) so both grids' Drop handlers can share one code path with
    // the tap-to-select fallback below (PerformMove).
    SlotLocation? dragSource;

    public BoxListPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (NavigationState.PendingSave is { } sav)
        {
            currentSave = sav;
            NavigationState.PendingSave = null;
            hasUnsavedChanges = false;
            // A newly loaded save gets the browse view, never a management panel left open from the
            // previous one - the panel's capability gating is per-save.
            SetManagePanelVisible(false);
        }

        if (currentSave is not null)
        {
            LoadBoxNames(currentSave);
            LoadScopeOptions(currentSave);
            RefreshGrids();
            RefreshManagePanel();
        }
        UpdateExportButtonState();
    }

    // Rebuilds the picker's labels from the save, preserving the selected box. Called on load and
    // again after a rename, since the picker's own text is what a rename changes.
    private void LoadBoxNames(SaveFile sav)
    {
        var names = new List<string>();
        for (int i = 0; i < sav.BoxCount; i++)
            names.Add(BoxManagement.GetDisplayBoxName(sav, i));

        int keep = currentBox;
        BoxPicker.ItemsSource = names;
        if (names.Count > 0)
            BoxPicker.SelectedIndex = Math.Clamp(keep, 0, names.Count - 1);
        currentBox = Math.Max(BoxPicker.SelectedIndex, 0);
    }

    private void LoadScopeOptions(SaveFile sav)
    {
        // Spelled out rather than a checkbox: "All 32 boxes" has to be impossible to misread as
        // "this box" when the next tap deletes everything in scope.
        ScopePicker.ItemsSource = new List<string>
        {
            "This box only",
            $"All {sav.BoxCount} boxes",
        };
        if (ScopePicker.SelectedIndex < 0)
            ScopePicker.SelectedIndex = 0; // conservative default, re-asserted on every save load
    }

    private BoxOpScope CurrentScope => ScopePicker.SelectedIndex == 1 ? BoxOpScope.AllBoxes : BoxOpScope.CurrentBox;

    private void OnScopeChanged(object? sender, EventArgs e) => RefreshScopeSummary();

    private void RefreshScopeSummary()
    {
        if (currentSave is null)
            return;

        var sav = currentSave;
        ScopeSummaryLabel.Text = CurrentScope == BoxOpScope.AllBoxes
            ? $"Box tools below will affect ALL {sav.BoxCount} boxes in this save."
            : $"Box tools below will affect {BoxManagement.GetDisplayBoxName(sav, currentBox)} only.";
    }

    private void OnBoxSelected(object? sender, EventArgs e)
    {
        if (currentSave is null || BoxPicker.SelectedIndex < 0)
            return;

        currentBox = BoxPicker.SelectedIndex;
        // Switching boxes while a slot is selected is deliberately allowed - this is exactly how a
        // box<->box move between two *different* boxes works via the tap fallback: select a slot in
        // box A, switch the picker to box B, tap a destination there. `selected` is a SlotLocation,
        // independent of what's currently rendered, so it survives the picker change untouched.
        RefreshGrids();
        RefreshManagePanel();
    }

    private void OnMoveModeToggled(object? sender, ToggledEventArgs e)
    {
        // Leaving move mode with a pending selection would strand a highlighted cell with no way
        // to complete or clear it from the UI - reset explicitly rather than let it linger.
        selected = null;
        StatusLabel.Text = e.Value
            ? "Move mode: tap a Pokemon, then tap its destination. Tap the same cell again to cancel."
            : string.Empty;
        RefreshGrids();
    }

    private void RefreshGrids()
    {
        if (currentSave is null)
            return;

        var sav = currentSave;

        var partyCells = BuildPartyCells(sav);
        PartyGrid.ItemsSource = partyCells;
        PartyHeaderLabel.Text = $"Party: {sav.PartyCount}/6";

        var boxCells = BuildBoxCells(sav, currentBox);
        BoxGrid.ItemsSource = boxCells;
        int occupied = 0;
        foreach (var c in boxCells)
        {
            if (!c.IsEmpty)
                occupied++;
        }
        BoxHeaderLabel.Text = $"{occupied} Pokemon";
    }

    // Shows every populated party slot plus exactly one trailing empty cell (if PartyCount < 6) -
    // the single valid "append" target PokemonSlotMover accepts (party storage can't have gaps
    // below PartyCount). Rendering further empty cells would offer drop targets the mover
    // correctly rejects as gap-creating - a confusing dead end, not a real option, so they're
    // simply not shown. See PROGRESS.md "Box/party move + swap".
    private List<SlotCellDisplay> BuildPartyCells(SaveFile sav)
    {
        var cells = new List<SlotCellDisplay>();
        int shown = Math.Min(sav.PartyCount + (sav.PartyCount < 6 ? 1 : 0), 6);
        for (int i = 0; i < shown; i++)
        {
            var loc = SlotLocation.Party(i);
            var pk = i < sav.PartyCount ? sav.GetPartySlotAtIndex(i) : null;
            cells.Add(new SlotCellDisplay(loc, pk, IsSameLocation(loc, selected)));
        }
        return cells;
    }

    // Every box slot is rendered, empty or not - boxes can already have holes (unlike party), so
    // there's no equivalent restriction to the party grid's single-append-cell rule above.
    private List<SlotCellDisplay> BuildBoxCells(SaveFile sav, int box)
    {
        var cells = new List<SlotCellDisplay>();
        for (int s = 0; s < sav.BoxSlotCount; s++)
        {
            var loc = SlotLocation.InBox(box, s);
            var pk = sav.GetBoxSlotAtIndex(box, s);
            cells.Add(new SlotCellDisplay(loc, pk, IsSameLocation(loc, selected)));
        }
        return cells;
    }

    private static bool IsSameLocation(SlotLocation a, SlotLocation? b) =>
        b is { } loc && loc.IsParty == a.IsParty && loc.Box == a.Box && loc.Slot == a.Slot;

    private async void OnCellTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Element el || el.BindingContext is not SlotCellDisplay cell || currentSave is null)
            return;

        if (!MoveModeSwitch.IsToggled)
        {
            // Pre-existing browse behavior, unchanged: tap opens the detail screen. Empty cells
            // have nothing to open.
            if (cell.IsEmpty)
                return;
            await OpenDetail(cell);
            return;
        }

        if (selected is null)
        {
            if (cell.IsEmpty)
            {
                StatusLabel.Text = "That slot is empty - tap a Pokemon first.";
                return;
            }
            selected = cell.Location;
            StatusLabel.Text = "Selected. Tap a destination slot (or the same slot again to cancel).";
            RefreshGrids();
            return;
        }

        if (IsSameLocation(cell.Location, selected))
        {
            selected = null;
            StatusLabel.Text = "Selection cleared.";
            RefreshGrids();
            return;
        }

        var from = selected.Value;
        selected = null;
        PerformMove(from, cell.Location);
    }

    // Shared by both interaction methods (tap-to-select and drag-and-drop) so there's exactly one
    // place that calls PokemonSlotMover and refreshes the grids afterward.
    private void PerformMove(SlotLocation from, SlotLocation to)
    {
        if (currentSave is null)
            return;

        try
        {
            PokemonSlotMover.MoveOrSwap(currentSave, from, to);
            StatusLabel.Text = "Moved.";
            hasUnsavedChanges = true;
            UpdateExportButtonState();
        }
        catch (Exception ex)
        {
            // PokemonSlotMover validates before writing anything (see its own comments) - a thrown
            // exception here means nothing was mutated, so it's safe to just report and continue.
            StatusLabel.Text = $"Can't move there: {ex.Message}";
        }
        finally
        {
            RefreshGrids();
        }
    }

    private void UpdateExportButtonState() => ExportBtn.IsEnabled = hasUnsavedChanges;

    // Exports the current in-memory SaveFile state, exactly like PokemonDetailPage.OnSaveChangesClicked's
    // FileSaver flow - a move/swap has nowhere else to reach disk from, since it doesn't go through
    // any per-mon edit form. Deliberately does not touch pk.ResetPartyStats or any per-mon field -
    // PokemonSlotMover already did whatever stat-block work a given move needed at move time.
    private async void OnExportClicked(object? sender, EventArgs e)
    {
        if (currentSave is null)
            return;

        try
        {
            var bytes = currentSave.Write().ToArray();
            using var stream = new MemoryStream(bytes);
            var fileName = $"boxparty_{DateTime.Now:yyyyMMdd_HHmmss}.sav";
            var result = await FileSaver.Default.SaveAsync(fileName, stream, CancellationToken.None);

            if (result.IsSuccessful)
            {
                StatusLabel.Text = $"Saved to: {result.FilePath}";
                hasUnsavedChanges = false;
                UpdateExportButtonState();
            }
            else
            {
                StatusLabel.Text = result.IsCancelled ? "Save cancelled." : $"Save failed: {result.Exception?.Message}";
            }
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
        }
    }

    private void OnCellDragStarting(object? sender, DragStartingEventArgs e)
    {
        if (sender is not Element el || el.BindingContext is not SlotCellDisplay cell || cell.IsEmpty)
        {
            e.Cancel = true;
            return;
        }

        dragSource = cell.Location;
        // A drag supersedes any pending tap-selection so the two fallback paths never conflict
        // mid-gesture (e.g. a tap-selected slot left highlighted while a drag completes elsewhere).
        selected = null;
    }

    private void OnCellDrop(object? sender, DropEventArgs e)
    {
        if (sender is not Element el || el.BindingContext is not SlotCellDisplay cell || dragSource is null)
            return;

        var from = dragSource.Value;
        dragSource = null;

        if (IsSameLocation(cell.Location, from))
        {
            RefreshGrids(); // Clear the selected-look left over from DragStarting above.
            return;
        }

        PerformMove(from, cell.Location);
    }

    private async Task OpenDetail(SlotCellDisplay cell)
    {
        if (cell.Source is null)
            return;

        NavigationState.PendingPokemon = cell.Source;
        if (cell.Location.IsParty)
        {
            // Read-write: writes back into the exact party slot this mon was read from, exactly
            // like PartyListPage's own navigation.
            NavigationState.PendingPokemonSave = currentSave;
            NavigationState.PendingPokemonIndex = cell.Location.Slot;
        }
        else
        {
            // Read-only: leave PendingPokemonSave null so the detail page hides Save Changes and
            // never calls SetPartySlotAtIndex - box slots aren't party slots, and writing one back
            // through the party-index path would corrupt data or go out of range. Unchanged from
            // the pre-existing box read-only guard (see PROGRESS.md "PC box viewing, read-only").
            NavigationState.PendingPokemonSave = null;
            NavigationState.PendingPokemonIndex = 0;
        }

        await Shell.Current.GoToAsync(nameof(PokemonDetailPage));
    }

    // ===================================================================================
    // Box management (rename / sort / compact / clear / read-only details)
    //
    // All capability gating lives in BoxManagement.cs so verify/BoxManagement can link and
    // exercise the same code the app runs. This half is presentation only: it must never decide
    // for itself whether a save supports something.
    // ===================================================================================

    private void OnManageToggleClicked(object? sender, EventArgs e)
        => SetManagePanelVisible(!ManagePanel.IsVisible);

    // The panel and the box grid share a grid row and are mutually exclusive - see the XAML comment
    // for why swapping beats stacking on a phone.
    private void SetManagePanelVisible(bool visible)
    {
        ManagePanel.IsVisible = visible;
        BoxGrid.IsVisible = !visible;
        ManageBtn.Text = visible ? "Done" : "Manage";
        if (visible)
            RefreshManagePanel(); // forces its own re-measure at the end - see there for why
    }

    // Applies every capability probe to the controls. The rule throughout: a control the save can't
    // support is *disabled but still shows the true current value*, with an inline reason - never
    // hidden, and never left enabled to accept an edit that would silently evaporate.
    private void RefreshManagePanel()
    {
        if (currentSave is null || !ManagePanel.IsVisible)
            return;

        var sav = currentSave;

        // --- Rename ---
        bool canRename = BoxManagement.CanRenameBoxes(sav);
        var stored = BoxManagement.GetStoredBoxName(sav, currentBox);

        // Sourced from the *stored* name, never GetDisplayBoxName: prefilling the field with the
        // "Box N" fallback and letting the user press Rename without touching it would persist that
        // literal string into the save as a real, chosen name. The fallback is a placeholder only.
        BoxNameEntry.Text = stored ?? string.Empty;
        BoxNameEntry.Placeholder = BoxManagement.GetDisplayBoxName(sav, currentBox);
        BoxNameEntry.MaxLength = BoxManagement.GetBoxNameMaxLength(sav);
        BoxNameEntry.IsEnabled = canRename;
        RenameBtn.IsEnabled = canRename;
        BoxNameCaptionLabel.Text = canRename
            ? $"Name (up to {BoxManagement.GetBoxNameMaxLength(sav)} characters)"
            : "Name (read-only)";
        BoxNameNoticeLabel.IsVisible = !canRename;
        if (!canRename)
        {
            // Two genuinely different reasons, and the distinction is visible to the user: Gen1 can
            // report a name it can't store, Let's Go can't even do that.
            BoxNameNoticeLabel.Text = BoxManagement.CanReadBoxNames(sav)
                ? "This game doesn't store box names - the name shown is a fixed default, so it can't be changed."
                : "This save type has no box names at all - the name shown is supplied by the app for display only.";
        }

        // --- Scope summary ---
        RefreshScopeSummary();

        // --- Current box ---
        bool canSetCurrent = BoxManagement.CanPersistCurrentBox(sav);
        CurrentBoxValueLabel.Text = canSetCurrent
            ? BoxManagement.GetDisplayBoxName(sav, Math.Clamp(sav.CurrentBox, 0, Math.Max(sav.BoxCount - 1, 0)))
            : "Not stored";
        SetCurrentBoxBtn.IsEnabled = canSetCurrent && sav.CurrentBox != currentBox;
        CurrentBoxNoticeLabel.IsVisible = !canSetCurrent;
        CurrentBoxNoticeLabel.Text =
            "This save type doesn't record which box the game opens on, so setting it would look like it worked but never reach the file.";

        // --- Boxes unlocked (self-describing sentinel: base SaveFile returns -1) ---
        var unlocked = BoxManagement.GetBoxesUnlocked(sav);
        BoxesUnlockedValueLabel.Text = unlocked is { } u
            ? $"{u} of {sav.BoxCount}"
            : "Not tracked by this game";

        // --- Wallpaper (read-only by design - see BoxManagement.GetBoxWallpaper) ---
        var wallpaper = BoxManagement.GetBoxWallpaper(sav, currentBox);
        WallpaperValueLabel.Text = wallpaper is { } w ? $"#{w}" : "Not supported by this save type";
        WallpaperNoticeLabel.Text = wallpaper is null
            ? "This game has no per-box wallpapers."
            : "Read-only: PKHeX.Core exposes no valid wallpaper range to check an edit against.";

        // Real on-device bug, confirmed via uiautomator + manual scroll testing: the Android
        // renderer can collapse this ScrollView's content to near-zero height whenever anything
        // inside it changes text/visibility - not just on the initial IsVisible toggle in
        // SetManagePanelVisible, but again after Sort/Rename/Clear re-run this same method and
        // change label text lengths. The Rename and Box tools cards became permanently unreachable
        // (existed in the accessibility tree with degenerate bounds, never actually scrollable into
        // view) until forcing a fresh measure pass here, on every refresh - not just the first one.
        ManagePanel.InvalidateMeasure();
    }

    private async void OnRenameClicked(object? sender, EventArgs e)
    {
        if (currentSave is null)
            return;

        var result = BoxManagement.RenameBox(currentSave, currentBox, BoxNameEntry.Text ?? string.Empty);
        StatusLabel.Text = result.Message;

        if (!result.Ok)
        {
            await DisplayAlertAsync("Rename box", result.Message, "OK");
            return;
        }

        hasUnsavedChanges = true;
        UpdateExportButtonState();
        LoadBoxNames(currentSave); // the picker's own label is what a rename changes
        RefreshManagePanel();
    }

    private void OnSortSpeciesClicked(object? sender, EventArgs e) => _ = RunSort(BoxSortOrder.Species, "Sort by species");

    private void OnSortLevelClicked(object? sender, EventArgs e) => _ = RunSort(BoxSortOrder.Level, "Sort by level");

    private void OnCompactClicked(object? sender, EventArgs e) => _ = RunSort(BoxSortOrder.Compact, "Close gaps");

    // Reordering doesn't destroy anything, but it is bulk and not undoable in-session, so it still
    // confirms - with the scope spelled out in the prompt rather than only in the panel above it.
    private async Task RunSort(BoxSortOrder order, string title)
    {
        if (currentSave is null)
            return;

        var sav = currentSave;
        var scope = CurrentScope;
        var scopeText = ScopeDescription(sav, scope);

        if (!await DisplayAlertAsync(title, $"{title} across {scopeText}?\n\nThis rearranges stored Pokemon. Nothing is deleted.", "Continue", "Cancel"))
            return;

        SetBusy(true, "Working...");
        // Off the UI thread: a Gen9 PC is ~960 slots, and Sort parses every one of them twice for
        // its own before/after integrity check.
        var result = await Task.Run(() => BoxManagement.Sort(sav, scope, currentBox, order));
        SetBusy(false, result.Message);

        if (result.Ok)
        {
            hasUnsavedChanges = true;
            UpdateExportButtonState();
        }
        else
        {
            await DisplayAlertAsync(title, result.Message, "OK");
        }

        RefreshGrids();
        RefreshManagePanel();
    }

    private async void OnClearClicked(object? sender, EventArgs e)
    {
        if (currentSave is null)
            return;

        var sav = currentSave;
        var scope = CurrentScope;
        var scopeText = ScopeDescription(sav, scope);

        SetBusy(true, "Counting...");
        int start = scope == BoxOpScope.AllBoxes ? 0 : currentBox;
        int stop = scope == BoxOpScope.AllBoxes ? sav.BoxCount - 1 : currentBox;
        int doomed = await Task.Run(() => BoxManagement.CountStored(sav, start, stop));
        SetBusy(false, string.Empty);

        if (doomed == 0)
        {
            StatusLabel.Text = $"Nothing to delete - {scopeText} already empty.";
            return;
        }

        if (!await DisplayAlertAsync("Delete Pokemon",
                $"Permanently delete {doomed} Pokemon from {scopeText}?\n\nThis cannot be undone.",
                "Delete", "Cancel"))
            return;

        // A second, differently-worded confirmation for the whole-PC case only. Wiping one box is a
        // recoverable-looking mistake; wiping every box is the single most destructive thing this
        // app can do, and one stray tap shouldn't be enough to reach it.
        if (scope == BoxOpScope.AllBoxes &&
            !await DisplayAlertAsync("Delete every Pokemon in the PC?",
                $"This deletes all {doomed} Pokemon in all {sav.BoxCount} boxes. Your party is not affected.\n\nAre you certain?",
                "Yes, delete everything", "Cancel"))
            return;

        SetBusy(true, "Deleting...");
        var result = await Task.Run(() => BoxManagement.Clear(sav, scope, currentBox));
        SetBusy(false, result.Message);

        if (result.Ok)
        {
            hasUnsavedChanges = true;
            UpdateExportButtonState();
        }
        else
        {
            await DisplayAlertAsync("Delete Pokemon", result.Message, "OK");
        }

        RefreshGrids();
        RefreshManagePanel();
    }

    private async void OnSetCurrentBoxClicked(object? sender, EventArgs e)
    {
        if (currentSave is null)
            return;

        var result = BoxManagement.SetCurrentBox(currentSave, currentBox);
        StatusLabel.Text = result.Message;

        if (!result.Ok)
        {
            await DisplayAlertAsync("Box the game opens on", result.Message, "OK");
            return;
        }

        hasUnsavedChanges = true;
        UpdateExportButtonState();
        RefreshManagePanel();
    }

    // Names the *operation's* target - the box selected in this page's picker. Deliberately not
    // sav.CurrentBox, which is a different concept entirely (the box the game opens on) and would
    // put the wrong box name in a delete confirmation.
    private string ScopeDescription(SaveFile sav, BoxOpScope scope) => scope == BoxOpScope.AllBoxes
        ? $"ALL {sav.BoxCount} boxes"
        : BoxManagement.GetDisplayBoxName(sav, currentBox);

    // Blocks every entry point into a bulk operation while one is running, so a second tap can't
    // start a concurrent mutation of the same SaveFile.
    private void SetBusy(bool busy, string status)
    {
        SortSpeciesBtn.IsEnabled = !busy;
        SortLevelBtn.IsEnabled = !busy;
        CompactBtn.IsEnabled = !busy;
        ClearBtn.IsEnabled = !busy;
        RenameBtn.IsEnabled = !busy && currentSave is not null && BoxManagement.CanRenameBoxes(currentSave);
        ScopePicker.IsEnabled = !busy;
        BoxPicker.IsEnabled = !busy;
        ManageBtn.IsEnabled = !busy;
        if (status.Length > 0 || !busy)
            StatusLabel.Text = status;
    }
}
