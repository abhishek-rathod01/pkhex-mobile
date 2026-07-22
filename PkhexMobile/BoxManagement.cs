using System.Reflection;
using PKHeX.Core;

namespace PkhexMobile;

/// <summary>Which boxes a bulk operation touches. Never inferred - always passed explicitly, so a
/// caller can't accidentally hit the whole PC when it meant one box.</summary>
public enum BoxOpScope
{
    CurrentBox,
    AllBoxes,
}

/// <summary>Ordering applied by <see cref="BoxManagement.Sort"/>.</summary>
public enum BoxSortOrder
{
    /// <summary>PKHeX.Core's default box sort (species, then form/gender/nickname).</summary>
    Species,

    /// <summary>Ascending level, species as tiebreak.</summary>
    Level,

    /// <summary>No reordering at all - only pulls the holes out, preserving existing relative order.
    /// See <see cref="BoxManagement.Sort"/> for why this is done via SortBoxes, not CompressStorage.</summary>
    Compact,
}

/// <param name="Ok">False means nothing was changed (refused up front) or the change was rolled back.</param>
/// <param name="Count">Meaning depends on the operation: slots changed (sort/compact), Pokemon deleted (clear).</param>
public readonly record struct BoxOpResult(bool Ok, int Count, string Message);

/// <summary>
/// Box-level save operations (rename, sort, compact, clear) plus the capability probes that must
/// gate them, kept out of <c>BoxListPage</c> so the harness can link this exact file rather than a
/// re-implementation - the same arrangement <c>PokemonSlotMover.cs</c> already uses.
///
/// Every probe here exists because of this project's recurring bug class: an API that satisfies the
/// abstract <see cref="SaveFile"/> contract but silently does nothing on a given save type. The
/// three separate probes below are genuinely independent - <c>SAV1</c> can report its current box
/// but cannot be renamed, <c>SAV7b</c> can do neither, and Gen1/2 have no wallpapers at all - so
/// they must not be collapsed into one "supports box stuff" flag.
/// </summary>
public static class BoxManagement
{
    // ===================== Box names =====================

    /// <summary>True if the save can report a stored box name. <c>SAV7b</c> (Let's Go) cannot -
    /// neither it nor its <c>SAV_BEEF</c> base implements <see cref="IBoxDetailNameRead"/>.</summary>
    public static bool CanReadBoxNames(SaveFile sav) => sav is IBoxDetailNameRead;

    /// <summary>True if box names can actually be written back.
    /// <para><b>Not the same set as <see cref="CanReadBoxNames"/>.</b> <c>SAV1</c> (Gen1 RBY)
    /// implements only the read half: its <c>GetBoxName</c> hands back
    /// <c>GetDefaultBoxName</c>/<c>GetDefaultBoxNameJapanese</c> unconditionally because RBY has no
    /// box-name storage at all. The audit's §3.5 coverage list is wrong on this point - it lists
    /// SAV1 among the implementors - so this probe is load-bearing for the project's own primary
    /// Gen1 test save, not just for the SAV7b case the audit does call out.</para></summary>
    public static bool CanRenameBoxes(SaveFile sav) => sav is IBoxDetailName;

    /// <summary>Conservative UI bound for a box name.
    /// <para>PKHeX.Core exposes no box-name-specific length, so this uses
    /// <see cref="SaveFile.MaxStringLengthTrainer"/> per the audit. It is a floor, not the true
    /// capacity - Gen5 stores 13 characters but reports 7 here - and every <c>SetBoxName</c>
    /// implementation truncates internally anyway, so a short bound can never corrupt, only
    /// under-offer.</para></summary>
    public static int GetBoxNameMaxLength(SaveFile sav) => Math.Max(sav.MaxStringLengthTrainer, 1);

    /// <summary>The name the save actually holds, or null when it holds none.
    /// <para><b>Deliberately never substitutes <c>GetDefaultBoxName</c>.</b> That fallback is a
    /// display-only convenience; round-tripping it through <see cref="RenameBox"/> would persist a
    /// literal "Box 3" into the save as a real, user-chosen name.</para></summary>
    public static string? GetStoredBoxName(SaveFile sav, int box)
        => sav is IBoxDetailNameRead r ? r.GetBoxName(box) : null;

    /// <summary>Display name, with the "Box N" fallback applied. For labels and the box picker only -
    /// never feed this to <see cref="RenameBox"/>.</summary>
    public static string GetDisplayBoxName(SaveFile sav, int box)
    {
        var stored = GetStoredBoxName(sav, box);
        return string.IsNullOrWhiteSpace(stored) ? BoxDetailNameExtensions.GetDefaultBoxName(box) : stored;
    }

