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
                    ["Posts", "Beams", "Joists", "Details", "Stairs"],
                    $"{mode} {floorName} folders");
            }
        }
    }

    public static void DefaultsIncludeShearAndHoldownsPerFloor()
    {
        FolderTemplateConfig defaults = FolderTemplateConfig.BuildDefault();
        foreach (string mode in new[] { "COM", "EWP" })
        {
            FolderPlanNode shearWalls = Find(defaults.TreeFor(mode), "Shear Walls", mode);
            AssertNames(
                shearWalls.Children,
                Floors.Select(floor => floor.ShearName),
                $"{mode} shear floors");

            foreach ((_, string floorName) in Floors)
            {
                FolderPlanNode floor = Find(shearWalls.Children, floorName, mode);
                AssertNames(
                    floor.Children,
                    ["Shear", "Holdowns"],
                    $"{mode} {floorName} shear folders");
            }
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
