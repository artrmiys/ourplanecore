using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SmartTakeoffs;

public sealed record FolderTemplateResult(int Created, int Skipped, int Errors, IReadOnlyList<string> ErrorMessages)
{
    public static FolderTemplateResult Empty { get; } = new(0, 0, 0, []);

    public FolderTemplateResult Add(FolderTemplateResult other) =>
        new(
            Created + other.Created,
            Skipped + other.Skipped,
            Errors + other.Errors,
            ErrorMessages.Concat(other.ErrorMessages).ToList());
}

public sealed record CapsTakeoffFolderResult(
    int TopCreated,
    int TopSkipped,
    int SubCreated,
    int SubSkipped,
    int Errors,
    IReadOnlyList<string> GroupNames,
    IReadOnlyList<string> ErrorMessages);

public static partial class PlanSwiftFolderTemplateService
{
    private sealed record FolderNode(string Name, IReadOnlyList<FolderNode> Children)
    {
        public FolderNode(string name) : this(name, [])
        {
        }
    }

    private static readonly string[] PageFoldersCom =
    [
        "details arch",
        "details struct",
        "rcp",
        "sections",
        "units",
        "--------others",
        "1. walls + gables",
        "2. sqfts + balconies + porch",
        "3. roof - eve rakes",
        "4. framing + headers",
        "5. balconies porches etc.",
        "6. shear walls - holdowns - ties",
        "7. siding - trims",
        "8. int.trims",
        "9. drywalls",
    ];

    private static readonly string[] PageFoldersEwp =
    [
        "arch_plans",
        "sqfts",
        "details struct",
        "--------others",
        "framing",
    ];

    private static readonly FolderNode[] TakeoffTreeStandard =
    [
        new("units"),
        new("sqfts"),
        new("walls",
        [
            new("basement foor walls"),
            new("1st floor walls"),
            new("2nd floor walls"),
            new("3rd floor walls"),
            new("4th floor walls"),
            new("5th floor walls"),
            new("shaft walls"),
        ]),
        new("gables",
        [
            new("gable trusses"),
            new("gable stick"),
        ]),
        new("parapets"),
        new("trussheel"),
        new("openings"),
        new("eves rakes",
        [
            new("eves"),
            new("rakes"),
        ]),
        new("roof misc"),
        new("framing",
        [
            new("1st floor framing"),
            new("2nd floor framing"),
            new("3rd floor framing"),
            new("4th floor framing"),
            new("5th floor framing"),
            new("loft framing"),
            new("roof framing"),
        ]),
        new("trims"),
        new("siding"),
    ];

    private static readonly FolderNode[] TakeoffTreeEwp =
    [
        new("sqfts"),
        new("framing",
        [
            new("1st floor framing"),
            new("2nd floor framing"),
            new("3rd floor framing"),
            new("4th floor framing"),
            new("5th floor framing"),
            new("loft framing"),
            new("roof framing"),
        ]),
    ];

