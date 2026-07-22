using System.Text;
using PKHeX.Core;

namespace PkhexMobile;

/// <summary>
/// Single-Pokémon import/export: the two file-level interchange formats PKHeX desktop exposes per
/// entity - a raw <c>.pkX</c> entity file (lossless, binary) and a Pokémon Showdown text set
/// (lossy by design, human-readable).
///
/// Deliberately UI-free and dependency-free apart from PKHeX.Core, so <c>verify/ShowdownEntityIO</c>
/// can <c>&lt;Compile Include&gt;</c> this exact file and prove the code the app actually runs -
/// same convention as <see cref="PokemonSlotMover"/>.
///
/// **No legalization, ever.** Nothing here calls the encounter-suggestion / auto-fix machinery.
/// An import is applied exactly as the source described it and may well produce an illegal
/// Pokémon; saying so out loud is the caller's job (see <see cref="ShowdownImportResult.Applied"/>
/// and the persistent warning on the transfer page).
/// </summary>
public static class EntityTransferService
{
    // ---------------------------------------------------------------------------------------
    // Export - .pkX entity file
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Serialises <paramref name="pk"/> to the canonical single-entity <c>.pkX</c> file form:
    /// decrypted, <see cref="PKM.SIZE_STORED"/> bytes, no party stat block.
    /// </summary>
    /// <remarks>
    /// Stored (not party) size is what PKHeX desktop writes for a single-entity file and what
    /// <see cref="EntityFormat.GetFromBytes(Memory{byte}, EntityContext)"/> expects to read back;
    /// the party stat block is a derived cache (see <see cref="PKM.ResetPartyStats"/>) and is
    /// recomputed on import rather than carried in the file.
    ///
    /// Note the audit's own draft cited a <c>DecryptedPartyExport</c> member - that does **not**
    /// exist in this vendored PKHeX.Core. <see cref="PKM.WriteDecryptedDataStored"/> is the real
    /// API, and it refreshes the checksum as it writes (PKM.cs:52-58), so the exported bytes are
    /// self-consistent even if the caller mutated the entity beforehand.
    /// </remarks>
    public static EntityExport ExportEntity(PKM pk)
    {
        var buffer = new byte[pk.SIZE_STORED];
        var written = pk.WriteDecryptedDataStored(buffer);

        // WriteDecryptedDataStored returns the length it used; overrides (e.g. the GB formats)
        // may write fewer bytes than SIZE_STORED, in which case the file must be truncated to
        // what was actually written rather than padded with stale zeroes.
        if (written != buffer.Length)
            Array.Resize(ref buffer, written);

        return new EntityExport(BuildFileName(pk), buffer);
    }

    /// <summary>
    /// <c>&lt;name&gt;.&lt;ext&gt;</c> using PKHeX's own namer, with characters no filesystem will
    /// accept stripped.
    /// </summary>
    /// <remarks>
    /// <see cref="EntityFileNamer.GetName"/> embeds the nickname verbatim, and a nickname is
    /// arbitrary user/game text - on the GB formats especially it can decode to glyphs that are
    /// legal in-game but illegal in a file name. The extension comes from
    /// <see cref="PKM.Extension"/> (the concrete type name lower-cased: pk1..pk9, pb7, pa8, ...),
    /// which is exactly the set <see cref="SaveFile.PKMExtensions"/> enumerates.
    /// </remarks>
    public static string BuildFileName(PKM pk)
    {
        var name = EntityFileNamer.GetName(pk);
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(Array.IndexOf(InvalidFileNameChars, c) >= 0 ? '_' : c);

        var cleaned = sb.ToString().Trim();
        if (cleaned.Length == 0)
            cleaned = $"{pk.Species:0000}";

        return $"{cleaned}.{pk.Extension}";
    }

    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    // ---------------------------------------------------------------------------------------
    // Export - Showdown text
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Renders <paramref name="pk"/> as a Pokémon Showdown set.
    /// </summary>
    /// <remarks>
    /// **Lossy by design.** A Showdown set carries species/form, nickname, held item, ability,
    /// level, gender, shininess, nature, friendship, EVs, IVs, up to four moves, and (Gen9) Tera
    /// type. It carries *nothing else*: no PID, no encryption constant, no OT name/ID/gender, no
    /// met location/date/level, no ball, no origin game, no ribbons/markings, no Pokérus, no
    /// hyper-training flags, no memories. Round-tripping through this format is a deliberate
    /// downgrade, not a copy - see <see cref="ImportShowdown"/>.
    ///
    /// One subtlety worth knowing: on Format ≥ 8 the ctor reads <c>pk.StatAlignment</c> (the Mint
    /// byte), not <c>pk.Nature</c> (ShowdownSet.cs:829). So the exported "Nature" is the one the
    /// mon's *stats* are actually computed from, which is the right choice for a battle set but
    /// means a Minted mon exports its Mint rather than its birth nature.
    /// </remarks>
    public static string ExportShowdown(PKM pk) => new ShowdownSet(pk).Text;

