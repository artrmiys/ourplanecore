using OurPlanCore;

internal static class FolderTemplateDefaultTests
{
    private static readonly (string FramingName, string ShearName)[] Floors =
    [
        ("1st floor framing", "1st floor"),
        ("2nd floor framing", "2nd floor"),
        ("3rd floor framing", "3rd floor"),
        ("4th floor framing", "4th floor"),
        ("5th floor framing", "5th floor"),
        ("loft framing", "loft"),
        ("roof framing", "roof"),
    ];

    public static void DefaultsIncludeFramingTradeFolders()
    {
        FolderTemplateConfig defaults = FolderTemplateConfig.BuildDefault();
        foreach (string mode in new[] { "COM", "EWP" })
        {
            FolderPlanNode framing = Find(defaults.TreeFor(mode), "framing", mode);
            AssertNames(
                framing.Children,
                Floors.Select(floor => floor.FramingName),
                $"{mode} framing floors");

            foreach ((string floorName, _) in Floors)
            {
                FolderPlanNode floor = Find(framing.Children, floorName, mode);
                AssertNames(
                    floor.Children,
                    ["posts", "beams", "headers", "joists", "details", "stairs"],
                    $"{mode} {floorName} folders");

                FolderPlanNode headers = Find(floor.Children, "headers", mode);
                AssertNames(headers.Children, ["ext", "int"], $"{mode} {floorName} header folders");
            }
        }
    }

    public static void DefaultsIncludeShearAndHoldownsPerFloor()
    {
        FolderTemplateConfig defaults = FolderTemplateConfig.BuildDefault();
        foreach (string mode in new[] { "COM", "EWP" })
        {
            FolderPlanNode shearWalls = Find(defaults.TreeFor(mode), "shear walls", mode);
            AssertNames(
                shearWalls.Children,
                Floors.Select(floor => floor.ShearName),
                $"{mode} shear floors");

            foreach ((_, string floorName) in Floors)
            {
                FolderPlanNode floor = Find(shearWalls.Children, floorName, mode);
                AssertNames(
                    floor.Children,
                    ["shear", "holdowns"],
                    $"{mode} {floorName} shear folders");
            }
        }
    }

    public static void DefaultsUseLowercaseFolderNames()
    {
        FolderTemplateConfig defaults = FolderTemplateConfig.BuildDefault();
        foreach (string mode in new[] { "COM", "EWP" })
        {
            foreach (FolderPlanNode node in Descendants(defaults.TreeFor(mode)))
            {
                if (!string.Equals(node.Name, node.Name.ToLowerInvariant(), StringComparison.Ordinal))
                    throw new InvalidOperationException($"{mode}: folder '{node.Name}' is not lowercase.");
            }
        }
    }

    private static IEnumerable<FolderPlanNode> Descendants(IEnumerable<FolderPlanNode> nodes)
    {
        foreach (FolderPlanNode node in nodes)
        {
            yield return node;
            foreach (FolderPlanNode child in Descendants(node.Children))
                yield return child;
        }
    }

    private static FolderPlanNode Find(
        IEnumerable<FolderPlanNode> nodes,
        string name,
        string context) =>
        nodes.FirstOrDefault(node => string.Equals(node.Name, name, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"{context}: folder '{name}' is missing.");

    private static void AssertNames(
        IEnumerable<FolderPlanNode> nodes,
        IEnumerable<string> expected,
        string context)
    {
        string expectedText = string.Join("|", expected);
        string actualText = string.Join("|", nodes.Select(node => node.Name));
        if (!string.Equals(expectedText, actualText, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"{context}: expected '{expectedText}', actual '{actualText}'.");
    }
}
