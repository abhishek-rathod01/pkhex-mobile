using PKHeX.Core;
using PkhexMobile;

// Verifies PkhexMobile.PokemonSlotMover (the actual production file, linked in via the .csproj,
// not a re-implementation) against real save files for Gen1/5/9, replicating the box/party grid
// UI's exact call path: PokemonSlotMover.MoveOrSwap(sav, from, to) -> sav.Write() ->
// SaveUtil.GetSaveFile(byte[]) to confirm the export round-trips and reparses cleanly.
//
// Covers every case called out in PROGRESS.md's "Box/party move + swap" task:
//   - box->party move (into the one valid empty append slot)
//   - party->box move (vacating a party slot, including a *middle* slot to prove the
//     no-gaps/shift-down behavior, not just a trailing one)
//   - box->box move
//   - swaps specifically (destination occupied): party<->box and box<->box
//   - same-slot no-op
//   - "last party member out" - deliberately allowed (not blocked - PKHeX.Core itself has no such
//     guard, and this project's own inventory already treats PartyCount==0 as a legitimate parsed
//     state, e.g. SAV3RSBox), verified by draining the party to 0 and confirming Write()+reload
//     still reparses successfully
//   - field-for-field preservation (not just "a mon exists at the destination"): species, PID,
//     nickname, OT name, TID16, all 4 moves, all 6 IVs, all 6 EVs
//   - empirical Stat_HPMax dump immediately after GetBoxSlotAtIndex, before any write - confirms
//     (or refutes) the working theory that a box-sourced PKM never carries live stat-block bytes

var root = @"C:\Users\abhis\Downloads\sav files pkmn";

var cases = new (string Label, string RelPath)[]
{
    ("Gen1 (Red, real save)", "POKEMON RED-0.sav"),
    ("Gen5 (Black, real save)", "Pokemon Black Version.sav"),
    ("Gen9 (Scarlet, real save)", @"pkmnscarlet_100\main"),
};

var allPass = true;

foreach (var (label, relPath) in cases)
{
    Console.WriteLine($"=== {label} ===");
    var path = Path.Combine(root, relPath);

    var originalBytes = File.ReadAllBytes(path);
    // Defensive copy: SaveUtil.GetSaveFile(byte[]) wraps the array as Memory<byte> without
    // cloning, so any Write() later mutates this same array in place (documented pitfall from
    // verify/EditRoundTrip/Program.cs). Snapshot separately for the "untouched on disk" check.
    var originalSnapshot = (byte[])originalBytes.Clone();

    var sav = SaveUtil.GetSaveFile(originalBytes);
    if (sav is null)
    {
        Console.WriteLine("  FAIL: original file not recognized as a save.");
        allPass = false;
        continue;
    }

    if (!RunCase(sav, label))
        allPass = false;

    var stillOriginal = File.ReadAllBytes(path).AsSpan().SequenceEqual(originalSnapshot);
    Console.WriteLine(stillOriginal
        ? "  Original file on disk: untouched (confirmed)."
        : "  WARNING: original file on disk changed unexpectedly!");
    Console.WriteLine();
}

