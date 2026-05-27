using Meshmakers.Octo.Frontend.CommandReferenceGenerator;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: CommandReferenceGenerator <fixturesDir> <outDir>");
    return 1;
}

var fixturesDir = args[0];
var outDir = args[1];

if (!Directory.Exists(fixturesDir))
{
    Console.Error.WriteLine($"Fixtures directory not found: {fixturesDir}");
    return 1;
}

Directory.CreateDirectory(outDir);

var files = Directory.GetFiles(fixturesDir, "*.cs", SearchOption.AllDirectories);
var sourcesByPath = files.ToDictionary(f => f, File.ReadAllText);

// Also collect constants from sibling .cs files in the parent directory
// (e.g. Constants.cs lives in ManagementTool/, commands live in ManagementTool/Commands/)
var parentDir = Directory.GetParent(fixturesDir)?.FullName;
var extraConstantFiles = parentDir != null
    ? Directory.GetFiles(parentDir, "*.cs", SearchOption.TopDirectoryOnly)
    : Array.Empty<string>();

// First pass: collect all constants from all sources (with exception safety)
var constants = new Dictionary<string, string>();
foreach (var extraPath in extraConstantFiles)
{
    try
    {
        foreach (var kv in RoslynExtractor.CollectConstants(File.ReadAllText(extraPath)))
            constants[kv.Key] = kv.Value;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[WARN] Failed to collect constants from {Path.GetFileName(extraPath)}: {ex.Message}");
    }
}

foreach (var (path, src) in sourcesByPath)
{
    try
    {
        foreach (var kv in RoslynExtractor.CollectConstants(src))
        {
            constants[kv.Key] = kv.Value;
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[WARN] Failed to collect constants from {Path.GetFileName(path)}: {ex.Message}");
    }
}

// Second pass: extract commands per file (with exception safety)
var writtenGroups = new HashSet<string>();
var writtenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var written = 0;
var errorCount = 0;
var zombieFiles = new List<string>();

foreach (var (path, src) in sourcesByPath)
{
    var fileName = Path.GetFileName(path);
    int commandsThisFile = 0;
    int constantsThisFile = 0;

    try
    {
        constantsThisFile = RoslynExtractor.CollectConstants(src).Count;
        var commands = RoslynExtractor.Extract(src, path, constants);
        foreach (var cmd in commands)
        {
            var slug = GroupSlugger.Slug(cmd.Group);
            var label = GroupSlugger.Label(cmd.Group);
            var groupDir = Path.Combine(outDir, slug);
            Directory.CreateDirectory(groupDir);

            // Emit _category_.json once per group, but only if it doesn't already exist —
            // so manual edits (e.g. sidebar `position`, custom label) survive regeneration.
            if (writtenGroups.Add(slug))
            {
                var categoryPath = Path.Combine(groupDir, "_category_.json");
                if (!File.Exists(categoryPath))
                {
                    var categoryJson = $"{{\n  \"label\": \"{label}\"\n}}\n";
                    File.WriteAllText(categoryPath, categoryJson);
                }
            }

            var md = MarkdownRenderer.Render(cmd);
            var primaryName = FilenameResolver.Resolve(cmd);
            var outPath = Path.Combine(groupDir, $"{primaryName}.md");
            if (!writtenPaths.Add(outPath))
            {
                var fallbackName = FilenameResolver.ResolveDisambiguated(cmd);
                var fallbackPath = Path.Combine(groupDir, $"{fallbackName}.md");
                Console.Error.WriteLine($"[WARN] Filename collision on '{primaryName}.md' in group '{label}'. " +
                                        $"Using disambiguated '{fallbackName}.md' instead.");
                outPath = fallbackPath;
                writtenPaths.Add(outPath);
            }
            File.WriteAllText(outPath, md);
            written++;
            commandsThisFile++;
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[ERROR] Failed to process {fileName}: {ex.GetType().Name}: {ex.Message}");
        errorCount++;
        continue;
    }

    if (commandsThisFile == 0 && constantsThisFile == 0)
    {
        zombieFiles.Add(fileName);
    }
}

Console.WriteLine($"Wrote {written} command(s) to {outDir}");

if (zombieFiles.Count > 0)
{
    Console.Error.WriteLine($"[WARN] {zombieFiles.Count} file(s) contributed neither commands nor constants:");
    foreach (var z in zombieFiles)
    {
        Console.Error.WriteLine($"  - {z}");
    }
    Console.Error.WriteLine("These may be empty, helper-only, abstract-only, or commented-out source files.");
}

return errorCount > 0 ? 1 : 0;
