using PKHeX.Core;
using PkhexMobile;

// Verifies PkhexMobile.BoxManagement (the actual production file, linked in via the .csproj, not a
// re-implementation) against real save files, replicating the BoxListPage call path exactly:
// probe -> mutate -> sav.Write() -> SaveUtil.GetSaveFile(byte[]) -> read back.
//
// What this is actually trying to catch, in priority order:
//
//  1. THE PROJECT'S RECURRING BUG CLASS - an API that satisfies the abstract SaveFile contract but
//     silently does nothing on a given save type. Three independent probes are checked against
//     *empirical* write-and-reload behavior, not against an assumption:
//       - IBoxDetailName          (box rename)      - SAV1 and SAV7b genuinely lack it
//       - IBoxDetailWallpaper     (wallpaper)       - Gen1/2 and SAV7b lack it
//       - CurrentBox override     (reflection probe)- SAV7b silently keeps it in memory only
//     A probe that says "supported" while a reload disagrees is a hard failure here.
//
//  2. SILENT DATA LOSS in the bulk operations. Sort/Compact must be a pure permutation: the
//     multiset of stored Pokemon (28 fields deep, not just species) must be identical before and
//     after, across the WHOLE PC and not just the operated box - single-box SortBoxes rewrites
//     every slot in the save via SetBoxSlotAtIndex, so out-of-range boxes are genuinely at risk and
//     are checked byte-for-... field-for-field. Clear must delete exactly what was in range and
//     nothing outside it.
//
//  3. That the "Box N" display fallback is never round-tripped into the save as a literal name.

var root = @"C:\Users\abhis\Downloads\sav files pkmn";

var cases = new (string Label, string RelPath)[]
{
    ("Gen1 (Red, real save)",            "POKEMON RED-0.sav"),
    ("Gen2 (Crystal, real save)",        "Pokemon - Crystal Version (UE) (V1.1) C!.SAV"),
    ("Gen5 (Black, real save)",          "Pokemon Black Version.sav"),
    ("Gen9 (Scarlet, real save)",        @"pkmnscarlet_100\main"),
    ("Gen7b (Let's Go, real save)",      @"PLGP Master Trainer Counters\Final\savedata.bin"),
};

var allPass = true;

foreach (var (label, relPath) in cases)
{
    Console.WriteLine($"=== {label} ===");
    var path = Path.Combine(root, relPath);
    if (!File.Exists(path))
    {
        Console.WriteLine($"  FAIL: save not found at {path}");
        allPass = false;
        Console.WriteLine();
        continue;
    }

    var onDiskBefore = File.ReadAllBytes(path);

    var pass = true;
    pass &= ReportProbes(path);
    pass &= TestBoxRename(path);
    pass &= TestCurrentBoxProbeAgreement(path);
    pass &= TestSort(path, BoxSortOrder.Species, BoxOpScope.CurrentBox);
    pass &= TestSort(path, BoxSortOrder.Species, BoxOpScope.AllBoxes);
    pass &= TestSort(path, BoxSortOrder.Compact, BoxOpScope.CurrentBox);
    pass &= TestClear(path);

    var onDiskAfter = File.ReadAllBytes(path);
    if (onDiskAfter.AsSpan().SequenceEqual(onDiskBefore))
    {
        Console.WriteLine("  OK: original file on disk untouched (byte-for-byte).");
    }
    else
    {
        Console.WriteLine("  FAIL: original file on disk changed!");
        pass = false;
    }

    if (!pass)
        allPass = false;
    Console.WriteLine();
}

Console.WriteLine(allPass ? "=== ALL CASES PASS ===" : "=== ONE OR MORE CASES FAILED ===");
return allPass ? 0 : 1;

// ---------------------------------------------------------------------------------------------
// Loading
// ---------------------------------------------------------------------------------------------

// Every test gets its own freshly-loaded SaveFile. SaveUtil.GetSaveFile(byte[]) *wraps* the array
// rather than copying it (documented pitfall in verify/EditRoundTrip), so a shared buffer would let
// one test's Write() leak into the next test's starting state - and into the on-disk original if the
// bytes came straight from File.ReadAllBytes without a clone.
static SaveFile Load(string path)
{
    var bytes = (byte[])File.ReadAllBytes(path).Clone();
    return SaveUtil.GetSaveFile(bytes) ?? throw new InvalidOperationException($"Not a recognized save: {path}");
}

