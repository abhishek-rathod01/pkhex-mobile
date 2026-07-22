using CommunityToolkit.Maui.Storage;
using PKHeX.Core;

namespace PkhexMobile;

/// <summary>
/// Per-Pokémon import/export. Reached with <see cref="NavigationState.PendingPokemon"/> set, and
/// - for the import half - <see cref="NavigationState.PendingPokemonSave"/> plus
/// <see cref="NavigationState.PendingPokemonIndex"/>, exactly the same hand-off contract
/// <see cref="PokemonDetailPage"/> already uses. No new NavigationState fields.
///
/// All logic lives in <see cref="EntityTransferService"/> (harness-verified by
/// verify/ShowdownEntityIO); this file is presentation, file dialogs and messaging only.
///
/// Nothing here legalizes or auto-fixes. An import is applied as written and the warning banner
/// saying so is always visible, never conditional.
/// </summary>
public partial class PokemonTransferPage : ContentPage
{
    private PKM? pk;
    private SaveFile? parentSave;
    private int partySlotIndex;
    private bool isDirty;

    public PokemonTransferPage() => InitializeComponent();

    protected override void OnAppearing()
    {
        base.OnAppearing();

        var p = NavigationState.PendingPokemon;
        var sav = NavigationState.PendingPokemonSave;
        var idx = NavigationState.PendingPokemonIndex;

        // Consume the hand-off exactly like PokemonDetailPage does, so a later re-entry cannot
        // silently reuse a stale payload.
        NavigationState.PendingPokemon = null;
        NavigationState.PendingPokemonSave = null;

        if (p is null)
            return;

        pk = p;
        parentSave = sav;
        partySlotIndex = idx;

        RefreshHero(p);
        RefreshExport(p);
        ConfigureImportAvailability();
    }

    // ---------------------------------------------------------------------------------------
    // Presentation
    // ---------------------------------------------------------------------------------------

    private void RefreshHero(PKM p)
    {
        TitleLabel.Text = PkmDisplayHelper.GetDisplayName(p);
        SubtitleLabel.Text = $"{PkmDisplayHelper.GetSpeciesName(p.Species)}  ·  Lv {p.CurrentLevel}  ·  {p.GetType().Name}";
        ShinyStarLabel.IsVisible = p.IsShiny;
        SpriteImage.Source = SpriteHelper.SpeciesSpriteFile(p.Species, p.IsShiny);
    }

    private void RefreshExport(PKM p)
    {
        ExportEntityHint.Text =
            $"Writes a {p.GetType().Name.ToLowerInvariant()} file: the complete, lossless entity — " +
            "every field including PID, original trainer, met data and ribbons. This is the format to use " +
            "for moving a Pokémon between saves or tools.";

        try
        {
            ShowdownExportEditor.Text = EntityTransferService.ExportShowdown(p);
        }
        catch (Exception ex)
        {
            ShowdownExportEditor.Text = string.Empty;
            ShowSaveStatus($"Could not build a Showdown set: {ex.Message}");
        }
    }

    /// <summary>
    /// The import half needs somewhere to write the result back to. Under the current navigation
    /// contract only party slots are writable - a box-opened Pokémon arrives with a null
    /// PendingPokemonSave (BoxListPage leaves it null on purpose, since writing a box slot through
    /// the party-index path would corrupt data). Rather than fail at the moment the user taps
    /// Import, say so up front and hide the import cards entirely.
    /// </summary>
    private void ConfigureImportAvailability()
    {
        var canImport = parentSave is not null;

        ImportEntityCard.IsVisible = canImport;
        ImportShowdownCard.IsVisible = canImport;
        SaveChangesBtn.IsVisible = canImport;
        ImportUnavailableBorder.IsVisible = !canImport;

        if (!canImport)
        {
            ImportUnavailableLabel.Text =
                "This Pokémon was opened read-only, so there is nowhere to write an import back to. " +
                "Export works normally. To import, open a Pokémon from the party list.";
            return;
        }

        ImportEntityHint.Text =
            $"Reads any .pk file and converts it to this save's format ({parentSave!.PKMType.Name}) if needed. " +
            "Conversions that PKHeX cannot perform are refused with a reason — nothing is written in that case.";

        UpdateSaveButtonState();
    }