    // ---------------------------------------------------------------------------------------
    // Import - .pkX entity file
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Reads a <c>.pkX</c> file and, if its format differs from <paramref name="sav"/>'s, asks
    /// <see cref="EntityConverter"/> to convert it. Never throws on an unusable file and never
    /// silently discards a refusal - the reason always comes back in
    /// <see cref="EntityImportResult.Message"/>.
    /// </summary>
    /// <param name="fileBytes">Raw file contents. Copied before use.</param>
    /// <param name="sav">Destination save; supplies both the target type and the format hint.</param>
    public static EntityImportResult ImportEntity(ReadOnlySpan<byte> fileBytes, SaveFile sav)
    {
        if (fileBytes.Length == 0)
            return EntityImportResult.Fail("The selected file is empty.");

        if (!EntityDetection.IsSizePlausible(fileBytes.Length))
            return EntityImportResult.Fail($"{fileBytes.Length} bytes is not a plausible size for any Pokémon entity file.");

        // EntityFormat.GetFromBytes takes a Memory<byte> and *wraps* it - the returned PKM aliases
        // this array for its whole life, exactly like SaveUtil.GetSaveFile does with save bytes
        // (documented pitfall, PROGRESS.md). Copy so the entity can never be mutated behind our
        // back by whoever owns the caller's buffer.
        var owned = fileBytes.ToArray();

        PKM? loaded;
        try
        {
            // The `prefer` hint disambiguates the genuinely ambiguous format pairs
            // (PK6/PK7, PK8/PB8, PK9/PA9) toward the save we're importing into.
            loaded = EntityFormat.GetFromBytes(owned, sav.Context);
        }
        catch (Exception ex)
        {
            return EntityImportResult.Fail($"Could not read the file as a Pokémon entity: {ex.Message}");
        }

        if (loaded is null)
            return EntityImportResult.Fail("The file was not recognised as a Pokémon entity of any supported format.");

        if (loaded.Species == 0)
            return EntityImportResult.Fail("The file decoded to an empty slot (species 0), not a Pokémon.");

        var destType = sav.PKMType;
        var sourceFormat = loaded.GetType().Name;

        if (loaded.GetType() == destType)
            return EntityImportResult.Ok(loaded, converted: false, $"Imported {sourceFormat} directly - same format as this save.");

        // Cross-generation. ConvertToType reports refusal through `result` rather than throwing,
        // so the ONLY reliable accept signal is a non-null return value: note that a same-format
        // conversion yields EntityConverterResult.None, and `None` is deliberately excluded from
        // EntityConverterResult.IsSuccess (EntityConverterResultExtensions.cs:10). Gating on
        // IsSuccess would therefore reject perfectly good conversions; gate on the entity.
        PKM? converted;
        EntityConverterResult result;
        try
        {
            converted = EntityConverter.ConvertToType(loaded, destType, out result);
        }
        catch (Exception ex)
        {
            return EntityImportResult.Fail($"Conversion from {sourceFormat} to {destType.Name} failed: {ex.Message}");
        }

        if (converted is null)
        {
            // GetDisplayString formats PKHeX's own localized explanation of *why* - "no transfer
            // route", "incompatible species", the Gen1/2 Japanese/International mismatch, etc.
            // Surfacing this verbatim is the whole point: a refused import must look refused.
            return EntityImportResult.Fail(result.GetDisplayString(loaded, destType));
        }

        // A converted entity is not guaranteed to be storable even when a route existed.
        if (!EntityConverter.IsCompatibleWithModifications(converted))
            return EntityImportResult.Fail($"Converted {sourceFormat} to {destType.Name}, but the result is not storable in this save file.");

        if (converted.GetType() != destType)
            return EntityImportResult.Fail($"Conversion produced a {converted.GetType().Name}, which this save cannot store (expected {destType.Name}).");

        return EntityImportResult.Ok(converted, converted: true, result.GetDisplayString(loaded, destType));
    }