    public static BoxOpResult RenameBox(SaveFile sav, int box, string name)
    {
        if (sav is not IBoxDetailName writer)
            return new(false, 0, "This save type has no box-name storage, so it can't be renamed.");
        if ((uint)box >= sav.BoxCount)
            return new(false, 0, "That box doesn't exist in this save.");

        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            return new(false, 0, "Enter a box name first.");

        // The library truncates to each format's real capacity itself; clamping here only keeps the
        // confirmation message honest about what will actually land.
        var max = GetBoxNameMaxLength(sav);
        if (trimmed.Length > max)
            trimmed = trimmed[..max];

        writer.SetBoxName(box, trimmed);

        // Read straight back out of the save rather than trusting the write - the whole point of
        // the interface probe is that a setter can be a no-op, and a same-session read-back is the
        // cheapest possible confirmation that this one wasn't.
        var readBack = writer.GetBoxName(box);
        if (!string.Equals(readBack, trimmed, StringComparison.Ordinal))
            return new(false, 0, $"Rename didn't stick - the save reports \"{readBack}\". Nothing else was changed.");

        return new(true, 1, $"Box renamed to \"{readBack}\".");
    }

    // ===================== Wallpaper / current box / unlocked count =====================

    /// <summary>Current wallpaper index, or null when the save has no wallpaper storage.
    /// <para>Gen1/2 (<c>SAV1</c>/<c>SAV2</c>), <c>SAV7b</c>, <c>SAV3Colosseum</c>, <c>SAV3XD</c> and
    /// <c>SAV4BR</c> do not implement <see cref="IBoxDetailWallpaper"/>.</para>
    /// <para><b>Read-only on purpose.</b> <c>SetBoxWallpaper</c> takes a bare int and PKHeX.Core
    /// exposes no per-format wallpaper count or maximum anywhere (verified: no <c>WallpaperMax</c>,
    /// <c>WallpaperCount</c> or equivalent exists in the library), so there is no generic way to
    /// bound the value. Writing an unbounded index into a save is its own silent-corruption risk,
    /// so this ships as a displayed value with an inline reason instead of an editor.</para></summary>
    public static int? GetBoxWallpaper(SaveFile sav, int box)
        => sav is IBoxDetailWallpaper w && (uint)box < sav.BoxCount ? w.GetBoxWallpaper(box) : null;

    /// <summary>Number of boxes unlocked in-game, or null when the save doesn't track it.
    /// <para>Self-describing probe, no reflection needed: <c>SaveFile.BoxesUnlocked</c> is
    /// <c>virtual { get =&gt; -1; set { } }</c>, so a negative read *is* the library telling you the
    /// setter would be a no-op. Overridden from Gen5 onward only.</para></summary>
    public static int? GetBoxesUnlocked(SaveFile sav) => sav.BoxesUnlocked >= 0 ? sav.BoxesUnlocked : null;

