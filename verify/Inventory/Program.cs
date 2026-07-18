using PKHeX.Core;

if (args.Length < 1)
{
    Console.WriteLine("Usage: Inventory <folder>");
    return 1;
}

var root = args[0];
if (!Directory.Exists(root))
{
    Console.WriteLine($"Folder not found: {root}");
    return 1;
}

var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
    .ToList();

Console.WriteLine($"Scanning {files.Count} files under {root}\n");

foreach (var file in files)
{
    var rel = Path.GetRelativePath(root, file);
    try
    {
        var sav = SaveUtil.GetSaveFile(file);
        if (sav is null)
        {
            Console.WriteLine($"NOT RECOGNIZED  | {rel}");
            continue;
        }

        Console.WriteLine($"{sav.GetType().Name,-14} | {rel}  (Version={sav.Version}, OT={sav.OT}, PartyCount={sav.PartyCount})");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR           | {rel}  ({ex.GetType().Name}: {ex.Message})");
    }
}

return 0;
