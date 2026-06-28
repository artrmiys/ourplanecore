using System.Collections.Generic;
using System.Text.Json;

namespace OurPlaneCore;

public static class OurPlaneCoreJobStore
{
    internal static JsonSerializerOptions JsonOptions =>
        StorageSupport.JsonOptions;

    public static IReadOnlyList<string> DrainCorruptJsonFiles() =>
        StorageSupport.DrainCorruptJsonFiles();

    public static OurPlaneCoreJob CreateJob(string parentDir, string jobName) =>
        JobLayout.CreateJob(parentDir, jobName);

    public static OurPlaneCoreJob LoadJob(string rootPath) =>
        JobLayout.LoadJob(rootPath);

    public static string EnsureFolder(string parentFolder, string name) =>
        JobLayout.EnsureFolder(parentFolder, name);

    public static string CreateFolder(string parentFolder, string name) =>
        JobLayout.CreateFolder(parentFolder, name);

    internal static string CreateFolderAllowDuplicateName(string parentFolder, string name) =>
        JobLayout.CreateFolderAllowDuplicateName(parentFolder, name);

    public static string DefaultImportFolder(OurPlaneCoreJob job) =>
        JobLayout.DefaultImportFolder(job);

    public static IReadOnlyList<PageInfo> ImportPdf(
        OurPlaneCoreJob job,
        string pdfSourcePath,
        IReadOnlyList<string> pageNames,
        string destinationFolder,
        IReadOnlyDictionary<int, IReadOnlyList<PdfLayerInfo>>? pdfLayerCache = null) =>
        PageStore.ImportPdf(job, pdfSourcePath, pageNames, destinationFolder, pdfLayerCache);

    public static PageInfo CreatePageFromPdf(
        OurPlaneCoreJob job,
        string pdfSourcePath,
        string displayName,
        string destinationFolder,
        int pdfPage = 0,
        double scaleMetersPerPt = 0) =>
        PageStore.CreatePageFromPdf(job, pdfSourcePath, displayName, destinationFolder, pdfPage, scaleMetersPerPt);

    public static PageInfo CreateBlankPage(
        OurPlaneCoreJob job,
        string displayName,
        string destinationFolder) =>
        PageStore.CreateBlankPage(job, displayName, destinationFolder);

    public static SourceInfo? ReadSource(string pageFolder) =>
        PageStore.ReadSource(pageFolder);

    public static PageInfo? TryReadPage(string pageFolder) =>
        PageStore.TryReadPage(pageFolder);

    public static void SavePageScale(string pageFolder, double scaleMetersPerPt) =>
        PageStore.SavePageScale(pageFolder, scaleMetersPerPt);

    public static void SavePageLegendTakeoffOrder(
        string pageFolder,
        IReadOnlyList<string> legendTakeoffOrder,
        string legendTakeoffOrderMode = "") =>
        PageStore.SavePageLegendTakeoffOrder(pageFolder, legendTakeoffOrder, legendTakeoffOrderMode);

    public static void SavePageHiddenTakeoffs(string pageFolder, IReadOnlyList<string> hiddenTakeoffs) =>
        PageStore.SavePageHiddenTakeoffs(pageFolder, hiddenTakeoffs);

    public static void SavePageHiddenMeasurements(string pageFolder, IReadOnlyList<string> hiddenMeasurements) =>
        PageStore.SavePageHiddenMeasurements(pageFolder, hiddenMeasurements);

    public static void ReplacePagePdf(string pageFolder, string pdfAbsPath, int pageIndex = 0) =>
        PageStore.ReplacePagePdf(pageFolder, pdfAbsPath, pageIndex);

    public static void SavePageOverlay(
        string pageFolder,
        string overlayPageFolder,
        string overlayColor,
        double overlayOpacity) =>
        PageStore.SavePageOverlay(pageFolder, overlayPageFolder, overlayColor, overlayOpacity);

    public static void SavePageOverlayVisibility(string pageFolder, bool isVisible) =>
        PageStore.SavePageOverlayVisibility(pageFolder, isVisible);

    public static void ClearPageOverlay(string pageFolder) =>
        PageStore.ClearPageOverlay(pageFolder);

    public static void SavePageOverlayTransform(
        string pageFolder,
        double offsetXPt,
        double offsetYPt,
        double overlayScale,
        double? overlayRotationDegrees = null) =>
        PageStore.SavePageOverlayTransform(pageFolder, offsetXPt, offsetYPt, overlayScale, overlayRotationDegrees);

    public static string CreateTakeoffFolder(OurPlaneCoreJob job, string parentFolder, string name) =>
        TakeoffStore.CreateTakeoffFolder(job, parentFolder, name);

