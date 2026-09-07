using System.IO;

namespace OurPlanCore;

public static partial class SmartMassingDraftService
{
    private static List<TakeoffItem> ApplyAiTakeoffPlan(
        OurPlanCoreJob job,
        IReadOnlyList<TakeoffItem> items,
        SmartMassingTakeoffAiPlan? aiPlan,
        SmartMassingDraft draft)
    {
        if (aiPlan == null || aiPlan.Assignments.Count == 0)
            return items.ToList();

        Dictionary<string, SmartMassingTakeoffAiAssignment> assignments = AiAssignmentsByKey(job, aiPlan);
        var result = new List<TakeoffItem>();
        foreach (TakeoffItem item in items)
        {
            if (!TryFindAiAssignment(job, assignments, item, out SmartMassingTakeoffAiAssignment? assignment))
            {
                result.Add(item);
                continue;
            }

            SmartMassingTakeoffAiAssignment matched = assignment!;
            string role = NormalizeAiRole(matched.Role);
            if (role == "ignore")
            {
                draft.Assumptions.Add($"AI 3D sort ignored '{item.Name}': {matched.Reason}");
                continue;
            }

            result.Add(CloneWithAiRole(item, matched, role));
        }

        if (!string.IsNullOrWhiteSpace(aiPlan.Summary))
            draft.Assumptions.Add($"AI 3D sort summary: {aiPlan.Summary}");
        foreach (string warning in aiPlan.Warnings.Where(warning => !string.IsNullOrWhiteSpace(warning)).Take(8))
            draft.UnresolvedQuestions.Add($"AI 3D sort warning: {warning}");
        return result;
    }

    private static void AddAiPlanFloorSources(
        OurPlanCoreJob job,
        List<WallFloorSource> sources,
        IReadOnlyList<TakeoffItem> items,
        SmartMassingTakeoffAiPlan? aiPlan)
    {
        if (aiPlan == null || aiPlan.Assignments.Count == 0)
            return;

        Dictionary<string, SmartMassingTakeoffAiAssignment> assignments = AiAssignmentsByKey(job, aiPlan);
        var grouped = new Dictionary<int, List<TakeoffItem>>();
        foreach (TakeoffItem item in items)
        {
            if (!TryFindAiAssignment(job, assignments, item, out SmartMassingTakeoffAiAssignment? assignment))
                continue;

            SmartMassingTakeoffAiAssignment matched = assignment!;
            string role = NormalizeAiRole(matched.Role);
            if (!IsAiFootprintRole(role) || matched.Level < 0)
                continue;

            int level = matched.Level == 0 ? 1 : matched.Level;
            if (!grouped.TryGetValue(level, out List<TakeoffItem>? list))
            {
                list = [];
                grouped[level] = list;
            }

            list.Add(item);
        }

        foreach ((int level, List<TakeoffItem> levelItems) in grouped.OrderBy(entry => entry.Key))
        {
            sources.Add(new WallFloorSource(
                level,
                $"ai-plan-level-{level}",
                $"AI 3D sort {LevelDisplayName(level)}",
                levelItems));
        }
    }

    private static Dictionary<string, SmartMassingTakeoffAiAssignment> AiAssignmentsByKey(
        OurPlanCoreJob job,
        SmartMassingTakeoffAiPlan aiPlan)
    {
        var assignments = new Dictionary<string, SmartMassingTakeoffAiAssignment>(StringComparer.OrdinalIgnoreCase);
        foreach (SmartMassingTakeoffAiAssignment assignment in aiPlan.Assignments)
        {
            foreach (string key in AssignmentKeys(job, assignment))
            {
                if (!assignments.ContainsKey(key))
                    assignments[key] = assignment;
            }
        }

        return assignments;
    }

