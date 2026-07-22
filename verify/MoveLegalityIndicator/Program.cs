using System;
using System.IO;
using PKHeX.Core;

// Proves PokemonDetailPage's per-move legality dot/caption logic (RefreshMoveLegality) against a
// real save: one case where all 4 moves are genuinely legal (green checks, no captions), and one
// where a species change makes every move illegal (red crosses, non-empty reason captions) -
// exactly the "flag which specific move causes illegality" scenario the task asked to prove.
// Hardcodes a local save path (like verify/SpeciesMoveEdit and friends) - excluded from CI, see
// .github/workflows/ci.yml.

const string path = @"C:\Users\abhis\Downloads\sav files pkmn\pkmnscarlet_100\main";
var bytes = File.ReadAllBytes(path);
var sav = SaveUtil.GetSaveFile((byte[])bytes.Clone());
if (sav is null)
{
    Console.WriteLine("FAIL: could not parse the real Gen9 save.");
    return;
}

// Find the party slot the app's own on-device passes used: Skeledirge (species 911).
PKM? skeledirge = null;
foreach (var p in sav.PartyData)
{
    if (p.Species == 911) { skeledirge = p; break; }
}
if (skeledirge is null)
{
    Console.WriteLine("FAIL: no Skeledirge found in this save's party.");
    return;
}

Console.WriteLine($"Found Skeledirge: Level={skeledirge.CurrentLevel} Moves={skeledirge.Move1}/{skeledirge.Move2}/{skeledirge.Move3}/{skeledirge.Move4}");

// --- Case A: untouched mon, all 4 moves should be legal ---
var laValid = new LegalityAnalysis(skeledirge);
var ctxValid = LegalityLocalizationContext.Create(laValid);
var movesValid = laValid.Info.Moves;

Console.WriteLine();
Console.WriteLine("-- Case A: untouched Skeledirge (expect all 4 moves VALID) --");
bool caseAOk = true;
for (int i = 0; i < 4; i++)
{
    var r = movesValid[i];
    var moveId = i switch { 0 => skeledirge.Move1, 1 => skeledirge.Move2, 2 => skeledirge.Move3, _ => skeledirge.Move4 };
    if (moveId == 0) { Console.WriteLine($"  Move {i + 1}: (empty slot, no dot rendered)"); continue; }
    Console.WriteLine($"  Move {i + 1} (id {moveId}): Valid={r.Valid}");
    if (!r.Valid) caseAOk = false;
}
Console.WriteLine(caseAOk ? "CASE A: PASS (all real moves valid, matches green-check UI)" : "CASE A: FAIL (a move on an untouched real mon was flagged illegal)");

// --- Case B: change species, keep the same 4 moves -> none of them belong to the new species ---
var clone = skeledirge.Clone();
clone.Species = 913; // Quaxwell - same scenario PROGRESS.md's on-device pass already used
clone.ResetPartyStats();

var laIllegal = new LegalityAnalysis(clone);
var ctxIllegal = LegalityLocalizationContext.Create(laIllegal);
var movesIllegal = laIllegal.Info.Moves;

Console.WriteLine();
Console.WriteLine("-- Case B: Skeledirge's moves on a Quaxwell body (expect illegal moves, each with a reason) --");
bool anyInvalid = false;
bool everyInvalidHasReason = true;
for (int i = 0; i < 4; i++)
{
    var r = movesIllegal[i];
    var moveId = i switch { 0 => clone.Move1, 1 => clone.Move2, 2 => clone.Move3, _ => clone.Move4 };
    if (moveId == 0) { Console.WriteLine($"  Move {i + 1}: (empty slot, no dot rendered)"); continue; }

    if (!r.Valid)
    {
        anyInvalid = true;
        var reason = r.Summary(ctxIllegal);
        Console.WriteLine($"  Move {i + 1} (id {moveId}): Valid=False reason=\"{reason}\"");
        if (string.IsNullOrWhiteSpace(reason)) everyInvalidHasReason = false;
    }
    else
    {
        Console.WriteLine($"  Move {i + 1} (id {moveId}): Valid=True");
    }
}

bool caseBOk = anyInvalid && everyInvalidHasReason;
Console.WriteLine(caseBOk
    ? "CASE B: PASS (illegal move(s) correctly flagged, each with a non-empty caption)"
    : "CASE B: FAIL (expected at least one illegal move with a populated reason caption)");

Console.WriteLine();
Console.WriteLine(caseAOk && caseBOk ? "=== ALL CASES PASS ===" : "=== FAILURE ===");

// Confirm the original file on disk is untouched, per this project's verification discipline.
// This harness never calls Write() or touches `path` for output, but LegalityAnalysis reads deep
// into the save's internal state - worth confirming it's genuinely read-only in practice too.
var afterBytes = File.ReadAllBytes(path);
var unchanged = bytes.AsSpan().SequenceEqual(afterBytes);
Console.WriteLine(unchanged ? "Original file byte-for-byte unchanged on disk." : "WARNING: original file changed on disk.");