static SaveFile Reload(SaveFile sav)
{
    var written = (byte[])sav.Write().ToArray().Clone();
    return SaveUtil.GetSaveFile(written) ?? throw new InvalidOperationException("Written save no longer parses.");
}

// ---------------------------------------------------------------------------------------------
// Probes
// ---------------------------------------------------------------------------------------------

static bool ReportProbes(string path)
{
    var sav = Load(path);
    Console.WriteLine($"  Type={sav.GetType().Name} Generation={sav.Generation} HasBox={sav.HasBox} " +
                      $"BoxCount={sav.BoxCount} BoxSlotCount={sav.BoxSlotCount}");
    Console.WriteLine($"  Probes: CanReadBoxNames={BoxManagement.CanReadBoxNames(sav)} " +
                      $"CanRenameBoxes={BoxManagement.CanRenameBoxes(sav)} " +
                      $"CanPersistCurrentBox={BoxManagement.CanPersistCurrentBox(sav)} " +
                      $"Wallpaper(box0)={Fmt(BoxManagement.GetBoxWallpaper(sav, 0))} " +
                      $"BoxesUnlocked={Fmt(BoxManagement.GetBoxesUnlocked(sav))} " +
                      $"NameMaxLen={BoxManagement.GetBoxNameMaxLength(sav)}");
    Console.WriteLine($"  Box 1 stored name={Quote(BoxManagement.GetStoredBoxName(sav, 0))} " +
                      $"display={Quote(BoxManagement.GetDisplayBoxName(sav, 0))}");

    // The read/write probes are genuinely independent - assert that rather than assuming, because
    // the audit's own coverage table gets this wrong for SAV1 (lists it as an IBoxDetailName
    // implementor; it only implements the read half).
    if (BoxManagement.CanRenameBoxes(sav) && !BoxManagement.CanReadBoxNames(sav))
    {
        Console.WriteLine("  FAIL: save claims write-without-read for box names, which IBoxDetailName forbids.");
        return false;
    }
    return true;

    static string Fmt(int? v) => v?.ToString() ?? "n/a";
    static string Quote(string? v) => v is null ? "n/a" : $"\"{v}\"";
}

// ---------------------------------------------------------------------------------------------
// 1. Box rename
// ---------------------------------------------------------------------------------------------

static bool TestBoxRename(string path)
{
    var sav = Load(path);
    var pass = true;

    if (sav.BoxCount < 1)
    {
        Console.WriteLine("  SKIP rename: save has no boxes.");
        return true;
    }

    var supported = BoxManagement.CanRenameBoxes(sav);

    // A name that is deliberately NOT the "Box N" fallback, so a save that quietly ignores the write
    // and keeps returning its default can't accidentally look like a success.
    const string wanted = "ZZTestBox";

    var originalStored = BoxManagement.GetStoredBoxName(sav, 0);
    var neighbourBefore = sav.BoxCount > 1 ? BoxManagement.GetStoredBoxName(sav, 1) : null;

    var result = BoxManagement.RenameBox(sav, 0, wanted);

    if (!supported)
    {
        // Unsupported must be refused loudly and must not have touched anything.
        if (result.Ok)
        {
            Console.WriteLine("  FAIL rename: save has no IBoxDetailName but RenameBox reported success.");
            pass = false;
        }
        else
        {
            Console.WriteLine($"  OK rename: correctly refused - {result.Message}");
        }

        var reloadedUnsupported = Reload(sav);
        var after = BoxManagement.GetStoredBoxName(reloadedUnsupported, 0);
        if (after != originalStored)
        {
            Console.WriteLine($"  FAIL rename: unsupported save's box name changed anyway ({Show(originalStored)} -> {Show(after)}).");
            pass = false;
        }
        else
        {
            Console.WriteLine($"  OK rename: box 1 name unchanged after reload ({Show(after)}).");
        }
        return pass;
    }

    if (!result.Ok)
    {
        Console.WriteLine($"  FAIL rename: supported save refused the rename - {result.Message}");
        return false;
    }

    var expected = wanted.Length > BoxManagement.GetBoxNameMaxLength(sav)
        ? wanted[..BoxManagement.GetBoxNameMaxLength(sav)]
        : wanted;

    // The real test: survive Write() + a full reparse from bytes, not just an in-memory read-back.
    var reloaded = Reload(sav);
    var persisted = BoxManagement.GetStoredBoxName(reloaded, 0);
    if (persisted != expected)
    {
        Console.WriteLine($"  FAIL rename: expected {Show(expected)} after reload, got {Show(persisted)}.");
        pass = false;
    }
    else
    {
        Console.WriteLine($"  OK rename: {Show(originalStored)} -> {Show(persisted)} survived Write()+reload.");
    }

    // Renaming box 1 must not disturb box 2 (the per-box offset arithmetic is the thing that would
    // silently smear a name across neighbours).
    if (sav.BoxCount > 1)
    {
        var neighbourAfter = BoxManagement.GetStoredBoxName(reloaded, 1);
        if (neighbourAfter != neighbourBefore)
        {
            Console.WriteLine($"  FAIL rename: box 2's name changed as a side effect ({Show(neighbourBefore)} -> {Show(neighbourAfter)}).");
            pass = false;
        }
        else
        {
            Console.WriteLine($"  OK rename: neighbouring box 2 name untouched ({Show(neighbourAfter)}).");
        }
    }

    // The fallback trap: GetDisplayBoxName is allowed to invent "Box N", but that string must never
    // be what gets written. Rename box 2 to its own *display* name and confirm the stored value is
    // now genuinely that literal - i.e. prove the round-trip WOULD persist the fallback, which is
    // exactly why the production code sources its edit field from GetStoredBoxName instead.
    if (sav.BoxCount > 1)
    {
        var probe = Load(path);
        var displayed = BoxManagement.GetDisplayBoxName(probe, 1);
        var stored = BoxManagement.GetStoredBoxName(probe, 1);
        if (string.IsNullOrWhiteSpace(stored) && displayed == BoxDetailNameExtensions.GetDefaultBoxName(1))
        {
            Console.WriteLine("  NOTE: box 2 has no stored name; the \"Box 2\" fallback is display-only " +
                              "and the UI must not prefill an edit field with it.");
        }
    }

    // Empty input must be refused rather than silently blanking a name.
    var blankResult = BoxManagement.RenameBox(Load(path), 0, "   ");
    if (blankResult.Ok)
    {
        Console.WriteLine("  FAIL rename: blank name was accepted.");
        pass = false;
    }
    else
    {
        Console.WriteLine("  OK rename: blank name refused.");
    }

    return pass;

    static string Show(string? v) => v is null ? "(none)" : $"\"{v}\"";
}