    public static TakeoffItem CreateTakeoffItem(OurPlaneCoreJob job, string name, string color) =>
        TakeoffStore.CreateTakeoffItem(job, name, color);

    public static TakeoffItem CreateTakeoffItem(OurPlaneCoreJob job, string parentFolder, string name, string color, string measurementType) =>
        TakeoffStore.CreateTakeoffItem(job, parentFolder, name, color, measurementType);

    public static IReadOnlyList<TakeoffItem> LoadTakeoffItems(OurPlaneCoreJob job) =>
        TakeoffStore.LoadTakeoffItems(job);

    public static TakeoffItem? TryReadTakeoffItem(string folder) =>
        TakeoffStore.TryReadTakeoffItem(folder);

    public static void SaveTakeoffItem(TakeoffItem item) =>
        TakeoffStore.SaveTakeoffItem(item);

    public static void ApplyTakeoffPropertiesToMeasurements(TakeoffItem item) =>
        TakeoffStore.ApplyTakeoffPropertiesToMeasurements(item);

    public static List<Measurement> LoadMeasurements(string takeoffFolder) =>
        TakeoffStore.LoadMeasurements(takeoffFolder);

    public static void SaveMeasurements(string takeoffFolder, IEnumerable<Measurement> measurements) =>
        TakeoffStore.SaveMeasurements(takeoffFolder, measurements);

    public static List<PageAnnotation> LoadPageAnnotations(string pageFolder) =>
        PageAnnotationStore.LoadPageAnnotations(pageFolder);

    public static void SavePageAnnotations(string pageFolder, IEnumerable<PageAnnotation> annotations) =>
        PageAnnotationStore.SavePageAnnotations(pageFolder, annotations);

    public static List<PageBookmark> LoadPageBookmarks(OurPlaneCoreJob job) =>
        PageBookmarkStore.LoadPageBookmarks(job);

    public static void SavePageBookmarks(OurPlaneCoreJob job, IEnumerable<PageBookmark> bookmarks) =>
        PageBookmarkStore.SavePageBookmarks(job, bookmarks);

    public static bool IsPageFolder(string folder) =>
        StorageSupport.IsPageFolder(folder);

    public static string DisplayName(string folder) =>
        StorageSupport.DisplayName(folder);

    public static string PageAnnotationsJsonPath(string pageFolder) =>
        PageAnnotationStore.PageAnnotationsJsonPath(pageFolder);

    public static string PageBookmarksJsonPath(OurPlaneCoreJob job) =>
        PageBookmarkStore.PageBookmarksJsonPath(job);

    public static string NormalizePageAnnotationKind(string value) =>
        PageAnnotationStore.NormalizePageAnnotationKind(value);

    public static bool IsTakeoffItemFolder(string folder) =>
        TakeoffStore.IsTakeoffItemFolder(folder);

    public static IReadOnlyList<string> GetOrderedChildDirectories(string parentFolder) =>
        NodeStore.GetOrderedChildDirectories(parentFolder);

    public static int GetOrderIndex(string folder) =>
        NodeStore.GetOrderIndex(folder);

    public static void SetOrderIndex(string folder, int orderIndex) =>
        NodeStore.SetOrderIndex(folder, orderIndex);

    public static string RenameNode(string folder, string requestedName) =>
        NodeStore.RenameNode(folder, requestedName);

    public static string RenamePageAllowDuplicateName(string folder, string requestedName) =>
        NodeStore.RenamePageAllowDuplicateName(folder, requestedName);

    public static string RenameNodeAllowDuplicateName(string folder, string requestedName) =>
        NodeStore.RenameNodeAllowDuplicateName(folder, requestedName);

    public static string CopyNode(string sourcePath, string targetFolder) =>
        NodeStore.CopyNode(sourcePath, targetFolder);

    public static string CopyNodePreserveDisplayName(string sourcePath, string targetFolder) =>
        NodeStore.CopyNodePreserveDisplayName(sourcePath, targetFolder);

    public static IReadOnlyList<string> CopyNodesPreserveDisplayName(IEnumerable<string> sourcePaths, string targetFolder) =>
        NodeStore.CopyNodesPreserveDisplayName(sourcePaths, targetFolder);

    public static string MoveNode(string sourcePath, string targetFolder) =>
        NodeStore.MoveNode(sourcePath, targetFolder);

    public static IReadOnlyList<(string SourcePath, string MovedPath)> MoveNodes(IEnumerable<string> sourcePaths, string targetFolder) =>
        NodeStore.MoveNodes(sourcePaths, targetFolder);

    public static string DuplicatePage(string pageFolder) =>
        NodeStore.DuplicatePage(pageFolder);

    public static bool MoveSibling(string folder, int offset) =>
        NodeStore.MoveSibling(folder, offset);

