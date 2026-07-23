using System.Text.Json;
using System.Text.Json.Serialization;

namespace PkhexMobile;

/// <summary>
/// Per-game Pokedex flavor text ("It has a preference for hot things...") for the Pokedex detail
/// screen. Unlike the rest of PokedexService, this is NOT sourced from PKHeX.Core - flavor text is
/// hand-written game text that doesn't exist anywhere in the vendored library (a legality/save-
/// editing tool has no reason to carry it). Sourced from PokeAPI ahead of time and bundled as a
/// MauiAsset (Resources/Raw/dexentries/flavortext.json, ~2.6MB, covers all 1025 species across 34
/// game versions Gen1-9) - no network access at runtime, same offline-first stance as the rest of
/// the Pokedex feature.
/// </summary>
public static class PokedexFlavorTextService
{
    private sealed class SpeciesFlavorTextDto
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("entries")] public List<FlavorTextEntryDto> Entries { get; set; } = [];
    }

    private sealed class FlavorTextEntryDto
    {
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("text")] public string Text { get; set; } = "";
    }

    private static Dictionary<string, SpeciesFlavorTextDto>? cache;
    private static bool loadAttempted;

    // PokeAPI's version slugs (e.g. "firered", "omega-ruby") aren't display-ready - several are
    // compound game names a generic "title-case the slug" transform gets wrong (FireRed, HeartGold,
    // SoulSilver), so this is an explicit table rather than a string transform. Covers every slug
    // observed in the bundled data, plus brilliant-diamond/shining-pearl in case PokeAPI adds them
    // in the source data later (not present as of the 2026-07-23 fetch - BDSP appears to be missing
    // from PokeAPI's species flavor text entirely, not a bug in the fetch).
    private static readonly Dictionary<string, string> VersionDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = "Red", ["blue"] = "Blue", ["yellow"] = "Yellow",
        ["gold"] = "Gold", ["silver"] = "Silver", ["crystal"] = "Crystal",
        ["ruby"] = "Ruby", ["sapphire"] = "Sapphire", ["emerald"] = "Emerald",
        ["firered"] = "FireRed", ["leafgreen"] = "LeafGreen",
        ["diamond"] = "Diamond", ["pearl"] = "Pearl", ["platinum"] = "Platinum",
        ["heartgold"] = "HeartGold", ["soulsilver"] = "SoulSilver",
        ["black"] = "Black", ["white"] = "White", ["black-2"] = "Black 2", ["white-2"] = "White 2",
        ["x"] = "X", ["y"] = "Y",
        ["omega-ruby"] = "Omega Ruby", ["alpha-sapphire"] = "Alpha Sapphire",
        ["sun"] = "Sun", ["moon"] = "Moon", ["ultra-sun"] = "Ultra Sun", ["ultra-moon"] = "Ultra Moon",
        ["lets-go-pikachu"] = "Let's Go, Pikachu!", ["lets-go-eevee"] = "Let's Go, Eevee!",
        ["sword"] = "Sword", ["shield"] = "Shield",
        ["brilliant-diamond"] = "Brilliant Diamond", ["shining-pearl"] = "Shining Pearl",
        ["legends-arceus"] = "Legends: Arceus",
        ["scarlet"] = "Scarlet", ["violet"] = "Violet",
    };

    private static string GetGameDisplayName(string versionSlug) =>
        VersionDisplayNames.TryGetValue(versionSlug, out var name) ? name : versionSlug;

    public static async Task<List<PokedexFlavorTextEntry>> GetEntriesAsync(int speciesId)
    {
        await EnsureLoadedAsync();
        if (cache is null || !cache.TryGetValue(speciesId.ToString(), out var data))
            return [];

        // Sibling versions frequently share verbatim identical flavor text (e.g. Black/Black 2,
        // or every Gen1 game for a species PokeAPI didn't bother re-writing) - grouping by exact
        // text and joining the game names cuts real redundancy rather than showing the same
        // paragraph 2-4 times in a row. Order preserved from the source data (PokeAPI's own
        // per-generation ordering), grouped by first occurrence.
        var seen = new List<(string Text, List<string> Games)>();
        foreach (var e in data.Entries)
        {
            string display = GetGameDisplayName(e.Version);
            var existing = seen.Find(g => g.Text == e.Text);
            if (existing.Games is not null)
                existing.Games.Add(display);
            else
                seen.Add((e.Text, [display]));
        }
        return [.. seen.Select(g => new PokedexFlavorTextEntry(string.Join(", ", g.Games), g.Text))];
    }

    private static readonly SemaphoreSlim loadGate = new(1, 1);

    private static async Task EnsureLoadedAsync()
    {
        if (loadAttempted)
            return;
        await loadGate.WaitAsync();
        try
        {
            if (loadAttempted)
                return;
            loadAttempted = true;
            using var stream = await FileSystem.OpenAppPackageFileAsync("dexentries/flavortext.json");
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            cache = await JsonSerializer.DeserializeAsync<Dictionary<string, SpeciesFlavorTextDto>>(stream, options);
        }
        catch
        {
            // Missing/corrupt bundle - callers get an empty list and the UI shows nothing for this
            // card rather than crashing. Never expected in a normal build, but this data file is
            // fetched by a separate process from the rest of the app, so it's treated as optional.
            cache = null;
        }
        finally
        {
            loadGate.Release();
        }
    }
}

public readonly record struct PokedexFlavorTextEntry(string GameDisplayName, string Text);
