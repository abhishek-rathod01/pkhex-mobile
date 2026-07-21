using PKHeX.Core;

namespace PkhexMobile;

/// <summary>
/// A slot location within a single loaded <see cref="SaveFile"/> - either a party index (0-5) or
/// a (box, slot) pair. Deliberately a single small type shared by both storage kinds rather than
/// two parallel index systems, so a move/swap operation can be expressed generically without the
/// caller (or this class) ever confusing a box index for a party index or vice versa - the
/// <see cref="IsParty"/> tag is checked at every branch below instead of relying on callers to
/// route correctly.
/// </summary>
public readonly record struct SlotLocation
{
    public bool IsParty { get; }
    public int Box { get; }
    public int Slot { get; }

    private SlotLocation(bool isParty, int box, int slot)
    {
        IsParty = isParty;
        Box = box;
        Slot = slot;
    }

    public static SlotLocation Party(int slot) => new(true, -1, slot);
    public static SlotLocation InBox(int box, int slot) => new(false, box, slot);
}

/// <summary>
/// Moves or swaps a Pokemon between two slots (party-to-party, party-to-box, box-to-box) within a
/// single loaded <see cref="SaveFile"/>. This is the data-integrity-sensitive core behind the
/// box/party grid UI (<see cref="BoxListPage"/>/<see cref="PartyListPage"/>) - kept independent of
/// any UI code and covered by <c>verify/BoxPartyMove/Program.cs</c> so the write logic can be
/// proven correct against real save files before any drag/tap UI touches it.
///
/// Guards deliberately built in (see PROGRESS.md "Box/party move + swap" for the full reasoning):
/// - Both endpoints are read into local <see cref="PKM"/> instances *before* any write, so a
///   validation failure never leaves a partial edit.
/// - Party's "no gaps below PartyCount" invariant is preserved: the only valid empty *party*
///   target is exactly index PartyCount (checked here, not just assumed from the UI), and a
///   move that vacates a party slot closes the gap via <see cref="SaveFile.DeletePartySlot"/>
///   rather than leaving a hole.
/// - A destination write always happens before the corresponding source is cleared, so a
///   (very unlikely, since these are synchronous in-memory byte writes) mid-operation failure
///   can at worst leave a duplicate, never a silent loss.
/// - A Pokemon entering a party slot from box storage gets <see cref="PKM.ResetPartyStats"/>
///   called explicitly and unconditionally - never left to <see cref="SaveFile"/>'s own
///   <c>!PartyStatsPresent</c> auto-gate, which the "Species + move editing" session already
///   found unreliable for this exact class of bug (see PROGRESS.md). This only fires for the
///   box-origin side of a move/swap; a party-to-party reorder never calls it, since that would
///   wrongly full-heal/clear status on a mon that already has valid live battle state.
/// - <see cref="EntityImportSettings.None"/> is used for every write, matching the precedent
///   already set by <see cref="SaveFile.DeletePartySlot"/>'s own internal shifting calls -
///   a same-save relocation should not re-trigger "as if traded" handler conditioning,
///   Pokedex updates, or record-acquired bookkeeping (the mon isn't newly caught or traded).
/// </summary>
public static class PokemonSlotMover
{
    /// <summary>
    /// Moves the Pokemon at <paramref name="from"/> to <paramref name="to"/>. If <paramref name="to"/>
    /// already holds a Pokemon, the two are swapped atomically (both slots update together).
    /// Same-slot (<paramref name="from"/> == <paramref name="to"/>) is a safe no-op.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The source slot is empty, or the operation would violate party's no-gaps invariant.
    /// Thrown before any write happens.
    /// </exception>
    public static void MoveOrSwap(SaveFile sav, SlotLocation from, SlotLocation to)
    {
        if (Same(from, to))
            return; // Same-slot drag/tap: no-op, must not corrupt anything.

        // Read both endpoints into locals before validating or writing anything, so a thrown
        // exception below never leaves a half-applied edit - nothing has been mutated yet.
        var sourcePk = Read(sav, from);
        var destPk = Read(sav, to);

        bool sourceEmpty = sourcePk.Species == 0;
        bool destEmpty = destPk.Species == 0;

        if (sourceEmpty)
            throw new ArgumentException("Source slot is empty; nothing to move.", nameof(from));

        ValidateLocation(sav, from);
        ValidateLocation(sav, to);

        if (destEmpty)
        {
            // Party can never have a gap below PartyCount - the only valid empty party target is
            // the slot immediately after the current party (i.e. "append"). Any other empty-looking
            // party index shouldn't be reachable from the UI, but re-validate here rather than
            // trusting the caller, since a wrong index here would corrupt PartyCount bookkeeping.
            if (to.IsParty && to.Slot != sav.PartyCount)
            {
                throw new ArgumentException(
                    $"Party slot {to.Slot} is not a valid empty target (PartyCount={sav.PartyCount}); " +
                    "only the slot immediately after the current party accepts a move-in.", nameof(to));
            }

            PrepareForDestination(sourcePk, from, to);

            // Destination write happens first: if anything below throws, the source Pokemon still
            // exists (in its original slot) rather than being silently lost. Worst case on a thrown
            // exception is a duplicate, never a loss.
            WriteSlot(sav, to, sourcePk);

            // Clear/close the source. Party clears via DeletePartySlot so subsequent party members
            // shift down and PartyCount shrinks by one - no hole left behind. Box slots can have
            // holes already (existing app convention - see BoxListPage.LoadBox), so a plain blank
            // write is correct there.
            if (from.IsParty)
                sav.DeletePartySlot(from.Slot);
            else
                WriteSlot(sav, from, sav.BlankPKM);
        }
        else
        {
            // Swap: both slots stay occupied throughout, so there is no party-count/gap risk on
            // either side, regardless of the party/box combination involved.
            PrepareForDestination(sourcePk, from, to);
            PrepareForDestination(destPk, to, from);

            WriteSlot(sav, to, sourcePk);
            WriteSlot(sav, from, destPk);
        }
    }