    /// <summary>
    /// Writes <paramref name="pk"/> into party slot <paramref name="index"/> of
    /// <paramref name="sav"/>, recomputing the party stat block first.
    /// </summary>
    /// <remarks>
    /// The <c>ResetPartyStats()</c> call is load-bearing and is the whole reason this is a
    /// method rather than two lines at the call site:
    /// <list type="bullet">
    /// <item>A <c>.pkX</c> file carries only the STORED region - the party stat block lives in
    /// <c>SIZE_STORED..SIZE_PARTY</c> and is simply absent from the file (13 bytes on Gen3, 57 on
    /// Gen5, 9 on Gen9, measured in the harness). An imported entity therefore arrives with a
    /// blank/garbage stat block and nothing in the import path fills it in.</item>
    /// <item><see cref="SaveFile"/>'s own auto-gate does not save you: it only populates stats
    /// when none are present, so an entity that happens to carry stale ones keeps them. This
    /// project has already shipped a bug of exactly this shape once (PROGRESS.md, "Species + move
    /// editing" - an edited mon exported with the previous species' HP).</item>
    /// <item>Unlike the Showdown path - where <c>ApplySetDetails</c> ends with its own
    /// <c>ResetPartyStats()</c> - nothing recomputes stats for a raw <c>.pkX</c> import.</item>
    /// </list>
    /// Calling it unconditionally is safe: it is a pure recomputation from species/level/IVs/EVs/
    /// nature, and it also re-syncs <c>Stat_Level</c> to <c>CurrentLevel</c>.
    /// </remarks>
    public static void WriteIntoPartySlot(SaveFile sav, PKM pk, int index)
    {
        pk.ResetPartyStats();
        sav.SetPartySlotAtIndex(pk, index);
    }

    // ---------------------------------------------------------------------------------------
    // Import - Showdown text
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Parses <paramref name="text"/> as a Showdown set and applies it onto <paramref name="target"/>
    /// in place. Parse errors are reported, never swallowed - a set with unrecognised lines is
    /// still applied (matching PKHeX desktop) but the caller is told exactly what did not parse.
    /// </summary>
    /// <remarks>
    /// <see cref="CommonEdits.ApplySetDetails"/> already handles, internally, three of the four
    /// ordering/staleness bug classes this project has been bitten by - verified empirically in
    /// <c>verify/ShowdownEntityIO</c> rather than taken on faith:
    /// <list type="bullet">
    /// <item>species is assigned before <c>CurrentLevel</c> (CommonEdits.cs:160 vs :171), so the
    /// EXP-via-growth-rate conversion uses the new species' curve, not the old one;</item>
    /// <item>moves go through <c>SetMoves(..., true)</c> (:165), so PP and PP-Ups track the new
    /// moves instead of going stale;</item>
    /// <item>it ends with <c>ResetPartyStats()</c> (:269), so the party stat block is recomputed.</item>
    /// </list>
    /// The fourth is *not* handled and is fixed up here - see <see cref="SyncMintNature"/>.
    /// </remarks>
    public static ShowdownImportResult ImportShowdown(string text, PKM target)
    {
        if (string.IsNullOrWhiteSpace(text))
            return ShowdownImportResult.Fail("Paste a Showdown set first - the text box is empty.");

        ShowdownSet set;
        try
        {
            set = new ShowdownSet(text);
        }
        catch (Exception ex)
        {
            return ShowdownImportResult.Fail($"Could not parse the set: {ex.Message}");
        }

        var errors = DescribeParseErrors(set);

        if (set.Species == 0)
        {
            var detail = errors.Count == 0 ? string.Empty : $" ({string.Join("; ", errors)})";
            return ShowdownImportResult.Fail($"No recognisable species in that text{detail}.");
        }

        if (set.Species > target.MaxSpeciesID)
        {
            var name = SpeciesName.GetSpeciesName(set.Species, 2);
            return ShowdownImportResult.Fail(
                $"{name} (#{set.Species}) does not exist in this save's generation (max #{target.MaxSpeciesID}). " +
                "Importing it would clamp to a different species, so it was refused instead.");
        }

        try
        {
            target.ApplySetDetails(set);
        }
        catch (Exception ex)
        {
            return ShowdownImportResult.Fail($"Applying the set failed: {ex.Message}");
        }

        RepairMovePP(target);
        SyncMintNature(target);

        return ShowdownImportResult.Ok(set, errors);
    }