    private static IEnumerable<string> AssignmentKeys(OurPlanCoreJob job, SmartMassingTakeoffAiAssignment assignment)
    {
        if (!string.IsNullOrWhiteSpace(assignment.TakeoffId))
            yield return $"id:{assignment.TakeoffId.Trim()}";
        if (!string.IsNullOrWhiteSpace(assignment.FolderPath))
        {
            string folder = assignment.FolderPath.Trim();
            yield return $"rel:{folder}";
            yield return $"full:{Path.GetFullPath(Path.Combine(job.RootPath, folder))}";
        }
    }

    private static bool TryFindAiAssignment(
        OurPlanCoreJob job,
        IReadOnlyDictionary<string, SmartMassingTakeoffAiAssignment> assignments,
        TakeoffItem item,
        out SmartMassingTakeoffAiAssignment? assignment)
    {
        string id = Path.GetFileName(item.FolderPath);
        if (assignments.TryGetValue($"id:{id}", out assignment))
            return true;

        string relative = Path.GetRelativePath(job.RootPath, item.FolderPath);
        if (assignments.TryGetValue($"rel:{relative}", out assignment))
            return true;

        return assignments.TryGetValue($"full:{Path.GetFullPath(item.FolderPath)}", out assignment);
    }

    private static TakeoffItem CloneWithAiRole(
        TakeoffItem item,
        SmartMassingTakeoffAiAssignment assignment,
        string role)
    {
        var clone = new TakeoffItem
        {
            Name = item.Name,
            Color = item.Color,
            FolderPath = item.FolderPath,
            MeasurementType = item.MeasurementType,
            UnitPrice = item.UnitPrice,
            Notes = AppendAiRoleNotes(item.Notes, assignment, role),
            IsJoistTakeoff = item.IsJoistTakeoff,
            JoistType = item.JoistType,
            JoistSpacingInches = item.JoistSpacingInches,
            JoistDirectionDegrees = item.JoistDirectionDegrees,
            JoistDirectionFollowsAreaRotation = item.JoistDirectionFollowsAreaRotation,
            JoistAddEndJoist = item.JoistAddEndJoist,
            JoistPitch = item.JoistPitch,
            JoistLengthRounding = item.JoistLengthRounding,
            JoistShowLabels = item.JoistShowLabels,
            JoistDetailedLabels = item.JoistDetailedLabels,
            JoistMoveNote = item.JoistMoveNote,
        };
        clone.Measurements.AddRange(item.Measurements);
        return clone;
    }

    private static string AppendAiRoleNotes(
        string notes,
        SmartMassingTakeoffAiAssignment assignment,
        string role)
    {
        string roleText = role switch
        {
            "floor_plate" or "sqft" or "footprint" => "ai_role sqft footprint",
            "wall" or "exterior_wall" => "ai_role ext exterior wall",
            "eave" => "ai_role eave eve",
            "rake" => "ai_role rake",
            "gable" or "gable_area" => "ai_role gable",
            _ => $"ai_role {role}",
        };
        string levelText = assignment.Level > 0 ? $" level {assignment.Level}" : "";
        string confidence = assignment.Confidence > 0 ? $" confidence {assignment.Confidence:0.00}" : "";
        string reason = string.IsNullOrWhiteSpace(assignment.Reason) ? "" : $" reason {assignment.Reason}";
        return $"{notes} {roleText}{levelText}{confidence}{reason}".Trim();
    }

    private static bool IsAiFootprintRole(string role) =>
        role is "floor_plate" or "sqft" or "footprint" or "wall" or "exterior_wall";

    private static string NormalizeAiRole(string role)
    {
        string clean = NormalizeTakeoffName(role).Trim();
        return clean switch
        {
            "floor" or "floor plate" or "floor_plate" or "area" or "slab" => "floor_plate",
            "sq ft" or "square feet" or "square foot" or "square footage" or "sft" or "sf" => "sqft",
            "exterior wall" or "exterior_wall" or "walls" => "wall",
            "rakes" => "rake",
            "eaves" or "eve" => "eave",
            "gables" => "gable",
            "" => "unknown",
            _ => clean.Replace(' ', '_'),
        };
    }
}
