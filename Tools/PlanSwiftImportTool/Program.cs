using OurPlanCore;

int exitCode = Run(args);
return exitCode;

static int Run(string[] args)
{
    if (args.Length == 0 || HasArg(args, "--help") || HasArg(args, "-h"))
    {
        PrintUsage();
        return args.Length == 0 ? 1 : 0;
    }

    string command = args[0].Trim().ToLowerInvariant();
    var options = ParseOptions(args.Skip(1).ToArray());
    try
    {
        return command switch
        {
            "scan" => RunScan(options),
            "import" => RunImport(options),
            "sync-takeoff-folder" => RunSyncTakeoffFolder(options),
            _ => UnknownCommand(command),
        };
    }
    catch (Exception ex) when (ex is ArgumentException or DirectoryNotFoundException or IOException or UnauthorizedAccessException or InvalidOperationException)
    {
        Console.Error.WriteLine(ex.Message);
        return 2;
    }
}

static int RunScan(IReadOnlyDictionary<string, string> options)
{
    string source = Required(options, "--source");
    PlanSwiftProjectManifest manifest = PlanSwiftProjectScanner.Scan(source);
    int sections = manifest.TakeoffItems.Sum(item => item.Sections.Count);
    int segmentSections = manifest.Segments.Sum(segment => segment.Sections.Count);

    Console.WriteLine($"Source: {manifest.SourceJobPath}");
    Console.WriteLine($"Format: {manifest.SourceFormat}");
    Console.WriteLine($"Pages: {manifest.Pages.Count}");
    Console.WriteLine($"Takeoff folders: {manifest.TakeoffFolders.Count}");
    Console.WriteLine($"Takeoff items: {manifest.TakeoffItems.Count}");
    Console.WriteLine($"Measured sections: {sections}");
    Console.WriteLine($"PlanSwift segments: {manifest.Segments.Count}");
    Console.WriteLine($"PlanSwift segments with geometry: {manifest.Segments.Count(segment => segment.Sections.Count > 0)}");
    Console.WriteLine($"Segment sections: {segmentSections}");
    Console.WriteLine($"Estimate/material rows: {manifest.EstimateItems.Count}");
    Console.WriteLine($"Notes: {manifest.Notes.Count}");
    Console.WriteLine($"Warnings: {manifest.Warnings.Count}");
    foreach (string warning in manifest.Warnings.Take(20))
        Console.WriteLine($"WARN {warning}");
    if (manifest.Warnings.Count > 20)
        Console.WriteLine($"WARN ... {manifest.Warnings.Count - 20} more");
    return 0;
}

static int RunImport(IReadOnlyDictionary<string, string> options)
{
    var importOptions = new PlanSwiftImportOptions
    {
        SourceJobPath = Required(options, "--source"),
        DestinationParentPath = Required(options, "--dest"),
        DestinationJobName = Value(options, "--name"),
        ConvertPageImages = !BoolOption(options, "--no-images"),
        MaxPages = IntValue(options, "--max-pages"),
        MaxTakeoffItems = IntValue(options, "--max-items"),
        MaxMeasurements = IntValue(options, "--max-measurements"),
    };

    PlanSwiftImportResult result = PlanSwiftProjectImporter.Import(importOptions);
    Console.WriteLine($"Imported job: {result.DestinationJobPath}");
    Console.WriteLine($"Pages: {result.PagesImported}");
    Console.WriteLine($"Takeoff folders: {result.TakeoffFoldersImported}");
    Console.WriteLine($"Takeoff items: {result.TakeoffItemsImported}");
    Console.WriteLine($"Measurements: {result.MeasurementsImported}");
    Console.WriteLine($"Warnings: {result.Warnings}");
    Console.WriteLine($"Report: {Path.Combine(result.DestinationJobPath, "import_reports", "planswift_import_report.md")}");
    foreach (string warning in result.Messages.Take(20))
        Console.WriteLine($"WARN {warning}");
    if (result.Messages.Count > 20)
        Console.WriteLine($"WARN ... {result.Messages.Count - 20} more");
    return 0;
}

