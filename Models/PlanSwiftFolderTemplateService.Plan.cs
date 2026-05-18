using System;
using System.Collections.Generic;
using System.Linq;

namespace OurPlaneCore;

// Public, editable/serializable folder-plan surface for the Settings > From Pages
// editor. Lets the UI show the exact folders that will be created, edit them,
// store presets, and create folders from the edited plan.
public sealed class FolderPlanNode
{
    public string Name { get; set; } = "";
    public List<FolderPlanNode> Children { get; set; } = [];

    public FolderPlanNode Clone() =>
        new() { Name = Name, Children = Children.Select(c => c.Clone()).ToList() };
}

public sealed class FolderPlan
{
    public string Name { get; set; } = "Default";
    public string Mode { get; set; } = "COM";
    public List<string> TopFolders { get; set; } = [];
    public List<FolderPlanNode> SubTree { get; set; } = [];

    public FolderPlan Clone() => new()
    {
        Name = Name,
        Mode = Mode,
        TopFolders = [.. TopFolders],
        SubTree = SubTree.Select(n => n.Clone()).ToList(),
    };
}

public static partial class PlanSwiftFolderTemplateService
{
    private static FolderPlanNode ToPlan(FolderNode node) =>
        new() { Name = node.Name, Children = node.Children.Select(ToPlan).ToList() };

    // The hard-coded standard sub-tree for a mode, as an editable plan.
    public static List<FolderPlanNode> HardcodedSubTree(string mode) =>
        (string.Equals(mode, "EWP", StringComparison.OrdinalIgnoreCase)
            ? TakeoffTreeEwp
            : TakeoffTreeStandard)
        .Select(ToPlan)
        .ToList();

    // Effective sub-tree: user override for the mode if present, else default.
    public static List<FolderPlanNode> DefaultSubTree(string mode)
    {
        IReadOnlyList<FolderPlanNode>? over = TakeoffTreeOverride?.Invoke(mode);
        return over is { Count: > 0 }
            ? over.Select(n => n.Clone()).ToList()
            : HardcodedSubTree(mode);
    }

    // The default plan: top folders detected from Pages CAPS names + the
    // standard sub-tree for the resolved mode.
    public static FolderPlan BuildDefaultPlan(OurPlaneCoreJob job, string requestedMode = "AUTO")
    {
        string mode = ResolveMode(job, requestedMode);
        return new FolderPlan
        {
            Name = "Default",
            Mode = mode,
            TopFolders = CollectCapsGroupNames(job).ToList(),
            SubTree = DefaultSubTree(mode),
        };
    }

    private static FolderTemplateResult CreatePlanTree(string baseFolder, IReadOnlyList<FolderPlanNode> nodes)
    {
        FolderTemplateResult total = FolderTemplateResult.Empty;
        foreach (FolderPlanNode node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Name))
                continue;

            try
            {
                var (created, folder) = EnsureFolder(baseFolder, node.Name);
                var current = new FolderTemplateResult(created ? 1 : 0, created ? 0 : 1, 0, []);
                if (node.Children.Count > 0)
                    current = current.Add(CreatePlanTree(folder, node.Children));
                total = total.Add(current);
            }
            catch (Exception ex)
            {
                total = total.Add(new FolderTemplateResult(0, 0, 1, [$"{node.Name}: {ex.Message}"]));
            }
        }

        return total;
    }

    // Create exactly the folders described by an (edited) plan.
    public static CapsTakeoffFolderResult CreateFoldersFromPlan(string baseFolder, FolderPlan plan)
    {
        int topCreated = 0, topSkipped = 0, subCreated = 0, subSkipped = 0, errors = 0;
        var messages = new List<string>();
        var topNames = new List<string>();

        foreach (string rawName in plan.TopFolders)
        {
            string name = (rawName ?? "").Trim();
            if (name.Length == 0)
                continue;

            topNames.Add(name);
            try
            {
                var (created, folder) = EnsureFolder(baseFolder, name);
                if (created) topCreated++; else topSkipped++;

                FolderTemplateResult tree = CreatePlanTree(folder, plan.SubTree);
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
            topCreated, topSkipped, subCreated, subSkipped, errors, topNames, messages);
    }
}
