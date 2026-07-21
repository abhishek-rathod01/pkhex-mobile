using PKHeX.Core;

namespace PkhexMobile;

/// <summary>
/// A single grid cell in the box/party move UI (<see cref="BoxListPage"/>) - either a populated
/// slot (<see cref="Source"/> non-null) or an empty one that can still be a drag/tap destination.
/// Deliberately a plain record rebuilt and reassigned to the CollectionView's ItemsSource after
/// every change (matches this app's existing list-rebuild pattern in
/// <c>PartyListPage.LoadParty</c>/<c>BoxListPage.LoadBox</c>) rather than an
/// INotifyPropertyChanged view-model - selection highlighting is achieved by recomputing
/// <see cref="IsSelected"/> for the whole list and reassigning, not by mutating a bound property
/// in place.
/// </summary>
public sealed record SlotCellDisplay(SlotLocation Location, PKM? Source, bool IsSelected)
{
    public bool IsEmpty => Source is null || Source.Species == 0;
    // Precomputed rather than an XAML value-converter for the inverse - this project has no
    // converter infrastructure yet and a computed property keeps the DataTemplate binding simple.
    public bool HasSprite => !IsEmpty;
    public bool IsShiny => Source?.IsShiny ?? false;
    public string SpeciesName => IsEmpty ? string.Empty : PkmDisplayHelper.GetSpeciesName(Source!.Species);
    public string Nickname => IsEmpty ? string.Empty : PkmDisplayHelper.GetDisplayName(Source!);
    public string SpriteFile => IsEmpty ? string.Empty : SpriteHelper.SpeciesSpriteFile(Source!.Species, Source!.IsShiny);
    public int Level => Source?.CurrentLevel ?? 0;
    public string LevelText => IsEmpty ? string.Empty : $"Lv{Level}";

    // Slot number shown on empty cells (1-based) so an empty grid position is still identifiable
    // as a specific destination, matching how the party/box slot numbering already works elsewhere.
    public string SlotNumberText => (Location.Slot + 1).ToString();
}
