using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace OurPlanCore;

public sealed class SmartMassingAiTakeoffBuildResult
{
    public bool Success { get; init; }
    public SmartMassingDraft? Draft { get; init; }
    public SmartMassingTakeoffAiPlan? Plan { get; init; }
    public string Error { get; init; } = "";
    public string InputPath { get; init; } = "";
    public string PlanPath { get; init; } = "";
    public string RawResponsePath { get; init; } = "";
    public string Model { get; init; } = "";
}

public static class SmartMassingTakeoffAiPlanner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static async Task<SmartMassingAiTakeoffBuildResult> BuildDraftAsync(
        OurPlanCoreJob job,
        double levelSpacingFeet,
        string apiKey,
        string model,
        CancellationToken cancellationToken)
    {
        string requestId = $"massing_takeoff_sort_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}";
        object input = BuildInput(job, levelSpacingFeet);
        string inputPath = SaveMassingJson(job, $"{requestId}.input.json", input);
        string prompt = BuildPrompt(input, inputPath);

        SmartAiRunResult run = await OpenAiRequestRunner.RunStructuredJsonAsync(
            job,
            requestId,
            prompt,
            BuildPlanSchema(),
            "massing_takeoff_sort_plan",
            BuildInstructions(),
            apiKey,
            model,
            maxOutputTokens: 5000,
            cancellationToken);

        if (!run.Success)
        {
            return new SmartMassingAiTakeoffBuildResult
            {
                Success = false,
                Error = run.Error,
                InputPath = inputPath,
                RawResponsePath = run.RawResponsePath,
                Model = run.Model,
            };
        }

        if (!TryParsePlan(run.OutputText, out SmartMassingTakeoffAiPlan? plan, out string parseError))
        {
            return new SmartMassingAiTakeoffBuildResult
            {
                Success = false,
                Error = parseError,
                InputPath = inputPath,
                RawResponsePath = run.RawResponsePath,
                Model = run.Model,
            };
        }

        SmartMassingTakeoffAiPlan parsedPlan = plan!;
        parsedPlan.LevelSpacingFeet = levelSpacingFeet;
        NormalizePlan(parsedPlan);
        string planPath = SaveMassingJson(job, $"{requestId}.plan.json", parsedPlan);

        SmartMassingDraft draft = SmartMassingDraftService.BuildDraftFromWallTakeoffs(job, levelSpacingFeet, parsedPlan);
        draft.Assumptions.Add($"OpenAI 3D sort used model {run.Model}; input: {inputPath}; plan: {planPath}; raw response: {run.RawResponsePath}.");
        draft.Assumptions.Add("OpenAI sorted takeoff roles/levels only. 3D geometry was still generated deterministically from saved measurements.");
        SmartMassingDraftService.SaveDraft(job, draft);
        return new SmartMassingAiTakeoffBuildResult
        {
            Success = true,
            Draft = draft,
            Plan = parsedPlan,
            InputPath = inputPath,
            PlanPath = planPath,
            RawResponsePath = run.RawResponsePath,
            Model = run.Model,
        };
    }

    public static object BuildInput(OurPlanCoreJob job, double levelSpacingFeet)
    {
        IReadOnlyList<PageInfo> pages = LoadPages(job);
        Dictionary<string, PageInfo> pageByFolder = pages
            .GroupBy(page => NormalizePath(page.FolderPath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<TakeoffItem> items = OurPlanCoreJobStore.LoadTakeoffItems(job);

        return new
        {
            schema_version = 1,
            project = new
            {
                name = job.Name,
                default_level_spacing_feet = levelSpacingFeet,
            },
            rules = new
            {
                output_is_classification_only = true,
                geometry_is_built_by_app_from_saved_measurements = true,
                roles = new[]
                {
                    "floor_plate",
                    "wall",
                    "eave",
                    "rake",
                    "gable",
                    "opening",
                    "ignore",
                    "unknown",
                },
                roof_type_values = new[] { "unknown", "gable", "hip", "shed", "low_slope", "flat" },
            },
            pages = pages.Select(page => new
            {
                name = page.Name,
                folder_path = Rel(job, page.FolderPath),
                scale_meters_per_pdf_point = page.ScaleMetersPerPt,
                pdf_page = page.PdfPage,
            }).ToList(),
            takeoffs = items.Select(item => TakeoffInput(job, item, pageByFolder)).ToList(),
        };
    }

    private static object TakeoffInput(
        OurPlanCoreJob job,
        TakeoffItem item,
        IReadOnlyDictionary<string, PageInfo> pageByFolder)
    {
        var measurements = item.Measurements.Select(measurement =>
        {
            pageByFolder.TryGetValue(NormalizePath(measurement.PageFolder), out PageInfo? page);
            SKRect bounds = Bounds(measurement.Points);
            return new
            {
                id = measurement.Id,
                type = measurement.MType,
                page = page?.Name ?? "",
                page_folder = string.IsNullOrWhiteSpace(measurement.PageFolder) ? "" : Rel(job, measurement.PageFolder),
                scale_meters_per_pdf_point = measurement.ScaleMetersPerPt,
                point_count = measurement.Points.Count,
                bounds_pdf = new[]
                {
                    Math.Round(bounds.Left, 3),
                    Math.Round(bounds.Top, 3),
                    Math.Round(bounds.Right, 3),
                    Math.Round(bounds.Bottom, 3),
                },
            };
        }).ToList();

        return new
        {
            takeoff_id = Path.GetFileName(item.FolderPath),
            folder_path = Rel(job, item.FolderPath),
            parent_folder = Rel(job, Path.GetDirectoryName(item.FolderPath) ?? ""),
            name = item.Name,
            notes = item.Notes,
            measurement_type = item.MeasurementType,
            measurement_count = item.Measurements.Count,
            pages = measurements
                .Select(measurement => measurement.page)
                .Where(page => !string.IsNullOrWhiteSpace(page))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            measurements,
        };
    }

    private static string BuildPrompt(object input, string inputPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Sort construction takeoff data for a reviewable 3D massing draft.");
        sb.AppendLine();
        sb.AppendLine("Your job:");
        sb.AppendLine("- Classify each takeoff into one role: floor_plate, wall, eave, rake, gable, opening, ignore, or unknown.");
        sb.AppendLine("- Assign a floor level when the takeoff belongs to a building level. Use 1 for first floor, 2 for second, etc.");
        sb.AppendLine("- Infer roof_type only from takeoff names/folders/pages; use unknown when uncertain.");
        sb.AppendLine("- Do not create geometry, dimensions, quantities, or coordinates.");
        sb.AppendLine("- Prefer deterministic evidence: item name, folder path, measurement type, page name, and level words.");
        sb.AppendLine("- If a folder combines words like 'eve rake', classify by the item name first.");
        sb.AppendLine("- Use ignore for unrelated takeoffs.");
        sb.AppendLine("- Include every takeoff_id exactly once when possible.");
        sb.AppendLine();
        sb.AppendLine($"Saved local input path: {inputPath}");
        sb.AppendLine();
        sb.AppendLine("Input JSON:");
        sb.AppendLine(JsonSerializer.Serialize(input, JsonOptions));
        return sb.ToString();
    }

    private static string BuildInstructions() =>
        "You are sorting construction takeoff metadata for a WPF takeoff app. " +
        "Return only JSON matching the schema. Do not invent geometry; classify and explain uncertainty.";

    private static object BuildPlanSchema() =>
        new Dictionary<string, object>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new[] { "schema_version", "summary", "roof_type", "level_spacing_feet", "assignments", "warnings" },
            ["properties"] = new Dictionary<string, object>
            {
                ["schema_version"] = new Dictionary<string, object> { ["type"] = "integer" },
                ["summary"] = new Dictionary<string, object> { ["type"] = "string" },
                ["roof_type"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["enum"] = new[] { "unknown", "gable", "hip", "shed", "low_slope", "flat" },
                },
                ["level_spacing_feet"] = new Dictionary<string, object> { ["type"] = "number" },
                ["warnings"] = new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object> { ["type"] = "string" },
                },
                ["assignments"] = new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = new[] { "takeoff_id", "folder_path", "role", "level", "confidence", "reason" },
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["takeoff_id"] = new Dictionary<string, object> { ["type"] = "string" },
                            ["folder_path"] = new Dictionary<string, object> { ["type"] = "string" },
                            ["role"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["enum"] = new[] { "floor_plate", "wall", "eave", "rake", "gable", "opening", "ignore", "unknown" },
                            },
                            ["level"] = new Dictionary<string, object> { ["type"] = "integer" },
                            ["confidence"] = new Dictionary<string, object> { ["type"] = "number" },
                            ["reason"] = new Dictionary<string, object> { ["type"] = "string" },
                        },
                    },
                },
            },
        };

    private static bool TryParsePlan(string json, out SmartMassingTakeoffAiPlan? plan, out string error)
    {
        plan = null;
        error = "";
        try
        {
            plan = JsonSerializer.Deserialize<SmartMassingTakeoffAiPlan>(json);
            if (plan == null)
            {
                error = "OpenAI returned empty AI 3D sort JSON.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Could not parse OpenAI AI 3D sort JSON: {ex.Message}";
            return false;
        }
    }

    private static void NormalizePlan(SmartMassingTakeoffAiPlan plan)
    {
        plan.RoofType = NormalizeRoofType(plan.RoofType);
        foreach (SmartMassingTakeoffAiAssignment assignment in plan.Assignments)
        {
            assignment.Role = NormalizeRole(assignment.Role);
            assignment.Confidence = Math.Clamp(assignment.Confidence, 0, 1);
            assignment.TakeoffId = assignment.TakeoffId.Trim();
            assignment.FolderPath = assignment.FolderPath.Trim();
            assignment.Reason = assignment.Reason.Trim();
        }
    }

    private static string NormalizeRole(string role) =>
        role.Trim().ToLowerInvariant().Replace(" ", "_") switch
        {
            "floor" or "floorplate" or "sqft" or "sft" or "sf" => "floor_plate",
            "walls" or "exterior_wall" => "wall",
            "eve" or "eaves" => "eave",
            "rakes" => "rake",
            "gables" or "gable_area" => "gable",
            "openings" => "opening",
            "ignored" => "ignore",
            "" => "unknown",
            string value => value,
        };

    private static string NormalizeRoofType(string roofType) =>
        roofType.Trim().ToLowerInvariant().Replace(" ", "_") switch
        {
            "gable" or "hip" or "shed" or "low_slope" or "flat" => roofType.Trim().ToLowerInvariant().Replace(" ", "_"),
            _ => "unknown",
        };

    private static IReadOnlyList<PageInfo> LoadPages(OurPlanCoreJob job)
    {
        if (!Directory.Exists(job.PagesRoot))
            return [];

        return Directory.EnumerateDirectories(job.PagesRoot, "*", SearchOption.AllDirectories)
            .Where(OurPlanCoreJobStore.IsPageFolder)
            .Select(OurPlanCoreJobStore.TryReadPage)
            .Where(page => page != null)
            .Select(page => page!)
            .OrderBy(page => page.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string SaveMassingJson(OurPlanCoreJob job, string fileName, object value)
    {
        string dir = Path.Combine(job.AIContextRoot, "3d_massing", "ai_takeoff_sort");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName);
        IoUtil.WriteAllTextAtomic(path, JsonSerializer.Serialize(value, JsonOptions));
        return Path.GetRelativePath(job.RootPath, path);
    }

    private static SKRect Bounds(IReadOnlyList<SKPoint> points)
    {
        if (points.Count == 0)
            return SKRect.Empty;

        float minX = points.Min(point => point.X);
        float maxX = points.Max(point => point.X);
        float minY = points.Min(point => point.Y);
        float maxY = points.Max(point => point.Y);
        return new SKRect(minX, minY, maxX, maxY);
    }

    private static string Rel(OurPlanCoreJob job, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        try
        {
            return Path.GetRelativePath(job.RootPath, path);
        }
        catch
        {
            return path;
        }
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path.Trim();
        }
    }
}
