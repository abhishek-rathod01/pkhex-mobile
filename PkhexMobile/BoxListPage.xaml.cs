using CommunityToolkit.Maui.Storage;
using PKHeX.Core;

namespace PkhexMobile;

public partial class BoxListPage : ContentPage
{
    SaveFile? currentSave;
    int currentBox;

    // A move/swap only mutates the in-memory SaveFile - unlike PokemonDetailPage's per-mon edit
    // flow, there's no "Save Changes" button naturally in the way here, so this page needs its own
    // explicit export action. Tracked so the button doesn't invite exporting an unchanged file, and
    // so it's obvious a move needs a separate, deliberate export step to actually reach disk.
    bool hasUnsavedMoves;

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
            hasUnsavedMoves = false;
        }

        if (currentSave is not null)
        {
            LoadBoxNames(currentSave);
            RefreshGrids();
        }
        UpdateExportButtonState();
    }

    private void LoadBoxNames(SaveFile sav)
    {
        var names = new List<string>();
        for (int i = 0; i < sav.BoxCount; i++)
        {
            var name = sav is IBoxDetailNameRead r ? r.GetBoxName(i) : BoxDetailNameExtensions.GetDefaultBoxName(i);
            names.Add(string.IsNullOrWhiteSpace(name) ? BoxDetailNameExtensions.GetDefaultBoxName(i) : name);
        }

        BoxPicker.ItemsSource = names;
        if (BoxPicker.SelectedIndex < 0 && names.Count > 0)
            BoxPicker.SelectedIndex = 0;
        currentBox = Math.Max(BoxPicker.SelectedIndex, 0);
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
            hasUnsavedMoves = true;
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

    private void UpdateExportButtonState() => ExportBtn.IsEnabled = hasUnsavedMoves;

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
                hasUnsavedMoves = false;
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
}