    /// <summary>True if writing <see cref="SaveFile.CurrentBox"/> actually reaches the save file.
    /// <para>This one has no self-describing sentinel to test. The base is
    /// <c>public virtual int CurrentBox { get; set; }</c> - a plain auto-property - so on a save
    /// that doesn't override it (<c>SAV7b</c>, <c>SAV3Colosseum</c>, <c>SAV3XD</c>, <c>SAV4BR</c>)
    /// the value assigns cleanly, reads back cleanly, and is simply never serialized. That is
    /// strictly worse than a no-op: it looks like it worked. Asking the runtime which type actually
    /// declares the getter is the only generic way to tell.</para>
    /// <para>Fails closed: if property metadata is unavailable (trimming) this reports
    /// unsupported, so the UI can under-offer but never claim a capability it can't prove. The
    /// verdict is cross-checked against a real write/reload round-trip for every test save in
    /// <c>verify/BoxManagement</c>.</para></summary>
    public static bool CanPersistCurrentBox(SaveFile sav)
    {
        try
        {
            var getter = sav.GetType()
                .GetProperty(nameof(SaveFile.CurrentBox), BindingFlags.Public | BindingFlags.Instance)
                ?.GetGetMethod();
            return getter is not null && getter.DeclaringType != typeof(SaveFile);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static BoxOpResult SetCurrentBox(SaveFile sav, int box)
    {
        if (!CanPersistCurrentBox(sav))
            return new(false, 0, "This save type doesn't store a current box.");
        if ((uint)box >= sav.BoxCount)
            return new(false, 0, "That box doesn't exist in this save.");

        sav.CurrentBox = box;
        if (sav.CurrentBox != box)
            return new(false, 0, "The save refused that current-box value. Nothing was changed.");

        return new(true, 1, $"Box {box + 1} is now the box the game opens on.");
    }

    // ===================== Bulk operations =====================

    /// <summary>
    /// Reorders box contents.
    /// <para><b>Why <see cref="BoxSortOrder.Compact"/> goes through <c>SortBoxes</c> and not
    /// <c>SaveFile.CompressStorage</c>:</b> <c>CompressStorage(out int, Span&lt;int&gt;)</c> makes
    /// the *caller* supply the slot pointers to re-point, but <c>SaveFile.SlotPointers</c> is
    /// <c>protected</c> - an outside caller can only pass an empty span. Those pointers are not
    /// decorative: they are battle-team slots on Gen7/BDSP, and on <c>SAV7b</c> they are
    /// <c>Blocks.Storage.PokeListInfo</c>, i.e. the Let's Go party itself. Compacting with an empty
    /// span leaves every one of them aimed at whatever slid into that index - a silent scramble, and
    /// exactly this project's recurring bug class wearing a different hat. <c>SortBoxes</c> is a
    /// member, reads <c>SlotPointers</c> itself, and calls <c>SlotPointerUtil.UpdateRepointFrom</c>;
    /// a stable <c>OrderBy(Species == 0)</c> through it produces identical compaction with the
    /// pointer bookkeeping done correctly.</para>
    /// </summary>
    public static BoxOpResult Sort(SaveFile sav, BoxOpScope scope, int currentBox, BoxSortOrder order)
    {
        if (!TryGetRange(sav, scope, currentBox, out int start, out int stop, out var rangeError))
            return new(false, 0, rangeError);
        if (sav.IsAnySlotLockedInBox(start, stop))
            return new(false, 0, "Some slots in range are locked by the game (battle team or similar) and can't be reordered.");

        var before = PositionalDigests(sav);
        var snapshot = sav.GetPCBinary();

        _ = order switch
        {
            // Default PKHeX box sort.
            BoxSortOrder.Species => sav.SortBoxes(start, stop),
            BoxSortOrder.Level => sav.SortBoxes(start, stop, static (list, _) => list.OrderByLevel()),
            // LINQ's OrderBy is a stable sort, so occupied slots keep their existing relative order
            // and only the holes migrate to the end - "compact", with no reordering surprise.
            BoxSortOrder.Compact => sav.SortBoxes(start, stop, static (list, _) => list.OrderBy(p => p.Species == 0)),
            _ => 0,
        };

        var after = PositionalDigests(sav);

        // A reorder must be a pure permutation of the occupied slots. Anything else - a lost mon, a
        // duplicated one, a mutated field - is a contract violation, so verify it on every real run
        // rather than only in the harness, and undo it if it ever fires.
        if (!IsSamePopulation(before, after))
        {
            var restored = sav.SetPCBinary(snapshot);
            return new(false, 0, restored
                ? "Sort changed the set of Pokemon stored, so it was undone. Nothing was modified."
                : "Sort changed the set of Pokemon stored AND could not be undone - do NOT export this save.");
        }

        // SortBoxes' own return value counts slots written (empties included), not Pokemon moved -
        // count the positions that actually differ instead so the message means what it says.
        int changed = CountDifferences(before, after);
        int stored = CountOccupied(after);
        return new(true, changed, changed == 0
            ? "Already in order - nothing moved."
            : $"{changed} slot(s) reordered. {stored} Pokemon still stored.");
    }

    /// <summary>
    /// Deletes every Pokemon in range. Destructive and irreversible in-session - the caller is
    /// responsible for confirming with the user first.
    /// </summary>
    public static BoxOpResult Clear(SaveFile sav, BoxOpScope scope, int currentBox)
    {
        if (!TryGetRange(sav, scope, currentBox, out int start, out int stop, out var rangeError))
            return new(false, 0, rangeError);
        if (sav.IsAnySlotLockedInBox(start, stop))
            return new(false, 0, "Some slots in range are locked by the game (battle team or similar) and can't be cleared.");

        var before = PositionalDigests(sav);
        var snapshot = sav.GetPCBinary();
        int inScopeBefore = CountOccupied(before, start * sav.BoxSlotCount, (stop + 1) * sav.BoxSlotCount);

        int deleted = sav.ClearBoxes(start, stop);

        var after = PositionalDigests(sav);

        // The blast radius must be exactly the requested range. Out-of-range slots changing at all
        // would mean the range arithmetic is wrong, which is the one failure here that silently
        // destroys data the user never selected.
        if (!IsRangeUntouched(before, after, 0, start * sav.BoxSlotCount)
            || !IsRangeUntouched(before, after, (stop + 1) * sav.BoxSlotCount, before.Length))
        {
            var restored = sav.SetPCBinary(snapshot);
            return new(false, 0, restored
                ? "Clear reached outside the selected boxes, so it was undone. Nothing was modified."
                : "Clear reached outside the selected boxes AND could not be undone - do NOT export this save.");
        }

        int inScopeAfter = CountOccupied(after, start * sav.BoxSlotCount, (stop + 1) * sav.BoxSlotCount);
        int skipped = inScopeAfter; // whatever survived was overwrite-protected

        if (deleted == 0)
            return new(false, 0, "Nothing to clear - those boxes are already empty.");

        var msg = $"Deleted {deleted} Pokemon.";
        if (skipped > 0)
            msg += $" {skipped} were protected by the game and left alone.";
        if (inScopeBefore != deleted + skipped)
            msg += $" (Expected {inScopeBefore} in range.)";
        return new(true, deleted, msg);
    }

    // ===================== Integrity helpers =====================

    /// <summary>
    /// One digest per box slot across the whole PC (not just the operated range), empty slots as
    /// <see cref="string.Empty"/>. Built from field values rather than raw bytes so it is
    /// format-agnostic - Gen1's list-based box format and Gen9's encrypted blocks both reduce to the
    /// same comparable shape.
    /// </summary>
    public static string[] PositionalDigests(SaveFile sav)
    {
        var data = sav.BoxData;
        var result = new string[data.Count];
        for (int i = 0; i < data.Count; i++)
            result[i] = Digest(data[i]);
        return result;
    }

    private static string Digest(PKM pk)
    {
        if (pk.Species == 0)
            return string.Empty;
        return string.Join('|',
            pk.Species, pk.Form, pk.PID, pk.EncryptionConstant, pk.Nickname, pk.OriginalTrainerName,
            pk.TID16, pk.SID16, pk.CurrentLevel, pk.EXP, pk.HeldItem, pk.IsEgg,
            pk.Move1, pk.Move2, pk.Move3, pk.Move4,
            pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPA, pk.IV_SPD, pk.IV_SPE,
            pk.EV_HP, pk.EV_ATK, pk.EV_DEF, pk.EV_SPA, pk.EV_SPD, pk.EV_SPE);
    }

    /// <summary>True if both snapshots hold the same multiset of Pokemon, regardless of position.</summary>
    public static bool IsSamePopulation(string[] before, string[] after)
    {
        var a = before.Where(static x => x.Length != 0).ToArray();
        var b = after.Where(static x => x.Length != 0).ToArray();
        if (a.Length != b.Length)
            return false;
        Array.Sort(a, StringComparer.Ordinal);
        Array.Sort(b, StringComparer.Ordinal);
        return a.AsSpan().SequenceEqual(b);
    }

    public static bool IsRangeUntouched(string[] before, string[] after, int start, int end)
    {
        if (before.Length != after.Length)
            return false;
        end = Math.Min(end, before.Length);
        for (int i = Math.Max(start, 0); i < end; i++)
        {
            if (!string.Equals(before[i], after[i], StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    public static int CountDifferences(string[] before, string[] after)
    {
        if (before.Length != after.Length)
            return Math.Max(before.Length, after.Length);
        int n = 0;
        for (int i = 0; i < before.Length; i++)
        {
            if (!string.Equals(before[i], after[i], StringComparison.Ordinal))
                n++;
        }
        return n;
    }

    /// <summary>Occupied slots in a box range, counted straight off the save.
    /// <para>Deliberately not <c>CountOccupied(PositionalDigests(sav))</c>: the confirmation prompt
    /// for a destructive op needs an exact number and nothing else, and building 28-field digests
    /// for a 960-slot Gen9 PC to get it would be wasteful on a phone.</para></summary>
    public static int CountStored(SaveFile sav, int boxStart, int boxStop)
    {
        int n = 0;
        boxStart = Math.Max(boxStart, 0);
        boxStop = Math.Min(boxStop, sav.BoxCount - 1);
        for (int b = boxStart; b <= boxStop; b++)
        {
            for (int s = 0; s < sav.BoxSlotCount; s++)
            {
                if (sav.GetBoxSlotAtIndex(b, s).Species != 0)
                    n++;
            }
        }
        return n;
    }

    public static int CountOccupied(string[] digests) => CountOccupied(digests, 0, digests.Length);

    public static int CountOccupied(string[] digests, int start, int end)
    {
        end = Math.Min(end, digests.Length);
        int n = 0;
        for (int i = Math.Max(start, 0); i < end; i++)
        {
            if (digests[i].Length != 0)
                n++;
        }
        return n;
    }

    private static bool TryGetRange(SaveFile sav, BoxOpScope scope, int currentBox, out int start, out int stop, out string error)
    {
        start = 0;
        stop = 0;
        error = string.Empty;

        if (sav.BoxCount <= 0)
        {
            error = "This save has no PC boxes.";
            return false;
        }

        if (scope == BoxOpScope.AllBoxes)
        {
            stop = sav.BoxCount - 1;
            return true;
        }

        if ((uint)currentBox >= sav.BoxCount)
        {
            error = "That box doesn't exist in this save.";
            return false;
        }

        start = stop = currentBox;
        return true;
    }
}
