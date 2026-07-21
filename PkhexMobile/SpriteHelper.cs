namespace PkhexMobile;

// Filenames must match what was actually vendored into Resources/Images/species|items
// (see PROGRESS.md for the exact source/coverage). IDs outside the vendored range simply
// fail to resolve as a MauiImage resource at runtime - callers layer these Images over a
// static placeholder graphic (see SpriteSlot usage in the page XAML) so a miss is silently
// invisible rather than a broken-image glyph or a crash.
public static class SpriteHelper
{
    public static string SpeciesSpriteFile(ushort species, bool shiny) =>
        shiny ? $"spr_{species:D4}_s.png" : $"spr_{species:D4}.png";

    public static string ItemSpriteFile(int itemId) => $"item_{itemId:D4}.png";
}