    private static bool Same(SlotLocation a, SlotLocation b) =>
        a.IsParty == b.IsParty && a.Box == b.Box && a.Slot == b.Slot;

    private static PKM Read(SaveFile sav, SlotLocation loc) =>
        loc.IsParty ? sav.GetPartySlotAtIndex(loc.Slot) : sav.GetBoxSlotAtIndex(loc.Box, loc.Slot);

    private static void WriteSlot(SaveFile sav, SlotLocation loc, PKM pk)
    {
        if (loc.IsParty)
            sav.SetPartySlotAtIndex(pk, loc.Slot, EntityImportSettings.None);
        else
            sav.SetBoxSlotAtIndex(pk, loc.Box, loc.Slot, EntityImportSettings.None);
    }

    private static void ValidateLocation(SaveFile sav, SlotLocation loc)
    {
        if (loc.IsParty)
        {
            if ((uint)loc.Slot > 5)
                throw new ArgumentOutOfRangeException(nameof(loc), "Party slot index must be 0-5.");
        }
        else
        {
            if ((uint)loc.Box >= (uint)sav.BoxCount)
                throw new ArgumentOutOfRangeException(nameof(loc), "Box index out of range.");
            if ((uint)loc.Slot >= (uint)sav.BoxSlotCount)
                throw new ArgumentOutOfRangeException(nameof(loc), "Box slot index out of range.");
        }
    }

    /// <summary>
    /// A Pokemon transitioning from box storage into a party slot needs its party stat block
    /// (HP/Atk/.../Stat_Level, current HP, status) freshly computed - box storage doesn't carry
    /// those bytes at all (<see cref="SaveFile.GetStoredSlot"/> only reads SIZE_STORED, smaller
    /// than SIZE_PARTY), so a box-sourced <see cref="PKM"/> object holds a stale/zeroed stat block
    /// in memory. Mirrors the explicit <see cref="PKM.ResetPartyStats"/> call already used in
    /// <see cref="PokemonDetailPage.OnSaveChangesClicked"/> for the same class of bug - see
    /// PROGRESS.md "Species + move editing", which found <see cref="SaveFile"/>'s own
    /// <c>!PartyStatsPresent</c> auto-gate unreliable for mons that already carry stats.
    ///
    /// Only fires box-&gt;party (<c>!origin.IsParty &amp;&amp; destination.IsParty</c>). A
    /// party-&gt;party reorder or box-&gt;box move never calls this - doing so unconditionally
    /// would wrongly full-heal and clear status on a mon that already has valid live battle state.
    /// </summary>
    private static void PrepareForDestination(PKM pk, SlotLocation origin, SlotLocation destination)
    {
        if (!origin.IsParty && destination.IsParty)
            pk.ResetPartyStats();
    }
}