    /// <summary>
    /// Re-derives every move slot's PP and PP-Ups from the moves the entity ACTUALLY ended up
    /// with, after <see cref="CommonEdits.ApplySetDetails"/> has run.
    /// </summary>
    /// <remarks>
    /// Works around a genuine bug in the vendored PKHeX.Core, found by
    /// <c>verify/ShowdownEntityIO</c> and reproducible on a real Gen1 save. ApplySetDetails does:
    /// <code>
    ///   if (moves[0] != 0) pk.SetMoves(moves, true);      // CommonEdits.cs:164-165
    ///   if (Legal.IsPPUpAvailable(pk)) pk.SetMaximumPPUps(moves); // :166-167
    /// </code>
    /// <c>MoveApplicator.SetMoves</c> first zeroes any move above <c>pk.MaxMoveID</c> and then
    /// calls <c>FixMoves()</c>, which *compacts* the list so the surviving moves slide down into
    /// the gap (PKM.ReorderMoves - it correctly drags PP/PP-Ups along with them). The very next
    /// line then overwrites PP and PP-Ups from <c>set.Moves</c>, the **original, uncompacted,
    /// unclamped** array - so the PP values end up one slot out of step with the moves.
    ///
    /// Observed: importing a Garchomp-style set (Earthquake / Dragon Claw / Swords Dance /
    /// Substitute) onto a Gen1 save. Dragon Claw (#337) does not exist in Gen1, so the moves
    /// compact to [Earthquake, Swords Dance, Substitute, -] while PP stays
    /// [16, 0, 48, 16] - i.e. Swords Dance carrying Dragon Claw's 0 PP, Substitute carrying Swords
    /// Dance's 48, and the empty fourth slot carrying Substitute's 16. An unusable moveset.
    ///
    /// Only triggers when the set names at least one move the destination generation cannot
    /// represent, which is exactly the normal case for pasting a modern Showdown set into an older
    /// save - so it is worth fixing rather than documenting around.
    ///
    /// Fixed here rather than in <c>vendor/</c>: the vendored tree is shared with the upstream
    /// project and other agents, and this is a caller-side correction that needs no vendor patch.
    /// </remarks>
    public static void RepairMovePP(PKM pk)
    {
        Span<ushort> actual = stackalloc ushort[4];
        pk.GetMoves(actual);

        if (Legal.IsPPUpAvailable(pk))
            pk.SetMaximumPPUps(actual); // sets PP-Ups AND PP, both from the real move list
        else
            pk.SetMaximumPPCurrent(actual);
    }