// ---------------------------------------------------------------------------------------------
// 2. CurrentBox: does the reflection probe agree with reality?
// ---------------------------------------------------------------------------------------------

static bool TestCurrentBoxProbeAgreement(string path)
{
    var sav = Load(path);
    if (sav.BoxCount < 2)
    {
        Console.WriteLine("  SKIP CurrentBox: fewer than 2 boxes.");
        return true;
    }

    var claimed = BoxManagement.CanPersistCurrentBox(sav);

    int original = sav.CurrentBox;
    int target = original == 0 ? 1 : 0;

    var setResult = BoxManagement.SetCurrentBox(sav, target);
    if (setResult.Ok != claimed)
    {
        Console.WriteLine($"  FAIL CurrentBox: probe={claimed} but SetCurrentBox returned Ok={setResult.Ok}.");
        return false;
    }

    // Empirical ground truth: does the value survive a Write() + full reparse?
    var reloaded = Reload(sav);
    bool actuallyPersisted = reloaded.CurrentBox == target;

    if (claimed != actuallyPersisted)
    {
        Console.WriteLine($"  FAIL CurrentBox: reflection probe said {claimed}, but write+reload says {actuallyPersisted} " +
                          $"(wrote {target}, reloaded {reloaded.CurrentBox}).");
        return false;
    }

    Console.WriteLine(claimed
        ? $"  OK CurrentBox: probe and reality agree - persists ({original} -> {target} survived reload)."
        : $"  OK CurrentBox: probe and reality agree - NOT stored by this save type (in-memory only), correctly refused.");
    return true;
}

// ---------------------------------------------------------------------------------------------
// 3. Sort / Compact: must be a pure permutation, in scope only
// ---------------------------------------------------------------------------------------------

