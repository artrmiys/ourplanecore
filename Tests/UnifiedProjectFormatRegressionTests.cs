internal static class UnifiedProjectFormatRegressionTests
{
    public static void OpenImportUsesOneProjectPickerForBothStorageTypes()
    {
        string menu = ReadRepoFile("MainWindow.OpenImportMenu.cs");
        string picker = ReadRepoFile("MainWindow.JobPicker.cs");
        string pickerXaml = ReadRepoFile(Path.Combine("Dialogs", "JobPickerDialog.xaml"));
        int openSectionStart = menu.IndexOf("// One project picker", StringComparison.Ordinal);
        int createSectionStart = menu.IndexOf("// Create a new job.", StringComparison.Ordinal);
        string openSection = openSectionStart >= 0 && createSectionStart > openSectionStart
            ? menu[openSectionStart..createSectionStart]
            : "";

        AssertTrue(
            openSection.Contains("MakeMenuHeader(\"OPEN A PROJECT\")", StringComparison.Ordinal) &&
            openSection.Contains(
                "MakeMenuItem(\"Open project...\", true, ShowRecentJobPicker)",
                StringComparison.Ordinal) &&
            !openSection.Contains("OpenOurPlanProjectDialog", StringComparison.Ordinal) &&
            !openSection.Contains("OpenJobFromFolderDialog", StringComparison.Ordinal) &&
            !openSection.Contains(".ourplan", StringComparison.OrdinalIgnoreCase),
            "Open / Import must expose one neutral project picker instead of separate file and folder choices");
        AssertTrue(
            picker.Contains("EnumerateProjectFoldersSafe(rootPath)", StringComparison.Ordinal) &&
            picker.Contains("EnumerateProjectPackagesSafe(rootPath)", StringComparison.Ordinal) &&
            picker.Contains("OpenJobSafely(selectedJobPath);", StringComparison.Ordinal),
            "the one project picker must still list and open both folder projects and package files");
        AssertTrue(
            menu.Contains("Blank project - start empty", StringComparison.Ordinal) &&
            menu.Contains("New project from a folder of PDFs...", StringComparison.Ordinal),
            "new-project commands must use neutral project wording");
        AssertFalse(
            menu.Contains("Create legacy folder project", StringComparison.OrdinalIgnoreCase) ||
            menu.Contains("Blank legacy project", StringComparison.OrdinalIgnoreCase) ||
            menu.Contains("Legacy project from PDFs", StringComparison.OrdinalIgnoreCase) ||
            menu.Contains("Blank OurPlan project", StringComparison.OrdinalIgnoreCase) ||
            menu.Contains("New OurPlan project", StringComparison.OrdinalIgnoreCase),
            "Open / Import must not split new-project creation by storage format");
        AssertFalse(
            picker.Contains("OurPlan file", StringComparison.Ordinal) ||
            picker.Contains("Legacy folder", StringComparison.Ordinal),
            "the job picker must identify project sources without format badges");
        AssertTrue(
            pickerXaml.Contains("Text=\"Open project file...\"", StringComparison.Ordinal) &&
            pickerXaml.Contains("ToolTip=\"Create a new project from PDFs\"", StringComparison.Ordinal) &&
            pickerXaml.Contains("ToolTip=\"Create an empty project\"", StringComparison.Ordinal) &&
            !pickerXaml.Contains("default Project Storage format", StringComparison.Ordinal),
            "the job picker must present neutral create/open actions without a format preference");
    }

    public static void EveryNewProjectEntryPointAlwaysUsesPackageCreation()
    {
        string menu = ReadRepoFile("MainWindow.OpenImportMenu.cs");
        string picker = ReadRepoFile("MainWindow.JobPicker.cs");
        string lifecycle = ReadRepoFile("MainWindow.JobLifecycle.cs");
        string palette = ReadRepoFile("MainWindow.CommandPalette.cs");
        string planSwift = ReadRepoFile("MainWindow.PlanSwiftImport.cs");
        string pdfTakeoffs = ReadRepoFile("MainWindow.PdfTakeoffImport.cs");
        string settings = ReadRepoFile("MainWindow.SettingsManager.ProjectStorage.cs");
        string runtimeRouting = string.Concat(
            menu,
            picker,
            lifecycle,
            palette,
            planSwift,
            pdfTakeoffs,
            settings);

        AssertFalse(
            runtimeRouting.Contains("UseLegacyFolderForNewProjects", StringComparison.Ordinal) ||
            runtimeRouting.Contains("NewProjectStorageFormat", StringComparison.Ordinal) ||
            runtimeRouting.Contains("forceOurPlan", StringComparison.Ordinal) ||
            runtimeRouting.Contains("CreateLegacyJobFromDialog", StringComparison.Ordinal) ||
            runtimeRouting.Contains("CreateLegacyBlankJobFromDialog", StringComparison.Ordinal),
            "saved legacy-format settings and legacy creation helpers must not route any new-project command");
        AssertTrue(
            picker.Contains("private void CreateJobFromDialog(string? preferredParent = null)", StringComparison.Ordinal) &&
            picker.Contains("CreateNewPackageProject(name, out OurPlanCoreJob? createdJob", StringComparison.Ordinal) &&
            picker.Contains("private void CreateBlankJobFromDialog(string? preferredParent = null)", StringComparison.Ordinal) &&
            picker.Contains("CreateNewPackageProject(name, out OurPlanCoreJob? job", StringComparison.Ordinal),
            "blank and PDF-folder jobs must always use the package project workflow");
        AssertTrue(
            picker.Contains("private void CreateSampleJob(string? preferredParent = null)", StringComparison.Ordinal) &&
            picker.Contains("OurPlanPackageWorkspace.ReserveManagedWorkspace(displayName)", StringComparison.Ordinal) &&
            picker.Contains("OurPlanPackageWriter.SaveAs(", StringComparison.Ordinal),
            "sample jobs must always be created and published as package projects");
        AssertTrue(
            planSwift.Contains("await ImportPlanSwiftJobAsOurPlanAsync(options);", StringComparison.Ordinal),
            "new PlanSwift imports must always use the managed package import workflow");
        AssertTrue(
            pdfTakeoffs.Contains("CreateNewPackageProjectAtPath(jobName, packagePath", StringComparison.Ordinal) &&
            pdfTakeoffs.Contains("OurPlanPackageFormat.Extension", StringComparison.Ordinal),
            "new PDF Takeoffs jobs and their preview paths must always use the package workflow");
        AssertTrue(
            palette.Contains(
                "case \"file.newJob\": BtnNewJob_Click(this, new RoutedEventArgs());",
                StringComparison.Ordinal) &&
            palette.Contains(
                "case \"file.blankJob\": BtnBlankJob_Click(this, new RoutedEventArgs());",
                StringComparison.Ordinal) &&
            palette.Contains("case \"file.sampleJob\": CreateSampleJob();", StringComparison.Ordinal) &&
            lifecycle.Contains("private void BtnNewJob_Click", StringComparison.Ordinal) &&
            lifecycle.Contains("CreateJobFromDialog();", StringComparison.Ordinal) &&
            lifecycle.Contains("private void BtnBlankJob_Click", StringComparison.Ordinal) &&
            lifecycle.Contains("CreateBlankJobFromDialog();", StringComparison.Ordinal),
            "command-palette creation commands must use the same unified entry points");
    }

    public static void PdfTakeoffPreviewMatchesExactCreatedPackagePath()
    {
        string importer = ReadRepoFile("MainWindow.PdfTakeoffImport.cs");
        string packageCreation = ReadRepoFile("MainWindow.ProjectPackage.cs");
        int prepare = importer.IndexOf(
            "PreparePdfTakeoffProjectDestination(options);",
            StringComparison.Ordinal);
        int preview = importer.IndexOf(
            "PreviewPdfTakeoffImportDestinations(options);",
            prepare,
            StringComparison.Ordinal);
        int targetStart = importer.IndexOf(
            "private OurPlanCoreJob? EnsurePdfTakeoffImportTargetJob(",
            StringComparison.Ordinal);
        int targetEnd = importer.IndexOf(
            "private bool EnsurePdfTakeoffImportTargetWritable(",
            targetStart,
            StringComparison.Ordinal);
        string target = targetStart >= 0 && targetEnd > targetStart
            ? importer[targetStart..targetEnd]
            : "";
        int previewStart = importer.IndexOf(
            "private (string PagesFolder, string TakeoffsFolder) PreviewPdfTakeoffImportDestinations(",
            StringComparison.Ordinal);
        int prepareStart = importer.IndexOf(
            "private static void PreparePdfTakeoffProjectDestination(",
            previewStart,
            StringComparison.Ordinal);
        string previewMethod = previewStart >= 0 && prepareStart > previewStart
            ? importer[previewStart..prepareStart]
            : "";
        int exactStart = packageCreation.IndexOf(
            "private bool CreateNewPackageProjectAtPath(",
            StringComparison.Ordinal);
        int dialogStart = packageCreation.IndexOf(
            "private SaveFileDialog CreateOurPlanSaveDialog(",
            exactStart,
            StringComparison.Ordinal);
        string exactCreation = exactStart >= 0 && dialogStart > exactStart
            ? packageCreation[exactStart..dialogStart]
            : "";

        AssertTrue(prepare >= 0 && preview > prepare,
            "PDF Takeoffs must reserve its unique job name before showing the destination preview");
        AssertTrue(
            target.Contains(
                "string packagePath = PdfTakeoffPackagePath(parent, jobName);",
                StringComparison.Ordinal) &&
            target.Contains(
                "CreateNewPackageProjectAtPath(jobName, packagePath",
                StringComparison.Ordinal),
            "PDF Takeoffs creation must publish directly to its precomputed package path without a second Save dialog");
        AssertTrue(
            previewMethod.Contains(
                "string packagePath = PdfTakeoffPackagePath(parent, options.JobName);",
                StringComparison.Ordinal),
            "PDF Takeoffs preview must use the same package-path helper as creation");
        AssertTrue(
            exactCreation.Contains("OurPlanPackageFormat.HasPackageExtension(destination)", StringComparison.Ordinal) &&
            exactCreation.Contains("OurPlanPackageWriter.SaveAs(", StringComparison.Ordinal) &&
            exactCreation.Contains("destination,", StringComparison.Ordinal),
            "the explicit-destination helper must validate and publish the exact .ourplan path it receives");
    }

    public static void ProjectStorageSettingsShowsOneFormatWithoutSelector()
    {
        string settings = ReadRepoFile("MainWindow.SettingsManager.ProjectStorage.cs");

        AssertTrue(
            settings.Contains("Header(\"Project format\")", StringComparison.Ordinal) &&
            settings.Contains("New projects are always saved as one portable .ourplan file.", StringComparison.Ordinal) &&
            settings.Contains("Existing folder projects keep their current format", StringComparison.Ordinal),
            "Project Storage settings must explain the one-format creation rule and legacy in-place compatibility");
        AssertFalse(
            settings.Contains("_newProjectStorageFormatCombo", StringComparison.Ordinal) ||
            settings.Contains("UseLegacyFolderForNewProjects", StringComparison.Ordinal) ||
            settings.Contains("NewProjectStorageFormat", StringComparison.Ordinal) ||
            settings.Contains("Legacy project folder", StringComparison.Ordinal) ||
            settings.Contains("Reset recommended", StringComparison.Ordinal),
            "Project Storage settings must not expose a selectable project-format preference");
    }

    private static string ReadRepoFile(string relativePath)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate) && File.Exists(Path.Combine(current.FullName, "ourplancore.csproj")))
                return File.ReadAllText(candidate);
            current = current.Parent;
        }

        throw new FileNotFoundException($"Repository file not found: {relativePath}");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message) => AssertTrue(!condition, message);
}
