namespace OurPlanCore;

public enum PdfTakeoffImportMode
{
    CreateNewJob,
    ImportIntoCurrentJob,
}

public sealed class PdfTakeoffImportOptions
{
    public PdfTakeoffImportMode Mode { get; set; } = PdfTakeoffImportMode.CreateNewJob;
    public string SourceFolder { get; set; } = "";
    public string JobsRootPath { get; set; } = "";
    public string JobName { get; set; } = "";
    public bool ImportTakeoffs { get; set; } = true;
    public bool ImportDimensionsAsRulers { get; set; } = true;
    public bool RemoveSupportedPdfAnnotations { get; set; } = true;
}