static int RunSyncTakeoffFolder(IReadOnlyDictionary<string, string> options)
{
    TakeoffFolderSyncResult result = TakeoffFolderSync.Run(new TakeoffFolderSyncOptions
    {
        SourceJobPath = Required(options, "--source-job"),
        TargetJobPath = Required(options, "--target-job"),
        SourceTakeoffFolder = Required(options, "--source-folder"),
        TargetTakeoffFolder = Required(options, "--target-folder"),
        BackupPath = Required(options, "--backup"),
        Apply = BoolOption(options, "--apply"),
    });

    Console.WriteLine($"Mode: {(result.Applied ? "applied" : "dry-run")}");
    Console.WriteLine($"Items: {result.Items}");
    Console.WriteLine($"Target measurements: {result.TargetMeasurements}");
    Console.WriteLine($"Measurements: {result.Measurements}");
    Console.WriteLine($"Reused measurements: {result.ReusedMeasurements}");
    Console.WriteLine($"Added measurements: {result.AddedMeasurements}");
    Console.WriteLine("Removed measurements: 0");
    Console.WriteLine($"Holes: {result.Holes}");
    Console.WriteLine($"Hidden measurements: {result.HiddenMeasurements}");
    Console.WriteLine($"Pages: {result.Pages}");
    Console.WriteLine($"Pages with hidden-state changes: {result.HiddenPages}");
    Console.WriteLine($"Backup: {result.BackupPath}");
    return 0;
}

static IReadOnlyDictionary<string, string> ParseOptions(string[] args)
{
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < args.Length; i++)
    {
        string key = args[i];
        if (!key.StartsWith("-", StringComparison.Ordinal))
            continue;

        if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
        {
            values[key] = args[i + 1];
            i++;
        }
        else
        {
            values[key] = "";
        }
    }

    return values;
}

static bool HasArg(string[] args, string key) =>
    args.Any(arg => string.Equals(arg, key, StringComparison.OrdinalIgnoreCase));

static bool BoolOption(IReadOnlyDictionary<string, string> options, string key)
{
    if (!options.TryGetValue(key, out string? value))
        return false;
    if (string.IsNullOrWhiteSpace(value))
        return true;
    if (bool.TryParse(value, out bool parsed))
        return parsed;
    throw new ArgumentException($"{key} must be a boolean flag or true/false.");
}

static string Required(IReadOnlyDictionary<string, string> options, string key)
{
    string value = Value(options, key);
    if (string.IsNullOrWhiteSpace(value))
        throw new ArgumentException($"{key} is required.");
    return value;
}

static string Value(IReadOnlyDictionary<string, string> options, string key) =>
    options.TryGetValue(key, out string? value) ? value : "";

static int IntValue(IReadOnlyDictionary<string, string> options, string key) =>
    int.TryParse(Value(options, key), out int parsed) && parsed > 0 ? parsed : 0;

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("PlanSwiftImportTool");
    Console.WriteLine();
    Console.WriteLine("Scan:");
    Console.WriteLine("  PlanSwiftImportTool scan --source \"C:\\Path\\To\\PlanSwiftJob\"");
    Console.WriteLine();
    Console.WriteLine("Import:");
    Console.WriteLine("  PlanSwiftImportTool import --source \"C:\\Path\\To\\PlanSwiftJob\" --dest \"C:\\Path\\To\\OurPlanCoreJobs\" [--name \"Job Name\"]");
    Console.WriteLine();
    Console.WriteLine("Sync one staged takeoff folder into an existing job:");
    Console.WriteLine("  PlanSwiftImportTool sync-takeoff-folder --source-job \"C:\\StagedJob\" --target-job \"C:\\ActiveJob\" --source-folder \"siding\" --target-folder \"old\\siding\" --backup \"C:\\Backup\" [--apply]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --no-images            Create blank page PDFs instead of converting PlanSwift images");
    Console.WriteLine("  --max-pages N          Import only first N pages");
    Console.WriteLine("  --max-items N          Import only first N measured takeoff items");
    Console.WriteLine("  --max-measurements N   Import only first N measured sections");
    Console.WriteLine("  --apply                Apply a validated takeoff-folder sync; without it the command is dry-run only");
}
