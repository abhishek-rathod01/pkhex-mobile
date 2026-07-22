using System;
using System.IO;
using PKHeX.Core;

// Proves PokemonDetailPage's new Ball/Friendship editor against real saves: replicates the app's
// exact write path (pk.Ball / pk.CurrentFriendship -> SetPartySlotAtIndex -> Write() ->
// SaveUtil.GetSaveFile(byte[])) and confirms the per-generation editable/no-op split documented in
// PROGRESS.md - Gen1 both no-op, Gen2 Ball no-op but Friendship real, Gen3+ both real. Hardcodes
// local save paths (like verify/SpeciesMoveEdit and friends) - excluded from CI.

bool allOk = true;

void Check(string label, bool ok)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {label}");
    if (!ok) allOk = false;
}

void RunCase(string genLabel, string path, int partySlot, bool expectBallEditable, bool expectFriendshipEditable)
{
    Console.WriteLine($"\n=== {genLabel}: {path} ===");
    var original = File.ReadAllBytes(path);
    var sav = SaveUtil.GetSaveFile((byte[])original.Clone());
    if (sav is null)
    {
        Check($"{genLabel} save parses", false);
        return;
    }

    var pk = sav.PartyData[partySlot];
    Console.WriteLine($"  Before: Ball={pk.Ball} Friendship={pk.CurrentFriendship} CurrentHandler={pk.CurrentHandler}");

    // Same values the app would apply: pick something clearly different from whatever is there,
    // wrapping into valid ranges so a real write is unambiguous versus a no-op.
    byte targetBall = (byte)(pk.Ball == 4 ? 1 : 4); // Poke Ball <-> Master Ball
    byte targetFriendship = (byte)(pk.CurrentFriendship == 200 ? 100 : 200);

    pk.Ball = targetBall;
    pk.CurrentFriendship = targetFriendship;

    bool ballTookEffect = pk.Ball == targetBall;
    bool friendshipTookEffect = pk.CurrentFriendship == targetFriendship;

    Console.WriteLine($"  After in-memory set: Ball={pk.Ball} (expected {(expectBallEditable ? targetBall : "no-op")}) " +
        $"Friendship={pk.CurrentFriendship} (expected {(expectFriendshipEditable ? targetFriendship : "no-op")}) CurrentHandler={pk.CurrentHandler}");
    if (pk is PK9 pk9a) Console.WriteLine($"  Raw fields: OT_Friendship={pk9a.OriginalTrainerFriendship} HT_Friendship={pk9a.HandlingTrainerFriendship}");

    Check($"{genLabel} Ball editable={expectBallEditable} matches actual took-effect={ballTookEffect}",
        ballTookEffect == expectBallEditable);
    Check($"{genLabel} Friendship editable={expectFriendshipEditable} matches actual took-effect={friendshipTookEffect}",
        friendshipTookEffect == expectFriendshipEditable);

    // Round-trip through the exact (now-fixed) app write path.
    int handlerBefore2 = pk.CurrentHandler;
    sav.SetPartySlotAtIndex(pk, partySlot, EntityImportSettings.None);
    var bytes = sav.Write().ToArray();
    var reloaded = SaveUtil.GetSaveFile(bytes);
    if (reloaded is null)
    {
        Check($"{genLabel} reload after Write()", false);
        return;
    }
    var reloadedPk = reloaded.PartyData[partySlot];
    Console.WriteLine($"  After Write()+reload: Ball={reloadedPk.Ball} Friendship={reloadedPk.CurrentFriendship} CurrentHandler={reloadedPk.CurrentHandler} (pk.CurrentFriendship now={pk.CurrentFriendship} pk.CurrentHandler now={pk.CurrentHandler})");
    if (reloadedPk is PK9 pk9b) Console.WriteLine($"  Raw fields after reload: OT_Friendship={pk9b.OriginalTrainerFriendship} HT_Friendship={pk9b.HandlingTrainerFriendship}");

    // For an editable field this asserts the NEW value stuck; for a no-op field it asserts the
    // ORIGINAL value survived untouched (targetBall/targetFriendship never applied in the first
    // place, so comparing against them here would be asserting the wrong thing entirely).
    byte expectedBallAfter = expectBallEditable ? targetBall : pk.Ball;
    byte expectedFriendshipAfter = expectFriendshipEditable ? targetFriendship : pk.CurrentFriendship;
    Check($"{genLabel} Ball round-trips through Write()+reload", reloadedPk.Ball == expectedBallAfter);
    Check($"{genLabel} Friendship round-trips through Write()+reload", reloadedPk.CurrentFriendship == expectedFriendshipAfter);
    Check($"{genLabel} CurrentHandler unchanged by the write (no silent 'as if traded' side effect)",
        reloadedPk.CurrentHandler == handlerBefore2);

    // Original file on disk untouched.
    var afterDisk = File.ReadAllBytes(path);
    Check($"{genLabel} original file on disk byte-for-byte unchanged", original.AsSpan().SequenceEqual(afterDisk));
}

