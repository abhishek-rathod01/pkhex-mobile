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
}