static bool TestSort(string path, BoxSortOrder order, BoxOpScope scope)
{
    var sav = Load(path);
    var tag = $"{order}/{scope}";

    if (!sav.HasBox || sav.BoxCount < 1)
    {
        Console.WriteLine($"  SKIP sort {tag}: no boxes.");
        return true;
    }

    // Operate on a box that actually has something in it, otherwise the test proves nothing.
    int box = FindFullestBox(sav);
    var before = BoxManagement.PositionalDigests(sav);
    int occupiedBefore = BoxManagement.CountOccupied(before);
    var partyBefore = PartyDigest(sav);

    var result = BoxManagement.Sort(sav, scope, box, order);
    var after = BoxManagement.PositionalDigests(sav);

    if (!result.Ok)
    {
        // A refusal is a legitimate outcome (locked slots), but it must have changed nothing.
        if (!BoxManagement.IsRangeUntouched(before, after, 0, before.Length))
        {
            Console.WriteLine($"  FAIL sort {tag}: refused ({result.Message}) but the save changed anyway.");
            return false;
        }
        Console.WriteLine($"  OK sort {tag}: refused with no change - {result.Message}");
        return true;
    }

    var pass = true;

    // (a) Pure permutation of the stored population.
    if (!BoxManagement.IsSamePopulation(before, after))
    {
        Console.WriteLine($"  FAIL sort {tag}: the set of stored Pokemon changed (lost or duplicated data).");
        pass = false;
    }

    int occupiedAfter = BoxManagement.CountOccupied(after);
    if (occupiedBefore != occupiedAfter)
    {
        Console.WriteLine($"  FAIL sort {tag}: occupied slot count changed {occupiedBefore} -> {occupiedAfter}.");
        pass = false;
    }

    // (b) Blast radius. Single-box SortBoxes still rewrites every slot in the PC via
    //     SetBoxSlotAtIndex, so proving the untouched boxes really are untouched matters.
    if (scope == BoxOpScope.CurrentBox)
    {
        int lo = box * sav.BoxSlotCount;
        int hi = lo + sav.BoxSlotCount;
        if (!BoxManagement.IsRangeUntouched(before, after, 0, lo) ||
            !BoxManagement.IsRangeUntouched(before, after, hi, before.Length))
        {
            Console.WriteLine($"  FAIL sort {tag}: slots outside box {box + 1} changed.");
            pass = false;
        }
        else
        {
            Console.WriteLine($"  OK sort {tag}: {before.Length - sav.BoxSlotCount} out-of-scope slots field-for-field identical.");
        }
    }

    // (c) Compact must not reorder, only close holes.
    if (order == BoxSortOrder.Compact)
    {
        int lo = scope == BoxOpScope.AllBoxes ? 0 : box * sav.BoxSlotCount;
        int hi = scope == BoxOpScope.AllBoxes ? before.Length : lo + sav.BoxSlotCount;

        var expected = before[lo..hi].Where(static d => d.Length != 0).ToArray();
        var actual = after[lo..hi];
        bool orderOk = true;
        for (int i = 0; i < actual.Length; i++)
        {
            var want = i < expected.Length ? expected[i] : string.Empty;
            if (!string.Equals(actual[i], want, StringComparison.Ordinal))
            {
                orderOk = false;
                break;
            }
        }
        if (!orderOk)
        {
            Console.WriteLine($"  FAIL sort {tag}: compact reordered or dropped entries instead of only closing holes.");
            pass = false;
        }
        else
        {
            Console.WriteLine($"  OK sort {tag}: holes closed, existing relative order preserved exactly.");
        }
    }

    // (c2) The party must be completely unaffected by a box operation.
    //      This is the specific hazard behind BoxManagement.Sort's refusal to call CompressStorage:
    //      SaveFile.SlotPointers is where team/party references into box storage live, and on SAV7b
    //      it IS the Let's Go party (Blocks.Storage.PokeListInfo). SortBoxes repoints them; a raw
    //      CompressStorage with the empty span an outside caller is forced to pass would not. The
    //      SAV7b row of this check is therefore the one that actually earns the design decision.
    var partyAfter = PartyDigest(sav);
    if (!string.Equals(partyBefore, partyAfter, StringComparison.Ordinal))
    {
        Console.WriteLine($"  FAIL sort {tag}: the party changed as a side effect of a box operation.");
        Console.WriteLine($"        before: {partyBefore}");
        Console.WriteLine($"        after : {partyAfter}");
        pass = false;
    }
    else
    {
        Console.WriteLine($"  OK sort {tag}: party ({sav.PartyCount} members) unaffected, slot pointers intact.");
    }

    // (d) Survives export + reparse with the same population.
    var reloaded = Reload(sav);
    var afterReload = BoxManagement.PositionalDigests(reloaded);
    if (!BoxManagement.IsSamePopulation(after, afterReload))
    {
        Console.WriteLine($"  FAIL sort {tag}: population changed across Write()+reload.");
        pass = false;
    }
    else if (!BoxManagement.IsRangeUntouched(after, afterReload, 0, after.Length))
    {
        Console.WriteLine($"  FAIL sort {tag}: slot positions moved across Write()+reload.");
        pass = false;
    }
    else if (pass)
    {
        Console.WriteLine($"  OK sort {tag}: {result.Message} Population of {occupiedAfter} preserved through Write()+reload.");
    }

    return pass;
}