    /// <summary>
    /// Gen8+ only: keep the displayed Nature and the Mint byte in agreement after an import.
    /// </summary>
    /// <remarks>
    /// <c>CommonEdits.SetNature</c> writes <c>StatAlignment</c> (the Mint) rather than
    /// <c>Nature</c> whenever <c>Format >= 8</c> (CommonEdits.cs:145-146), and the compensating
    /// <c>pk.Nature = pk.StatAlignment</c> at the end of <c>ApplySetDetails</c> (:252) is gated
    /// behind the static <c>CommonEdits.ShowdownSetBehaviorNature</c>, which **defaults to false**
    /// (:18 - an auto-property with no initialiser; desktop PKHeX opts in via a user setting).
    ///
    /// Left alone, importing "Adamant" onto a Gen9 mon therefore moves its *stats* (PKM.LoadStats
    /// reads StatAlignment) while the Nature field still reads the old value - precisely the
    /// "legal-but-confusing original-Nature/Mint mismatch" the detail-page Nature picker was
    /// already fixed for (PROGRESS.md, "Form + Nature + Ability editing"). Same fix, same reason.
    ///
    /// Done here rather than by flipping the global static: that static is process-wide shared
    /// state and this app has several concurrently-developed edit paths, so a local assignment is
    /// both race-free and visible at the point it matters. Safe after the fact - on Format ≥ 8 the
    /// stat calculation reads StatAlignment (which ApplySetDetails already set correctly), so
    /// assigning Nature afterwards does not invalidate the ResetPartyStats ApplySetDetails just did.
    /// </remarks>
    public static void SyncMintNature(PKM pk)
    {
        if (pk.Format >= 8)
            pk.Nature = pk.StatAlignment;
    }

    /// <summary>
    /// Turns <see cref="ShowdownSet.InvalidLines"/> into human-readable strings via PKHeX's own
    /// localized parse-error table (<c>Editing/BattleTemplate/Errors/</c>).
    /// </summary>
    public static List<string> DescribeParseErrors(ShowdownSet set)
    {
        var messages = new List<string>(set.InvalidLines.Count);
        if (set.InvalidLines.Count == 0)
            return messages;

        BattleTemplateParseErrorLocalization? localization = null;
        try
        {
            localization = BattleTemplateParseErrorLocalization.Get();
        }
        catch
        {
            // Localization table unavailable (trimmed resources on device) - fall back to the raw
            // enum name plus the offending text, which is still actionable.
            localization = null;
        }

        foreach (var error in set.InvalidLines)
        {
            string text;
            if (localization is null)
            {
                text = $"{error.Type}: {error.Value}";
            }
            else
            {
                try { text = error.Humanize(localization); }
                catch { text = $"{error.Type}: {error.Value}"; }
            }
            messages.Add(text);
        }
        return messages;
    }
}

/// <summary>A serialised single-entity file, ready to hand to a file-save dialog.</summary>
/// <param name="FileName">PKHeX-style name including the format extension.</param>
/// <param name="Data">Decrypted stored-size entity bytes.</param>
public readonly record struct EntityExport(string FileName, byte[] Data);

/// <summary>Outcome of reading a <c>.pkX</c> file into a save's own entity format.</summary>
public readonly record struct EntityImportResult(bool Success, PKM? Entity, bool Converted, string Message)
{
    public static EntityImportResult Ok(PKM entity, bool converted, string message) => new(true, entity, converted, message);
    public static EntityImportResult Fail(string message) => new(false, null, false, message);
}

/// <summary>Outcome of applying a Showdown set onto an existing entity.</summary>
/// <param name="Applied">
/// True if the set was written onto the target. Note this says nothing about legality - see
/// <see cref="EntityTransferService"/>.
/// </param>
/// <param name="ParseErrors">Lines PKHeX could not interpret. Non-empty does not imply failure.</param>
public readonly record struct ShowdownImportResult(bool Applied, ShowdownSet? Set, IReadOnlyList<string> ParseErrors, string Message)
{
    public static ShowdownImportResult Ok(ShowdownSet set, List<string> parseErrors)
    {
        var message = parseErrors.Count == 0
            ? "Set applied as-is."
            : $"Set applied, but {parseErrors.Count} line(s) were not understood and were ignored.";
        return new(true, set, parseErrors, message);
    }

    public static ShowdownImportResult Fail(string message) => new(false, null, [], message);
}
