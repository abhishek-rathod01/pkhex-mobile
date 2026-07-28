using PKHeX.Core;

namespace PkhexMobile;

/// <summary>
/// Hand-off for complex navigation payloads. Shell's GoToAsync query-dictionary path
/// crashes with InvalidCastException ("Object must implement IConvertible") when a
/// non-IConvertible object (e.g. SaveFile, PKM) is passed to a route registered via
/// Routing.RegisterRoute - Shell tries to coerce dictionary values while resolving the
/// implicit ShellContent, before the destination page ever sees them. Plain static
/// hand-off avoids that path entirely.
/// </summary>
public static class NavigationState
{
    public static SaveFile? PendingSave { get; set; }
    public static PKM? PendingPokemon { get; set; }

    // Carried alongside PendingPokemon so the detail page can write edits back into
    // the correct party slot via SaveFile.SetPartySlotAtIndex.
    public static SaveFile? PendingPokemonSave { get; set; }
    public static int PendingPokemonIndex { get; set; }

    /// <summary>
    /// True while any editor holds edits the user has not exported yet.
    /// </summary>
    /// <remarks>
    /// Dirty state is tracked per-page (BoxListPage.hasUnsavedChanges,
    /// PokemonDetailPage.isDirty); this mirrors it somewhere global so the updater can
    /// warn before an install replaces the app. Editors set it when they go dirty and
    /// clear it on a successful export.
    ///
    /// Deliberately coarse: it is a warning trigger, not a source of truth. Leaving an
    /// editor without exporting keeps it true, which errs toward warning too often
    /// rather than too rarely - the failure that matters here is losing a user's edits.
    /// </remarks>
    public static bool HasUnsavedChanges { get; set; }
}
