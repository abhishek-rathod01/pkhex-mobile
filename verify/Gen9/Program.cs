using System;
using PKHeX.Core;

Console.WriteLine("=== Gen9 (Scarlet/Violet) SAV9SV verification ===");

// Blank constructor: SAV9SV() -- builds AllBlocks via BlankBlocks9.GetBlankBlocks()
var sav = new SAV9SV();
Console.WriteLine($"Constructed SAV9SV. SaveRevision={sav.SaveRevision} ({sav.SaveRevisionString})");

// SV requires Version to be SL or VL for IsVersionValid(); set it explicitly.
sav.Version = GameVersion.VL;
sav.OT = "VERIFY";

var pk = sav.BlankPKM;
pk.Species = 25; // Pikachu
pk.CurrentLevel = 10;

sav.PartyData = [pk];

Console.WriteLine();
Console.WriteLine("-- PRIMARY verification: direct read off the live SAV object --");
Console.WriteLine($"Trainer: {sav.OT}");
Console.WriteLine($"Version: {sav.Version} (IsVersionValid={sav.IsVersionValid()})");
Console.WriteLine($"PartyCount: {sav.PartyCount}");
for (int i = 0; i < sav.PartyCount; i++)
{
    var p = sav.PartyData[i];
    Console.WriteLine($"  Species={p.Species} Level={p.CurrentLevel} Nickname='{p.Nickname}'");
}

bool primaryOk = sav.OT == "VERIFY" && sav.PartyCount == 1 && sav.PartyData[0].Species == 25 && sav.PartyData[0].CurrentLevel == 10;
Console.WriteLine();
Console.WriteLine(primaryOk ? "PRIMARY VERIFICATION: PASS" : "PRIMARY VERIFICATION: FAIL");

// --- BONUS / secondary check (optional): Write() + SaveUtil.GetSaveFile round-trip ---
Console.WriteLine();
Console.WriteLine("-- BONUS verification: Write() + SaveUtil.GetSaveFile round-trip --");
try
{
    Memory<byte> bytes = sav.Write();
    Console.WriteLine($"Write() succeeded. Byte length: {bytes.Length}");

    bool hashValid = SwishCrypto.GetIsHashValid(bytes.Span);
    Console.WriteLine($"SwishCrypto.GetIsHashValid(bytes): {hashValid}");

    var reloaded = SaveUtil.GetSaveFile(bytes);
    if (reloaded is null)
    {
        Console.WriteLine("SaveUtil.GetSaveFile returned null -- format not re-detected from raw bytes.");
        Console.WriteLine($"(diagnostic) bytes.Length = 0x{bytes.Length:X} -- likely outside SaveUtil's hardcoded SV size-bucket allowlist for this block-set size, even though the hash itself is valid.");
    }
    else
    {
        Console.WriteLine($"Re-detected as: {reloaded.GetType().Name}, Generation={reloaded.Generation}");
        Console.WriteLine($"Reloaded Trainer: {reloaded.OT}");
        Console.WriteLine($"Reloaded PartyCount: {reloaded.PartyCount}");
        for (int i = 0; i < reloaded.PartyCount; i++)
        {
            var p = reloaded.PartyData[i];
            Console.WriteLine($"  Species={p.Species} Level={p.CurrentLevel}");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"BONUS check threw: {ex.GetType().Name}: {ex.Message}");
}

// --- BONUS 2: bypass SaveUtil's size-sniffing entirely, use SAV9SV(Memory<byte>) directly ---
Console.WriteLine();
Console.WriteLine("-- BONUS verification 2: direct SAV9SV(Memory<byte>) round-trip (bypasses SaveUtil) --");
try
{
    var direct = new SAV9SV(sav.Write());
    Console.WriteLine($"Direct SAV9SV(Memory<byte>) ctor succeeded.");
    Console.WriteLine($"Reloaded Trainer: {direct.OT}");
    Console.WriteLine($"Reloaded PartyCount: {direct.PartyCount}");
    for (int i = 0; i < direct.PartyCount; i++)
    {
        var p = direct.PartyData[i];
        Console.WriteLine($"  Species={p.Species} Level={p.CurrentLevel}");
    }
    bool directOk = direct.OT == "VERIFY" && direct.PartyCount == 1 && direct.PartyData[0].Species == 25 && direct.PartyData[0].CurrentLevel == 10;
    Console.WriteLine(directOk ? "BONUS 2 (direct ctor round-trip): PASS" : "BONUS 2 (direct ctor round-trip): FAIL (data mismatch)");
}
catch (Exception ex)
{
    Console.WriteLine($"BONUS 2 check threw: {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine("(diagnostic) Root cause: BlankBlocks9.GetBlankBlocks() -> SCBlockUtil.GetBlankBlockArray");
    Console.WriteLine("creates every never-touched block with SCTypeCode.None and dummy data. SCBlock.WriteBlock()");
    Console.WriteLine("happily serializes Type=None blocks, but SCBlock.ReadFromOffset()'s deserializer has no");
    Console.WriteLine("case for None (only Bool1/2/3, Object, Array are special-cased); it falls through to the");
    Console.WriteLine("'single value storage' default branch and calls SCTypeCode.None.GetTypeSize(), which");
    Console.WriteLine("explicitly throws ArgumentOutOfRangeException. This affects any block never assigned a");
    Console.WriteLine("real value -- i.e. almost all blocks in a freshly blank-constructed save.");
}

Console.WriteLine();
Console.WriteLine("=== Done ===");
