using SkiaSharp;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    public bool IsRepeatDrawingActive { get; private set; }

    public bool StopRepeatDrawing()
    {
        if (!IsRepeatDrawingActive)
            return false;

        SetTool("select");
        ToolChanged?.Invoke("select");
        PostStatus("Repeat drawing stopped.");
        return true;
    }

    private void AddRepeatLinePoint(SKPoint pdf)
    {
        _drawPts.Add(pdf);
        if (_drawPts.Count < 2)
        {
            RequestRepaint();
            PostRecordPrompt();
            return;
        }

        if (MeasurementGeometry.Distance(_drawPts[0], _drawPts[1]) <= ViewportConstants.ZeroLengthEpsilon)
        {
            CancelDrawing();
            PostRecordPrompt();
            return;
        }

        FinalizeDrawing();
    }
}