    private void UpdateSaveButtonState()
    {
        SaveChangesBtn.IsEnabled = isDirty;
        SaveChangesBtn.Text = isDirty ? "Save changes" : "Saved";
    }

    private void MarkDirty()
    {
        isDirty = true;
        UpdateSaveButtonState();
    }

    private void ShowSaveStatus(string message) => SaveStatusLabel.Text = message;

    /// <summary>
    /// Single landing place for both accepted and refused imports. A refusal is displayed with the
    /// same prominence as a success - the requirement is that the converter's own explanation
    /// reaches the user rather than being swallowed.
    /// </summary>
    private void ShowResult(bool success, string title, string message, string? detail = null)
    {
        ResultBorder.IsVisible = true;
        ResultBorder.BackgroundColor = (Color)(success
            ? Application.Current!.Resources["StatusInfoBg"]
            : Application.Current!.Resources["StatusFailBg"]);
        ResultTitleLabel.TextColor = (Color)(success
            ? Application.Current!.Resources["StatusInfoFg"]
            : Application.Current!.Resources["StatusFailFg"]);

        ResultTitleLabel.Text = title;
        ResultLabel.Text = message;
        ResultDetailLabel.Text = detail ?? string.Empty;
        ResultDetailLabel.IsVisible = !string.IsNullOrWhiteSpace(detail);
    }

    // ---------------------------------------------------------------------------------------
    // Export
    // ---------------------------------------------------------------------------------------

    private async void OnExportEntityClicked(object sender, EventArgs e)
    {
        if (pk is null)
            return;

        try
        {
            var export = EntityTransferService.ExportEntity(pk);
            using var stream = new MemoryStream(export.Data);
            var result = await FileSaver.Default.SaveAsync(export.FileName, stream, CancellationToken.None);

            ShowSaveStatus(result.IsSuccessful
                ? $"Exported {export.Data.Length} bytes to: {result.FilePath}"
                : result.IsCancelled ? "Export cancelled." : $"Export failed: {result.Exception?.Message}");
        }
        catch (Exception ex)
        {
            ShowSaveStatus($"Export failed: {ex.Message}");
        }
    }

    private async void OnCopyShowdownClicked(object sender, EventArgs e)
    {
        var text = ShowdownExportEditor.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowSaveStatus("Nothing to copy.");
            return;
        }