    public static bool CanMoveSiblings(IEnumerable<string> folders, int offset) =>
        NodeStore.CanMoveSiblings(folders, offset);

    public static bool MoveSiblings(IEnumerable<string> folders, int offset) =>
        NodeStore.MoveSiblings(folders, offset);

    public static bool CanMoveSiblingsToPosition(IEnumerable<string> folders, string targetFolder, bool after) =>
        NodeStore.CanMoveSiblingsToPosition(folders, targetFolder, after);

    public static bool MoveSiblingsToPosition(IEnumerable<string> folders, string targetFolder, bool after) =>
        NodeStore.MoveSiblingsToPosition(folders, targetFolder, after);

    public static bool MoveSiblingsToEnd(IEnumerable<string> folders, string parentFolder) =>
        NodeStore.MoveSiblingsToEnd(folders, parentFolder);

    public static void SortChildren(string parentFolder, bool descending) =>
        NodeStore.SortChildren(parentFolder, descending);

    public static void SortTakeoffChildren(string parentFolder, bool descending) =>
        NodeStore.SortTakeoffChildren(parentFolder, descending);

    public static void NormalizeOrder(string parentFolder) =>
        NodeStore.NormalizeOrder(parentFolder);

    public static string? ReadName(string folder) =>
        StorageSupport.ReadName(folder);

    public static string? ReadClass(string folder) =>
        StorageSupport.ReadClass(folder);

    public static string SanitizeName(string name, int maxLength) =>
        StorageSupport.SanitizeName(name, maxLength);

    public static string NormalizeDisplayName(string name, int maxLength) =>
        StorageSupport.NormalizeDisplayName(name, maxLength);

    public static bool IsSameOrDescendant(string possibleParent, string possibleChild) =>
        StorageSupport.IsSameOrDescendant(possibleParent, possibleChild);

    public static string NormalizeMeasurementType(string value) =>
        StorageSupport.NormalizeMeasurementType(value);

    public static void SavePageLayerCache(string pageFolder, IReadOnlyList<PdfLayerInfo> pdfLayers) =>
        PageStore.SavePageLayerCache(pageFolder, pdfLayers);

    public static void SavePageRasterSheet(string pageFolder, RasterSheetSource? rasterSheet) =>
        PageStore.SavePageRasterSheet(pageFolder, rasterSheet);

    public static string PageLayersJsonPath(string pageFolder) =>
        PageStore.PageLayersJsonPath(pageFolder);

    public static string SourcePdfMetadataPath(string pageFolder) =>
        PageStore.SourcePdfMetadataPath(pageFolder);

    public static PdfSheetMetadata? ReadSourcePdfMetadata(string pageFolder) =>
        PageStore.ReadSourcePdfMetadata(pageFolder);

    public static void WriteSourcePdfMetadata(string pageFolder, PdfSheetMetadata metadata) =>
        PageStore.WriteSourcePdfMetadata(pageFolder, metadata);

    public static PageLayerManifest? ReadPageLayerManifest(string pageFolder) =>
        PageStore.ReadPageLayerManifest(pageFolder);

    internal static void WriteItemDataXml(string folder, string itemClass, string name, int orderIndex) =>
        StorageSupport.WriteItemDataXml(folder, itemClass, name, orderIndex);

    internal static int GetNextOrderIndex(string parentFolder) =>
        NodeStore.GetNextOrderIndex(parentFolder);

    internal static void UpdateItemName(string folder, string name) =>
        StorageSupport.UpdateItemName(folder, name);

    internal static void SetProperty(string folder, string propertyName, string value) =>
        StorageSupport.SetProperty(folder, propertyName, value);

    internal static void SetProperties(string folder, IEnumerable<System.Collections.Generic.KeyValuePair<string, string>> properties) =>
        StorageSupport.SetProperties(folder, properties);

    internal static void InvalidateMetadataCache(string folder) =>
        StorageSupport.InvalidateDataXmlCache(folder);

    public static void ClearMetadataCache() =>
        StorageSupport.ClearDataXmlCache();

    internal static string? ReadProperty(string folder, string propertyName) =>
        StorageSupport.ReadProperty(folder, propertyName);

    internal static string UniqueDirectoryPath(string desiredPath) =>
        StorageSupport.UniqueDirectoryPath(desiredPath);

    internal static string UniqueFilePath(string desiredPath) =>
        StorageSupport.UniqueFilePath(desiredPath);

    internal static IEnumerable<string> EnumerateSelfAndDescendants(string rootFolder) =>
        StorageSupport.EnumerateSelfAndDescendants(rootFolder);

    internal static void QuarantineCorruptJson(string path, string context, Exception exception) =>
        StorageSupport.QuarantineCorruptJson(path, context, exception);
}
