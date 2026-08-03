namespace OurPlanCore;

public static class JoistTakeoffDefaults
{
    public const string LengthRounding = JoistTakeoffCalculator.RoundingNearestFoot;
    public const bool ShowLabels = false;
    public const bool DetailedAreaLabel = false;
    public const bool DirectionFollowsAreaRotation = true;
    public const bool AddEndJoist = false;

    public static void ApplyToNewJoistArea(TakeoffItem item)
    {
        item.IsJoistTakeoff = true;
        item.JoistDirectionFollowsAreaRotation = DirectionFollowsAreaRotation;
        item.JoistAddEndJoist = AddEndJoist;
        item.JoistLengthRounding = LengthRounding;
        item.JoistShowLabels = ShowLabels;
        item.JoistDetailedLabels = DetailedAreaLabel;
    }
}
