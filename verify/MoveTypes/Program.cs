using PKHeX.Core;

// Establishes the empirical facts the detail screen's move type chips rest on, before
// any UI is built on top of them:
//   1. What MoveInfo.GetType returns for the empty move slot (ID 0) - the UI must NOT
//      show a bogus "Normal" chip for a cleared slot.
//   2. The shape of GameInfo.Strings.Types (count, index 0, the Gen2-4 "???" slot).
//   3. That passing pk.Context (not a hardcoded context) actually produces
//      generation-correct types for the moves that changed type between generations.
//   4. That every type byte a real save's moves can produce maps to a known chip token.
//
// Run: dotnet run --project verify/MoveTypes/MoveTypes.csproj

bool allPass = true;
static void Check(ref bool all, bool ok, string label, string detail)
{
    Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {label}: {detail}");
    if (!ok) all = false;
}

Console.WriteLine("=== 1. GameInfo.Strings.Types shape ===");
var types = GameInfo.Strings.Types;
Console.WriteLine($"  Count = {types.Count}");
for (int i = 0; i < types.Count; i++)
    Console.WriteLine($"    [{i,2}] = \"{types[i]}\"");
Check(ref allPass, types.Count >= 19, "Types has at least 19 entries", $"{types.Count}");
Check(ref allPass, types[0] == "Normal", "Types[0] is Normal", $"\"{types[0]}\"");

Console.WriteLine();
Console.WriteLine("=== 2. MoveInfo.GetType(0, ctx) - the empty move slot ===");
Console.WriteLine("  If this is 0 (Normal) everywhere, the UI MUST special-case move 0.");
foreach (var ctx in new[] { EntityContext.Gen1, EntityContext.Gen2, EntityContext.Gen5, EntityContext.Gen9 })
{
    byte t = MoveInfo.GetType(0, ctx);
    Console.WriteLine($"    {ctx,-6} -> type byte {t} (\"{types[t]}\")");
}
Check(ref allPass, MoveInfo.GetType(0, EntityContext.Gen9) == 0,
    "GetType(0) returns 0/Normal, i.e. indistinguishable from a real Normal move",
    "confirmed - UI must branch on move ID 0, not on the type byte");

Console.WriteLine();
Console.WriteLine("=== 3. Generation-correct typing (why pk.Context matters) ===");
// Each row: move, the type it should have in the early context, and in the modern one.
(ushort id, string name, EntityContext a, string expectA, EntityContext b, string expectB)[] cases =
[
    (16,  "Gust",        EntityContext.Gen1, "Normal", EntityContext.Gen2, "Flying"),
    (44,  "Bite",        EntityContext.Gen1, "Normal", EntityContext.Gen2, "Dark"),
    (2,   "Karate Chop", EntityContext.Gen1, "Normal", EntityContext.Gen2, "Fighting"),
    (28,  "Sand Attack", EntityContext.Gen1, "Normal", EntityContext.Gen2, "Ground"),
    (204, "Charm",       EntityContext.Gen5, "Normal", EntityContext.Gen9, "Fairy"),
    (186, "Sweet Kiss",  EntityContext.Gen5, "Normal", EntityContext.Gen9, "Fairy"),
    (236, "Moonlight",   EntityContext.Gen5, "Normal", EntityContext.Gen9, "Fairy"),
];
foreach (var (id, name, a, expectA, b, expectB) in cases)
{
    string gotA = types[MoveInfo.GetType(id, a)];
    string gotB = types[MoveInfo.GetType(id, b)];
    Check(ref allPass, gotA == expectA && gotB == expectB,
        $"{name} (#{id})", $"{a}={gotA} (want {expectA}), {b}={gotB} (want {expectB})");
}