// Isolated check: does SetPartySlotAtIndex(pk, index) - no explicit EntityImportSettings, the
// exact call PokemonDetailPage.OnSaveChangesClicked already made BEFORE this session's Ball/
// Friendship work - flip CurrentHandler / populate fake Handling Trainer data on its own, even for
// an edit that has nothing to do with Ball or Friendship? If so, this is a pre-existing bug this
// session's Friendship field merely exposed (the first field whose correctness depends on reading
// back through the CurrentHandler-routed getter), not something introduced by it.
{
    const string dir0 = @"C:\Users\abhis\Downloads\sav files pkmn";
    var path0 = Path.Combine(dir0, "pkmnscarlet_100", "main");
    var sav0 = SaveUtil.GetSaveFile((byte[])File.ReadAllBytes(path0).Clone())!;
    var pk0 = sav0.PartyData[0];
    Console.WriteLine($"\n=== Isolated check: nickname-only edit, Gen9 ===");
    Console.WriteLine($"  Before: CurrentHandler={pk0.CurrentHandler} Nickname={pk0.Nickname}");
    pk0.Nickname = "NICKONLY";
    pk0.IsNicknamed = true;
    int handlerBeforeWrite = pk0.CurrentHandler;
    sav0.SetPartySlotAtIndex(pk0, 0, EntityImportSettings.None); // the FIXED app call
    var bytes0 = sav0.Write().ToArray();
    var reloaded0 = SaveUtil.GetSaveFile(bytes0)!;
    var reloadedPk0 = reloaded0.PartyData[0];
    Console.WriteLine($"  After Write()+reload: CurrentHandler={reloadedPk0.CurrentHandler} Nickname={reloadedPk0.Nickname}");
    Check("Isolated nickname-only edit (with the fix) does NOT flip CurrentHandler", reloadedPk0.CurrentHandler == handlerBeforeWrite);
}

const string dir = @"C:\Users\abhis\Downloads\sav files pkmn";
RunCase("Gen1", Path.Combine(dir, "POKEMON RED-0.sav"), 0, expectBallEditable: false, expectFriendshipEditable: false);
RunCase("Gen2", Path.Combine(dir, "Pokemon - Crystal Version (UE) (V1.1) C!.SAV"), 0, expectBallEditable: false, expectFriendshipEditable: true);
RunCase("Gen3", Path.Combine(dir, "pokeemerald (2).sav"), 0, expectBallEditable: true, expectFriendshipEditable: true);
RunCase("Gen5", Path.Combine(dir, "Pokemon Black Version.sav"), 0, expectBallEditable: true, expectFriendshipEditable: true);
RunCase("Gen9", Path.Combine(dir, "pkmnscarlet_100", "main"), 0, expectBallEditable: true, expectFriendshipEditable: true);

Console.WriteLine();
Console.WriteLine(allOk ? "=== ALL CASES PASS ===" : "=== FAILURE ===");