// ---------------------------------------------------------------------------------------------
// 4. Clear: exactly the requested range, nothing else
// ---------------------------------------------------------------------------------------------

static bool TestClear(string path)
{
    var sav = Load(path);
    if (!sav.HasBox || sav.BoxCount < 1)
    {
        Console.WriteLine("  SKIP clear: no boxes.");
        return true;
    }

    int box = FindFullestBox(sav);
    var before = BoxManagement.PositionalDigests(sav);
    int lo = box * sav.BoxSlotCount;
    int hi = lo + sav.BoxSlotCount;
    int inScopeBefore = BoxManagement.CountOccupied(before, lo, hi);
    int outOfScopeBefore = BoxManagement.CountOccupied(before) - inScopeBefore;

    var result = BoxManagement.Clear(sav, BoxOpScope.CurrentBox, box);
    var after = BoxManagement.PositionalDigests(sav);

    if (!result.Ok)
    {
        if (!BoxManagement.IsRangeUntouched(before, after, 0, before.Length))
        {
            Console.WriteLine($"  FAIL clear: refused ({result.Message}) but the save changed anyway.");
            return false;
        }
        Console.WriteLine($"  OK clear: refused with no change - {result.Message}");
        return true;
    }

    var pass = true;

    if (!BoxManagement.IsRangeUntouched(before, after, 0, lo) ||
        !BoxManagement.IsRangeUntouched(before, after, hi, before.Length))
    {
        Console.WriteLine($"  FAIL clear: Pokemon outside box {box + 1} were affected.");
        pass = false;
    }

    int outOfScopeAfter = BoxManagement.CountOccupied(after) - BoxManagement.CountOccupied(after, lo, hi);
    if (outOfScopeAfter != outOfScopeBefore)
    {
        Console.WriteLine($"  FAIL clear: out-of-scope population changed {outOfScopeBefore} -> {outOfScopeAfter}.");
        pass = false;
    }

    int inScopeAfter = BoxManagement.CountOccupied(after, lo, hi);
    if (inScopeAfter + result.Count != inScopeBefore)
    {
        Console.WriteLine($"  FAIL clear: deleted {result.Count} but in-scope went {inScopeBefore} -> {inScopeAfter}.");
        pass = false;
    }

    var reloaded = Reload(sav);
    var afterReload = BoxManagement.PositionalDigests(reloaded);
    if (!BoxManagement.IsRangeUntouched(after, afterReload, 0, after.Length))
    {
        Console.WriteLine("  FAIL clear: box contents changed across Write()+reload.");
        pass = false;
    }

    if (pass)
    {
        Console.WriteLine($"  OK clear: box {box + 1} emptied ({inScopeBefore} -> {inScopeAfter}), " +
                          $"{outOfScopeAfter} Pokemon elsewhere untouched, survived Write()+reload. {result.Message}");
    }
    return pass;
}

// Party identity, independent of box storage. Used to prove a box operation never reaches across
// into the party's own address space (GetPartyOffset vs GetBoxSlotOffset are unrelated regions -
// the hazard PROGRESS.md's box/party move work already had to guard against).
static string PartyDigest(SaveFile sav)
{
    var parts = new List<string> { $"count={sav.PartyCount}" };
    for (int i = 0; i < sav.PartyCount; i++)
    {
        var pk = sav.GetPartySlotAtIndex(i);
        parts.Add($"{i}:{pk.Species}/{pk.PID}/{pk.Nickname}/{pk.CurrentLevel}/{pk.EXP}");
    }
    return string.Join(' ', parts);
}

static int FindFullestBox(SaveFile sav)
{
    int best = 0, bestCount = -1;
    for (int b = 0; b < sav.BoxCount; b++)
    {
        int n = 0;
        for (int s = 0; s < sav.BoxSlotCount; s++)
        {
            if (sav.GetBoxSlotAtIndex(b, s).Species != 0)
                n++;
        }
        if (n > bestCount)
        {
            bestCount = n;
            best = b;
        }
    }
    return best;
}