Console.WriteLine();
Console.WriteLine("=== 4. Real saves: every party/box move's type byte maps to a chip ===");
var root = @"C:\Users\abhis\Downloads\sav files pkmn";
(string path, string label)[] saves =
[
    (Path.Combine(root, "POKEMON RED-0.sav"), "Gen1 (gen1_real.sav)"),
    (Path.Combine(root, "Pokemon Black Version.sav"), "Gen5"),
    (Path.Combine(root, @"pkmnscarlet_100\main"), "Gen9 (gen9_real.sav)"),
];
foreach (var (path, label) in saves)
{
    if (!File.Exists(path)) { Console.WriteLine($"  [SKIP] {label}: not found"); continue; }
    var sav = SaveUtil.GetSaveFile(path);
    if (sav is null) { Console.WriteLine($"  [SKIP] {label}: not parsed"); continue; }

    var seen = new SortedSet<byte>();
    int mons = 0;
    var moveNames = GameInfo.Strings.Move;
    foreach (var p in sav.PartyData)
    {
        if (p.Species == 0) continue;
        mons++;
        Console.WriteLine($"    {label} party: {GameInfo.Strings.Species[p.Species]} (Context={p.Context})");
        foreach (ushort mv in new[] { p.Move1, p.Move2, p.Move3, p.Move4 })
        {
            if (mv == 0) { Console.WriteLine($"      (empty slot)                -> no chip"); continue; }
            byte t = MoveInfo.GetType(mv, p.Context);
            seen.Add(t);
            Console.WriteLine($"      {moveNames[mv],-20} #{mv,-4} -> {t,2} \"{types[t]}\"");
        }
    }
    // Sweep the boxes too - a much wider move sample than the party alone.
    for (int b = 0; b < sav.BoxCount && sav.HasBox; b++)
    {
        for (int s = 0; s < sav.BoxSlotCount; s++)
        {
            var p = sav.GetBoxSlotAtIndex(b, s);
            if (p.Species == 0) continue;
            foreach (ushort mv in new[] { p.Move1, p.Move2, p.Move3, p.Move4 })
            {
                if (mv == 0) continue;
                seen.Add(MoveInfo.GetType(mv, p.Context));
            }
        }
    }
    string names = string.Join(", ", seen.Select(t => $"{t}:{types[t]}"));
    Console.WriteLine($"    {label}: {mons} party mon(s), distinct type bytes across party+boxes = [{names}]");
    // The chip lookup covers 0..17 plus the Gen2-4 "???" slot at 9 if present.
    bool allMapped = seen.All(t => t < types.Count);
    Check(ref allPass, allMapped, $"{label} all type bytes in range", $"max={(seen.Count > 0 ? seen.Max : 0)}");
}

Console.WriteLine();
Console.WriteLine("=== 5. Full type-byte range any move can produce, per context ===");
Console.WriteLine("  The chip palette ported from the design bundle covers 18 types (0..17).");
Console.WriteLine("  Types[18] is \"Stellar\" (Gen9 Terastal) and has NO design token - if a move");
Console.WriteLine("  can be type 18, the UI needs a documented fallback rather than a wrong colour.");
foreach (var ctx in Enum.GetValues<EntityContext>())
{
    if (ctx is EntityContext.None or EntityContext.MaxInvalid) continue;
    var table = MoveInfo.GetTypeTable(ctx);
    if (table.Length == 0) continue;
    var distinct = new SortedSet<byte>();
    foreach (byte t in table) distinct.Add(t);
    byte maxType = distinct.Max;
    Console.WriteLine($"    {ctx,-6} table len {table.Length,4}, distinct types {distinct.Count,2}, max byte {maxType} (\"{types[maxType]}\")");
    Check(ref allPass, maxType <= 17, $"{ctx} max move type within the 18-token palette", $"max={maxType}");
}

Console.WriteLine();
Console.WriteLine("=== 6. Chip palette alignment: PokemonDetailPage.TypeColorKeys vs Types[] ===");
Console.WriteLine("  Sections 3-5 only prove the type NAME is right; the chip's COLOUR comes from the");
Console.WriteLine("  app's own TypeColorKeys[type] array, and nothing above checks the two agree. If they");
Console.WriteLine("  ever diverged, a chip would read \"FIRE\" while painting Steel's colour and every");
Console.WriteLine("  check above would still pass. This pins PKHeX's type-byte ORDER to the exact 18");
Console.WriteLine("  English names the app indexes its Colors.xaml Type* tokens by. Kept as a literal");
Console.WriteLine("  copy because the app array lives in a MAUI/net10.0-android assembly this net10.0");
Console.WriteLine("  harness cannot reference - so this asserts the contract, not the copy.");
string[] appTypeColorKeys =
[
    "Normal", "Fighting", "Flying", "Poison", "Ground", "Rock", "Bug", "Ghost", "Steel",
    "Fire", "Water", "Grass", "Electric", "Psychic", "Ice", "Dragon", "Dark", "Fairy",
];
for (int i = 0; i < appTypeColorKeys.Length; i++)
{
    Check(ref allPass, types[i] == appTypeColorKeys[i],
        $"TypeColorKeys[{i}] matches Types[{i}]", $"app=\"{appTypeColorKeys[i]}\" vs PKHeX=\"{types[i]}\"");
}
// The app falls back to "Normal" for anything past the palette; confirm the only such byte is
// Stellar, which section 5 already proved no move can ever be.
Check(ref allPass, types.Count == appTypeColorKeys.Length + 1 && types[18] == "Stellar",
    "the single unpalletted type byte is Stellar (unreachable by any move, see section 5)",
    $"Types.Count={types.Count}, Types[18]=\"{types[types.Count - 1]}\"");

Console.WriteLine();
Console.WriteLine(allPass ? "ALL CHECKS PASSED" : "SOME CHECKS FAILED");
return allPass ? 0 : 1;