// --- Locked-slot guard (CAPABILITY-GAPS.md Part 2 #8): PokemonSlotMover.MoveOrSwap must refuse to
// touch a box slot the game has reserved (battle team, etc.), matching BoxManagement's existing
// bulk-op guard. None of this project's real save inventory happens to have a locked slot in its
// as-downloaded state (confirmed by scanning all three), so this synthetically locks Team 0 via
// SAV9SV's own public TeamIndexes.SetIsTeamLocked - a real, supported save-editing API, not a
// hack - to genuinely exercise sav.IsBoxSlotOverwriteProtected returning true, rather than only
// asserting the guard code exists. Fresh SaveFile instance, entirely separate from the Gen9 case
// above, so this doesn't interact with its box-state mutations.
{
    Console.WriteLine("=== Gen9 (Scarlet, real save) - locked-slot guard ===");
    var path = Path.Combine(root, @"pkmnscarlet_100\main");
    var originalSnapshot = (byte[])File.ReadAllBytes(path).Clone();
    var sav = SaveUtil.GetSaveFile((byte[])originalSnapshot.Clone()) as SAV9SV;
    if (sav is null)
    {
        Console.WriteLine("  FAIL: could not load as SAV9SV.");
        allPass = false;
    }
    else
    {
        // Team 0's six TeamSlots entries default to NONE_SELECTED (-1) on a save with no battle
        // team configured (confirmed empirically - locking Team 0 alone with no slots assigned
        // produced zero locked box slots on the first run of this test). GetBoxSlotFlags only
        // resolves a linear box index to a team via TeamSlots.IndexOf(index), so a real team needs
        // real slot pointers, not just the lock flag. Point Team 0's first slot at box 0 slot 0 -
        // a real, valid linear index - to genuinely exercise the guard.
        sav.TeamIndexes.TeamSlots[0] = sav.BoxSlotCount * 0 + 0;
        sav.TeamIndexes.SetIsTeamLocked(0, true);

        int lockedBox = -1, lockedSlot = -1;
        for (int b = 0; b < sav.BoxCount && lockedBox < 0; b++)
        {
            for (int s = 0; s < sav.BoxSlotCount; s++)
            {
                if (sav.IsBoxSlotOverwriteProtected(b, s)) { lockedBox = b; lockedSlot = s; break; }
            }
        }

        if (lockedBox < 0)
        {
            Console.WriteLine("  FAIL: locking Team 0 did not produce any locked box slot - can't exercise the guard.");
            allPass = false;
        }
        else
        {
            Console.WriteLine($"  Team 0 locked -> box {lockedBox} slot {lockedSlot} now reports IsBoxSlotOverwriteProtected=true.");
            var (sourceBox, sourceSlot) = FindOccupiedSlotOtherThan(sav, lockedBox, lockedSlot);

            if (sourceBox < 0)
            {
                Console.WriteLine("  FAIL: no other occupied box slot found to use as a move source.");
                allPass = false;
            }
            else
            {
                var beforeSnapshot = sav.GetPCBinary();
                bool threw = false;
                try
                {
                    PokemonSlotMover.MoveOrSwap(sav, SlotLocation.InBox(sourceBox, sourceSlot), SlotLocation.InBox(lockedBox, lockedSlot));
                }
                catch (InvalidOperationException ex)
                {
                    threw = true;
                    Console.WriteLine($"  OK: move into the locked slot threw InvalidOperationException: {ex.Message}");
                }

                if (!threw)
                {
                    Console.WriteLine("  FAIL: move into the locked slot did NOT throw - guard is not working.");
                    allPass = false;
                }

                var afterSnapshot = sav.GetPCBinary();
                if (beforeSnapshot.AsSpan().SequenceEqual(afterSnapshot))
                    Console.WriteLine("  OK: box contents completely unchanged after the rejected move (validated before any write, as designed).");
                else
                {
                    Console.WriteLine("  FAIL: box contents changed even though the move was rejected!");
                    allPass = false;
                }
            }
        }
    }

    var stillOriginal = File.ReadAllBytes(path).AsSpan().SequenceEqual(originalSnapshot);
    Console.WriteLine(stillOriginal
        ? "  Original file on disk: untouched (confirmed)."
        : "  WARNING: original file on disk changed unexpectedly!");
}

Console.WriteLine(allPass ? "=== ALL CASES PASS ===" : "=== ONE OR MORE CASES FAILED ===");
return allPass ? 0 : 1;

static (int Box, int Slot) FindOccupiedSlotOtherThan(SaveFile sav, int exceptBox, int exceptSlot)
{
    for (int b = 0; b < sav.BoxCount; b++)
    {
        for (int s = 0; s < sav.BoxSlotCount; s++)
        {
            if (b == exceptBox && s == exceptSlot)
                continue;
            if (sav.GetBoxSlotAtIndex(b, s).Species != 0)
                return (b, s);
        }
    }
    return (-1, -1);
}

