using OurPlanCore;
using SkiaSharp;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    private bool TryEditNoteAnnotationAt(SKPoint pdf)
    {
        if (!TryHitAnnotation(pdf, out PageAnnotation annotation))
            return false;

        string kind = OurPlanCoreJobStore.NormalizePageAnnotationKind(annotation.Kind);
        if (!string.Equals(kind, "note", StringComparison.OrdinalIgnoreCase))
            return false;

        SelectAnnotation(annotation, -1);
        RequestRepaint();

        string? edited = PageAnnotationTextRequested?.Invoke(
            "Note text:",
            annotation.Text,
            "Edit Sheet Note");
        if (edited == null)
        {
            PostStatus("Note edit cancelled.");
            return true;
        }

        if (!UpdatePageAnnotationText(annotation, edited))
            PostStatus("Note unchanged.");
        return true;
    }
}