        try
        {
            await Clipboard.Default.SetTextAsync(text);
            ShowSaveStatus("Showdown set copied to the clipboard.");
        }
        catch (Exception ex)
        {
            ShowSaveStatus($"Copy failed: {ex.Message}");
        }
    }

    private async void OnSaveShowdownClicked(object sender, EventArgs e)
    {
        if (pk is null)
            return;

        var text = ShowdownExportEditor.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowSaveStatus("Nothing to save.");
            return;
        }

        try
        {
            // Reuse the entity namer so the .txt sits next to a .pk export of the same mon under a
            // matching name, rather than inventing a second naming scheme.
            var baseName = Path.GetFileNameWithoutExtension(EntityTransferService.BuildFileName(pk));
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
            var result = await FileSaver.Default.SaveAsync($"{baseName}.txt", stream, CancellationToken.None);

            ShowSaveStatus(result.IsSuccessful
                ? $"Saved to: {result.FilePath}"
                : result.IsCancelled ? "Save cancelled." : $"Save failed: {result.Exception?.Message}");
        }
        catch (Exception ex)
        {
            ShowSaveStatus($"Save failed: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------------------------------
    // Import
    // ---------------------------------------------------------------------------------------

    private async void OnImportEntityClicked(object sender, EventArgs e)
    {
        if (parentSave is null)
            return;

        try
        {
            var picked = await FilePicker.Default.PickAsync();
            if (picked is null)
            {
                ShowSaveStatus("Import cancelled.");
                return;
            }

            byte[] bytes;
            await using (var stream = await picked.OpenReadAsync())
            {
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                bytes = ms.ToArray();
            }

            var result = EntityTransferService.ImportEntity(bytes, parentSave);
            if (!result.Success || result.Entity is null)
            {
                ShowResult(false, "IMPORT REFUSED", result.Message,
                    "Nothing was changed. The Pokémon in this slot is untouched.");
                return;
            }

            // Replace the working entity, and write it into the slot with the party stat block
            // recomputed - a .pk file does not carry one, and unlike the Showdown path nothing in
            // the import path recomputes it for us.
            pk = result.Entity;
            EntityTransferService.WriteIntoPartySlot(parentSave, pk, partySlotIndex);

            RefreshHero(pk);
            RefreshExport(pk);
            MarkDirty();

            var what = $"{PkmDisplayHelper.GetSpeciesName(pk.Species)} (Lv {pk.CurrentLevel}) from {Path.GetFileName(picked.FileName)}.";
            ShowResult(true, result.Converted ? "IMPORTED — CONVERTED" : "IMPORTED", what,
                result.Message + "  Not validated: use Save changes to write it to a .sav file.");
        }
        catch (Exception ex)
        {
            ShowResult(false, "IMPORT FAILED", ex.Message, "Nothing was changed.");
        }
    }

    private async void OnPasteShowdownClicked(object sender, EventArgs e)
    {
        try
        {
            var text = await Clipboard.Default.GetTextAsync();
            if (string.IsNullOrWhiteSpace(text))
            {
                ShowSaveStatus("The clipboard is empty.");
                return;
            }
            ShowdownImportEditor.Text = text;
            ShowSaveStatus("Pasted from the clipboard. Tap Apply set to write it in.");
        }
        catch (Exception ex)
        {
            ShowSaveStatus($"Paste failed: {ex.Message}");
        }
    }

    private void OnApplyShowdownClicked(object sender, EventArgs e)
    {
        if (pk is null || parentSave is null)
            return;

        try
        {
            // Apply onto a clone first: ImportShowdown mutates in place, so a refusal partway
            // through must not be able to leave the live entity half-written.
            var candidate = (PKM)pk.Clone();
            var result = EntityTransferService.ImportShowdown(ShowdownImportEditor.Text ?? string.Empty, candidate);

            if (!result.Applied)
            {
                ShowResult(false, "SET REFUSED", result.Message,
                    "Nothing was changed. The Pokémon in this slot is untouched.");
                return;
            }

            pk = candidate;
            EntityTransferService.WriteIntoPartySlot(parentSave, pk, partySlotIndex);

            RefreshHero(pk);
            RefreshExport(pk);
            MarkDirty();

            var detail = result.ParseErrors.Count == 0
                ? "Applied as-is, with no validation. Use Save changes to write it to a .sav file."
                : "Ignored line(s): " + string.Join("; ", result.ParseErrors) +
                  "  Everything else was applied as-is, with no validation.";

            ShowResult(true, result.ParseErrors.Count == 0 ? "SET APPLIED" : "SET APPLIED — WITH WARNINGS",
                $"{PkmDisplayHelper.GetSpeciesName(pk.Species)} (Lv {pk.CurrentLevel}).", detail);
        }
        catch (Exception ex)
        {
            ShowResult(false, "SET FAILED", ex.Message, "Nothing was changed.");
        }
    }

    // ---------------------------------------------------------------------------------------
    // Save
    // ---------------------------------------------------------------------------------------

    private async void OnSaveChangesClicked(object sender, EventArgs e)
    {
        if (parentSave is null || pk is null)
            return;

        try
        {
            // The slot write already happened at import time; re-assert it so a Save is correct
            // even if something later re-read the entity.
            EntityTransferService.WriteIntoPartySlot(parentSave, pk, partySlotIndex);

            var bytes = parentSave.Write().ToArray();
            using var stream = new MemoryStream(bytes);

            // Distinctive default name: the save dialog is known to autocomplete onto an existing
            // nearby file if the suggested name is close to one (PROGRESS.md).
            var fileName = $"imported_{DateTime.Now:yyyyMMdd_HHmmss}.sav";
            var result = await FileSaver.Default.SaveAsync(fileName, stream, CancellationToken.None);

            if (result.IsSuccessful)
            {
                ShowSaveStatus($"Saved to: {result.FilePath} (imported as-is; other tools may flag this as illegal.)");
                isDirty = false;
                UpdateSaveButtonState();
            }
            else
            {
                ShowSaveStatus(result.IsCancelled ? "Save cancelled." : $"Save failed: {result.Exception?.Message}");
            }
        }
        catch (Exception ex)
        {
            ShowSaveStatus($"Error: {ex.Message}");
        }
    }
}