bool RunCase(SaveFile sav, string label)
{
    var pass = true;
    void Fail(string msg) { Console.WriteLine($"  FAIL: {msg}"); pass = false; }
    void Ok(string msg) => Console.WriteLine($"  OK: {msg}");

    int n0 = sav.PartyCount;
    Console.WriteLine($"  Initial PartyCount={n0}, BoxCount={sav.BoxCount}, BoxSlotCount={sav.BoxSlotCount}");

    // --- Empirical check: does a freshly-read box slot already carry a stale/nonzero stat block? ---
    // Working theory (PROGRESS.md): GetStoredSlot only copies SIZE_STORED bytes (smaller than
    // SIZE_PARTY), so box format shouldn't carry stat-block bytes at all - Stat_HPMax should read 0.
    int firstOccupiedBox = -1, firstOccupiedSlot = -1;
    for (int b = 0; b < sav.BoxCount && firstOccupiedBox < 0; b++)
    {
        for (int s = 0; s < sav.BoxSlotCount; s++)
        {
            var probe = sav.GetBoxSlotAtIndex(b, s);
            if (probe.Species != 0)
            {
                firstOccupiedBox = b;
                firstOccupiedSlot = s;
                var speciesName = probe.Species < GameInfo.Strings.Species.Count ? GameInfo.Strings.Species[probe.Species] : probe.Species.ToString();
                Console.WriteLine($"  Stat_HPMax dump: box {b} slot {s} ({speciesName}) " +
                                   $"Stat_HPMax={probe.Stat_HPMax} PartyStatsPresent={probe.PartyStatsPresent} (before any write)");
                if (probe.Stat_HPMax != 0)
                    Console.WriteLine("  NOTE: working theory refuted for this save/gen - Stat_HPMax is nonzero on a fresh box read. " +
                                       "PokemonSlotMover's unconditional explicit ResetPartyStats() on box->party already covers this either way.");
                break;
            }
        }
    }
    if (firstOccupiedBox < 0)
    {
        Fail("no occupied box slot found to test with - can't proceed.");
        return pass;
    }

    // --- Same-slot no-op: both party and box variants must not throw or change anything. ---
    if (n0 > 0)
    {
        var before = Snapshot(sav.GetPartySlotAtIndex(0));
        PokemonSlotMover.MoveOrSwap(sav, SlotLocation.Party(0), SlotLocation.Party(0));
        var after = Snapshot(sav.GetPartySlotAtIndex(0));
        if (FieldsEqual(before, after, "party same-slot no-op", Fail))
            Ok("party same-slot no-op left the slot unchanged.");
    }
    {
        var before = Snapshot(sav.GetBoxSlotAtIndex(firstOccupiedBox, firstOccupiedSlot));
        PokemonSlotMover.MoveOrSwap(sav, SlotLocation.InBox(firstOccupiedBox, firstOccupiedSlot), SlotLocation.InBox(firstOccupiedBox, firstOccupiedSlot));
        var after = Snapshot(sav.GetBoxSlotAtIndex(firstOccupiedBox, firstOccupiedSlot));
        if (FieldsEqual(before, after, "box same-slot no-op", Fail))
            Ok("box same-slot no-op left the slot unchanged.");
    }

    if (n0 == 0)
    {
        Console.WriteLine("  (PartyCount is already 0 - skipping party-involving move/swap cases for this save.)");
        return pass;
    }

    // --- Party -> box move, vacating a *middle* slot (proves shift-down, not just trailing removal). ---
    int midSlot = n0 / 2; // e.g. n0=6 -> slot 3
    var movedOutSnapshot = Snapshot(sav.GetPartySlotAtIndex(midSlot));
    var replacementSnapshot = midSlot + 1 < n0 ? Snapshot(sav.GetPartySlotAtIndex(midSlot + 1)) : null;

    var (emptyBox, emptyBoxSlot) = FindEmptySlot(sav, exceptBox: firstOccupiedBox, exceptSlot: firstOccupiedSlot);
    if (emptyBoxSlot < 0)
    {
        Fail("no empty box slot found to test party->box move.");
    }
    else
    {
        PokemonSlotMover.MoveOrSwap(sav, SlotLocation.Party(midSlot), SlotLocation.InBox(emptyBox, emptyBoxSlot));

        if (sav.PartyCount != n0 - 1)
            Fail($"party->box move: expected PartyCount={n0 - 1}, got {sav.PartyCount}.");
        else
            Ok($"party->box move: PartyCount correctly decremented to {sav.PartyCount}.");

        var landedInBox = Snapshot(sav.GetBoxSlotAtIndex(emptyBox, emptyBoxSlot));
        FieldsEqual(movedOutSnapshot, landedInBox, "party->box move (moved mon landed in box)", Fail);

        if (replacementSnapshot is not null)
        {
            var shiftedDown = Snapshot(sav.GetPartySlotAtIndex(midSlot));
            FieldsEqual(replacementSnapshot, shiftedDown, "party->box move (shift-down closed the gap)", Fail);
        }
    }

    // --- Box -> party move into the one valid empty append slot. ---
    // Uses emptyBox/emptyBoxSlot as the source - guaranteed occupied at this point because the
    // party->box move above just placed a mon there. Deliberately does NOT search the save for a
    // *third* pre-existing occupied box slot: real saves vary wildly in how full their boxes are
    // (Gen5's real save here has almost nothing stored outside the party), so a test that depends
    // on "find another occupied slot lying around" is save-content-dependent, not a property of
    // PokemonSlotMover. Self-seeding via a move the harness itself performed keeps this reliable
    // for any save regardless of box occupancy.
    int appendIndex = sav.PartyCount; // now n0-1, since the move above freed a slot
    {
        var boxSnapshot = Snapshot(sav.GetBoxSlotAtIndex(emptyBox, emptyBoxSlot));

        PokemonSlotMover.MoveOrSwap(sav, SlotLocation.InBox(emptyBox, emptyBoxSlot), SlotLocation.Party(appendIndex));

        if (sav.PartyCount != appendIndex + 1)
            Fail($"box->party move: expected PartyCount={appendIndex + 1}, got {sav.PartyCount}.");
        else
            Ok($"box->party move: PartyCount correctly incremented to {sav.PartyCount}.");

        var landedInParty = sav.GetPartySlotAtIndex(appendIndex);
        FieldsEqual(boxSnapshot, Snapshot(landedInParty), "box->party move (core fields preserved)", Fail);

        // The bug this project already found once (party stat block staleness) - confirm the
        // newly-partied mon has a freshly computed, non-stale stat block.
        if (landedInParty.Stat_HPMax <= 0)
            Fail($"box->party move: Stat_HPMax is {landedInParty.Stat_HPMax} after entering the party - stats were not reset.");
        else if (landedInParty.Stat_Level != landedInParty.CurrentLevel)
            Fail($"box->party move: Stat_Level={landedInParty.Stat_Level} != CurrentLevel={landedInParty.CurrentLevel}.");
        else if (landedInParty.Stat_HPCurrent != landedInParty.Stat_HPMax)
            Fail($"box->party move: Stat_HPCurrent={landedInParty.Stat_HPCurrent} != Stat_HPMax={landedInParty.Stat_HPMax} (expected a full heal on entering the party).");
        else
            Ok($"box->party move: stat block freshly computed (Stat_HPMax={landedInParty.Stat_HPMax}, Stat_Level={landedInParty.Stat_Level}, fully healed).");

        var boxSlotNowEmpty = sav.GetBoxSlotAtIndex(emptyBox, emptyBoxSlot);
        if (boxSlotNowEmpty.Species != 0)
            Fail("box->party move: source box slot still shows a non-empty species after the move.");
        else
            Ok("box->party move: source box slot correctly emptied.");
    }

    // --- Swap: party <-> box, destination occupied on both sides. ---
    {
        int partySwapIdx = 0;
        int boxSwapBox = firstOccupiedBox, boxSwapSlot = firstOccupiedSlot;
        var partyBefore = Snapshot(sav.GetPartySlotAtIndex(partySwapIdx));
        var boxBefore = Snapshot(sav.GetBoxSlotAtIndex(boxSwapBox, boxSwapSlot));
        int partyCountBefore = sav.PartyCount;

        PokemonSlotMover.MoveOrSwap(sav, SlotLocation.Party(partySwapIdx), SlotLocation.InBox(boxSwapBox, boxSwapSlot));

        if (sav.PartyCount != partyCountBefore)
            Fail($"party<->box swap: PartyCount changed from {partyCountBefore} to {sav.PartyCount} - a swap must never change party count.");
        else
            Ok("party<->box swap: PartyCount unchanged, as expected for a swap.");

        var partyAfter = Snapshot(sav.GetPartySlotAtIndex(partySwapIdx));
        var boxAfter = Snapshot(sav.GetBoxSlotAtIndex(boxSwapBox, boxSwapSlot));

        FieldsEqual(boxBefore, partyAfter, "party<->box swap (box mon landed in party slot)", Fail);
        FieldsEqual(partyBefore, boxAfter, "party<->box swap (party mon landed in box slot)", Fail);

        var partyPk = sav.GetPartySlotAtIndex(partySwapIdx);
        if (partyPk.Stat_HPMax <= 0)
            Fail($"party<->box swap: the box mon that entered the party has Stat_HPMax={partyPk.Stat_HPMax} (stats not reset).");
        else
            Ok($"party<->box swap: the box mon that entered the party has a fresh stat block (Stat_HPMax={partyPk.Stat_HPMax}).");
    }

    // --- Swap: box <-> box, destination occupied. ---
    // Self-seed a second occupied box slot via a fresh party->box move (same reasoning as the
    // box->party step above - don't depend on the real save having a spare occupied box slot
    // lying around; Gen5's real save here has almost nothing stored outside the party).
    {
        var (seedBox, seedSlot) = FindEmptySlot(sav, exceptBox: -1, exceptSlot: -1);
        if (sav.PartyCount < 1 || seedSlot < 0)
        {
            Fail("box<->box swap: no party member or empty box slot available to seed a second occupied slot.");
        }
        else
        {
            var seedSnapshot = Snapshot(sav.GetPartySlotAtIndex(0));
            PokemonSlotMover.MoveOrSwap(sav, SlotLocation.Party(0), SlotLocation.InBox(seedBox, seedSlot));

            var aBefore = Snapshot(sav.GetBoxSlotAtIndex(firstOccupiedBox, firstOccupiedSlot));
            var bBefore = Snapshot(sav.GetBoxSlotAtIndex(seedBox, seedSlot));
            FieldsEqual(seedSnapshot, bBefore, "box<->box swap setup (seed slot holds the moved party mon)", Fail);

            PokemonSlotMover.MoveOrSwap(sav, SlotLocation.InBox(firstOccupiedBox, firstOccupiedSlot), SlotLocation.InBox(seedBox, seedSlot));

            var aAfter = Snapshot(sav.GetBoxSlotAtIndex(firstOccupiedBox, firstOccupiedSlot));
            var bAfter = Snapshot(sav.GetBoxSlotAtIndex(seedBox, seedSlot));

            FieldsEqual(bBefore, aAfter, "box<->box swap (slot A now holds former slot B contents)", Fail);
            FieldsEqual(aBefore, bAfter, "box<->box swap (slot B now holds former slot A contents)", Fail);
            Ok("box<->box swap: both slots exchanged contents correctly.");
        }
    }

    // --- Last party member out: deliberately allowed, not blocked (see PROGRESS.md - PKHeX.Core
    // itself has no PartyCount>0 guard, and this project's own save inventory already treats
    // PartyCount==0 as a legitimate parsed state). Drain to 0 and confirm the save still Write()s
    // and reparses cleanly. A real save may simply not have enough *empty* box slots left to hold
    // every drained party member (Gen1's real save here is nearly full - 235/240 box slots
    // occupied even before this harness's own moves ate into the remaining few) - that is a
    // capacity fact about the specific save file, not a PokemonSlotMover defect, so it's reported
    // as a capacity note rather than a failure.
    int startingCount = sav.PartyCount;
    int drainSafety = 20;
    bool ranOutOfRoom = false;
    while (sav.PartyCount > 0 && drainSafety-- > 0)
    {
        int lastIdx = sav.PartyCount - 1;
        var (drainBox, drainSlot) = FindEmptySlot(sav, exceptBox: -1, exceptSlot: -1);
        if (drainSlot < 0)
        {
            ranOutOfRoom = true;
            break;
        }
        PokemonSlotMover.MoveOrSwap(sav, SlotLocation.Party(lastIdx), SlotLocation.InBox(drainBox, drainSlot));
    }

    if (ranOutOfRoom)
    {
        Console.WriteLine($"  NOTE (save capacity, not a mover defect): ran out of empty box slots after draining " +
                           $"{startingCount - sav.PartyCount}/{startingCount} party members (PartyCount={sav.PartyCount}); " +
                           "this save's boxes are nearly full. Verifying the partially-drained state still round-trips instead.");
        byte[] partialExport;
        try
        {
            partialExport = sav.Write().ToArray();
        }
        catch (Exception ex)
        {
            Fail($"last-party-member-out (partial, capacity-limited): sav.Write() threw {ex.GetType().Name}: {ex.Message}");
            return pass;
        }
        var partialReload = SaveUtil.GetSaveFile(partialExport);
        if (partialReload is null || partialReload.PartyCount != sav.PartyCount)
            Fail($"last-party-member-out (partial, capacity-limited): reload failed or PartyCount mismatch (expected {sav.PartyCount}).");
        else
            Ok($"last-party-member-out (partial, capacity-limited): Write()+reload round-tripped correctly at PartyCount={sav.PartyCount}.");
    }
    else if (sav.PartyCount != 0)
    {
        Fail($"last-party-member-out: PartyCount is {sav.PartyCount} after draining, expected 0.");
    }
    else
    {
        Ok("last-party-member-out: PartyCount successfully reached 0 (deliberately allowed).");

        byte[] exported;
        try
        {
            exported = sav.Write().ToArray();
        }
        catch (Exception ex)
        {
            Fail($"last-party-member-out: sav.Write() threw {ex.GetType().Name}: {ex.Message}");
            return pass;
        }

        var reloaded = SaveUtil.GetSaveFile(exported);
        if (reloaded is null)
            Fail("last-party-member-out: exported bytes with PartyCount=0 were not recognized by SaveUtil.GetSaveFile on reload.");
        else if (reloaded.PartyCount != 0)
            Fail($"last-party-member-out: reloaded PartyCount is {reloaded.PartyCount}, expected 0.");
        else
            Ok("last-party-member-out: Write() + SaveUtil.GetSaveFile reload succeeded with PartyCount=0.");
    }

    return pass;
}

static (int Box, int Slot) FindEmptySlot(SaveFile sav, int exceptBox, int exceptSlot)
{
    for (int b = 0; b < sav.BoxCount; b++)
    {
        for (int s = 0; s < sav.BoxSlotCount; s++)
        {
            if (b == exceptBox && s == exceptSlot)
                continue;
            var probe = sav.GetBoxSlotAtIndex(b, s);
            if (probe.Species == 0)
                return (b, s);
        }
    }
    return (-1, -1);
}

static object Snapshot(PKM pk) => new
{
    pk.Species,
    pk.PID,
    pk.Nickname,
    pk.OriginalTrainerName,
    pk.TID16,
    pk.CurrentLevel,
    pk.Move1, pk.Move2, pk.Move3, pk.Move4,
    pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPA, pk.IV_SPD, pk.IV_SPE,
    pk.EV_HP, pk.EV_ATK, pk.EV_DEF, pk.EV_SPA, pk.EV_SPD, pk.EV_SPE,
};

static bool FieldsEqual(object expected, object actual, string context, Action<string> fail)
{
    if (expected.Equals(actual))
        return true;
    fail($"{context}: field mismatch.\n    expected: {expected}\n    actual:   {actual}");
    return false;
}