    public static string ResolveMode(SmartTakeoffsJob job, string requestedMode = "AUTO")
    {
        string mode = (requestedMode ?? "AUTO").Trim().ToUpperInvariant();
        if (mode is "COM" or "EWP")
            return mode;

        string name = Path.GetFileName(job.RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .ToUpperInvariant();
        bool hasCom = name.Contains("COM", StringComparison.Ordinal);
        bool hasEwp = name.Contains("EWP", StringComparison.Ordinal);
        return hasEwp && !hasCom ? "EWP" : "COM";
    }

    public static IReadOnlyList<string> PageFolderNames(string mode) =>
        string.Equals(mode, "EWP", StringComparison.OrdinalIgnoreCase) ? PageFoldersEwp : PageFoldersCom;

    public static FolderTemplateResult CreatePageFolders(string baseFolder, string mode) =>
        CreateFlatFolders(baseFolder, PageFolderNames(mode));

    public static FolderTemplateResult CreateTakeoffTree(string baseFolder, string mode)
    {
        IReadOnlyList<FolderNode> tree = string.Equals(mode, "EWP", StringComparison.OrdinalIgnoreCase)
            ? TakeoffTreeEwp
            : TakeoffTreeStandard;
        return CreateTree(baseFolder, tree);
    }

    public static IReadOnlyList<string> CollectCapsGroupNames(SmartTakeoffsJob job)
    {
        var candidates = new List<string>();
        string others = Path.Combine(job.PagesRoot, "--------others");

        foreach (PageInfo page in CollectPages(job.PagesRoot))
        {
            if (SmartTakeoffsJobStore.IsSameOrDescendant(others, page.FolderPath))
                continue;
            candidates.Add(page.Name);
        }

        foreach (string folder in CollectFolders(job.PagesRoot))
        {
            string name = SmartTakeoffsJobStore.DisplayName(folder).Trim();
            if (name is "Pages" or "00. imported" or "--------others")
                continue;
            candidates.Add(name);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (string candidate in candidates)
        {
            string? group = CapsNameGroupFromPageName(candidate);
            if (string.IsNullOrWhiteSpace(group))
                continue;

            string key = SmartTakeoffsJobStore.SanitizeName(group, 120);
            if (!seen.Add(key))
                continue;
            result.Add(group);
        }

        result.Sort((a, b) => string.Compare(
            SmartTakeoffsJobStore.SanitizeName(a, 120),
            SmartTakeoffsJobStore.SanitizeName(b, 120),
            StringComparison.OrdinalIgnoreCase));
        return result;
    }

    public static CapsTakeoffFolderResult CreateTakeoffFoldersFromCapsPages(
        SmartTakeoffsJob job,
        string baseFolder,
        string mode)
    {
        IReadOnlyList<string> names = CollectCapsGroupNames(job);
        int topCreated = 0;
        int topSkipped = 0;
        int subCreated = 0;
        int subSkipped = 0;
        int errors = 0;
        var messages = new List<string>();

        foreach (string name in names)
        {
            try
            {
                var (created, folder) = EnsureFolder(baseFolder, name);
                if (created)
                    topCreated++;
                else
                    topSkipped++;

                FolderTemplateResult tree = CreateTakeoffTree(folder, mode);
                subCreated += tree.Created;
                subSkipped += tree.Skipped;
                errors += tree.Errors;
                messages.AddRange(tree.ErrorMessages);
            }
            catch (Exception ex)
            {
                errors++;
                messages.Add($"{name}: {ex.Message}");
            }
        }

        return new CapsTakeoffFolderResult(
            topCreated,
            topSkipped,
            subCreated,
            subSkipped,
            errors,
            names,
            messages);
    }

    public static string PreviewNames(IEnumerable<string> names, int max = 25)
    {
        var list = names.Take(max + 1).ToList();
        if (list.Count == 0)
            return "(none)";

        string preview = string.Join(Environment.NewLine, list.Take(max).Select(name => $"  {name}"));
        int remaining = list.Count - max;
        return remaining > 0
            ? preview + Environment.NewLine + $"  ... +{remaining} more"
            : preview;
    }

    private static FolderTemplateResult CreateFlatFolders(string baseFolder, IReadOnlyList<string> names)
    {
        int created = 0;
        int skipped = 0;
        int errors = 0;
        var messages = new List<string>();

        foreach (string name in names)
        {
            try
            {
                var (wasCreated, _) = EnsureFolder(baseFolder, name);
                if (wasCreated)
                    created++;
                else
                    skipped++;
            }
            catch (Exception ex)
            {
                errors++;
                messages.Add($"{name}: {ex.Message}");
            }
        }

        return new FolderTemplateResult(created, skipped, errors, messages);
    }

    private static FolderTemplateResult CreateTree(string baseFolder, IReadOnlyList<FolderNode> nodes)
    {
        FolderTemplateResult total = FolderTemplateResult.Empty;
        foreach (FolderNode node in nodes)
        {
            try
            {
                var (created, folder) = EnsureFolder(baseFolder, node.Name);
                var current = new FolderTemplateResult(created ? 1 : 0, created ? 0 : 1, 0, []);
                if (node.Children.Count > 0)
                    current = current.Add(CreateTree(folder, node.Children));
                total = total.Add(current);
            }
            catch (Exception ex)
            {
                total = total.Add(new FolderTemplateResult(0, 0, 1, [$"{node.Name}: {ex.Message}"]));
            }
        }

        return total;
    }

    private static (bool Created, string FolderPath) EnsureFolder(string parentFolder, string name)
    {
        string clean = SmartTakeoffsJobStore.SanitizeName(name, 120);
        string path = Path.Combine(parentFolder, clean);
        bool existed = Directory.Exists(path) && File.Exists(Path.Combine(path, "Data.xml"));
        string ensured = SmartTakeoffsJobStore.EnsureFolder(parentFolder, name);
        return (!existed, ensured);
    }

    private static IEnumerable<PageInfo> CollectPages(string folder)
    {
        if (!Directory.Exists(folder))
            yield break;

        if (SmartTakeoffsJobStore.TryReadPage(folder) is { } page)
        {
            yield return page;
            yield break;
        }

        foreach (string child in SmartTakeoffsJobStore.GetOrderedChildDirectories(folder))
        {
            foreach (PageInfo childPage in CollectPages(child))
                yield return childPage;
        }
    }

    private static IEnumerable<string> CollectFolders(string folder)
    {
        if (!Directory.Exists(folder))
            yield break;

        if (!SmartTakeoffsJobStore.IsPageFolder(folder))
            yield return folder;

        foreach (string child in SmartTakeoffsJobStore.GetOrderedChildDirectories(folder))
        {
            foreach (string nested in CollectFolders(child))
                yield return nested;
        }
    }

    private static string? CapsNameGroupFromPageName(string name)
    {
        string s = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(s) || s.EndsWith("-", StringComparison.Ordinal))
            return null;

        Match trailingTitle = SheetTitleRegex().Match(s);
        if (trailingTitle.Success)
        {
            string title = LeadingNonWordRegex().Replace(trailingTitle.Groups[1].Value.Trim(), "").Trim();
            Match uppercaseTail = UppercaseTailRegex().Match(title);
            if (uppercaseTail.Success)
                return uppercaseTail.Value.Trim();
        }

        Match trailingCaps = TrailingCapsRegex().Match(s);
        if (trailingCaps.Success)
            return trailingCaps.Groups[1].Value.Trim();

        return StartsWithUppercaseRegex().IsMatch(s) ? s : null;
    }

    [GeneratedRegex(@"\b[A-Za-z]+\d+[A-Za-z0-9.\-]*\s+(.+)$")]
    private static partial Regex SheetTitleRegex();

    [GeneratedRegex(@"^\W+")]
    private static partial Regex LeadingNonWordRegex();

    [GeneratedRegex(@"[A-Z].*$")]
    private static partial Regex UppercaseTailRegex();

    [GeneratedRegex(@"\s+([A-Z][A-Za-z0-9/ .&+\-]*)$")]
    private static partial Regex TrailingCapsRegex();

    [GeneratedRegex(@"^[A-Z]")]
    private static partial Regex StartsWithUppercaseRegex();
}
