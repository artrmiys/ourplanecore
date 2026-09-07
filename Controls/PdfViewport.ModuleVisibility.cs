using System.Collections.Generic;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    private bool _annotationsModuleEnabled = true;

    public IReadOnlyList<PdfLayer> CurrentPdfLayers => _layers;

    public void SetAnnotationsModuleEnabled(bool enabled)
    {
        if (_annotationsModuleEnabled == enabled)
            return;

        _annotationsModuleEnabled = enabled;
        if (!enabled)
            ClearAnnotationSelection();
        RequestRepaint();
    }
}
