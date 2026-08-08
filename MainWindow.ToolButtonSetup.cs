using System.Collections.Generic;
using System.Windows.Controls;

namespace OurPlanCore;

public partial class MainWindow
{
    private Dictionary<string, RadioButton> BuildToolButtons()
    {
        var buttons = new Dictionary<string, RadioButton>
        {
            ["pan"] = BtnPan,
            ["select"] = BtnSelect,
            ["scale"] = BtnScale,
            ["ruler"] = BtnRuler,
            ["pitch"] = BtnPitch,
            ["drawhighlight"] = BtnHighlight,
            ["drawline"] = BtnDrawLine,
            ["drawarrow"] = BtnDrawArrow,
            ["drawrect"] = BtnDrawRect,
            ["drawcloud"] = BtnDrawCloud,
            ["drawarea"] = BtnDrawAreaAnnot,
            ["note"] = BtnNote,
            ["point"] = BtnPoint,
            ["line"] = BtnLine,
            ["area"] = BtnArea,
            ["joistarea"] = BtnJoistArea,
            ["beam"] = BtnBeam,
            ["openings"] = BtnOpenings,
            ["areacut"] = BtnAreaCut,
        };

        BtnPan.ToolTip = $"Pan ({KeyboardShortcutKeys.EnglishLayoutDisplay("v")})";
        BtnSelect.ToolTip = $"Select ({KeyboardShortcutKeys.EnglishLayoutDisplay("e")})";
        BtnScale.ToolTip = $"Scale ({KeyboardShortcutKeys.EnglishLayoutDisplay("s")})";
        BtnRuler.ToolTip = $"Ruler ({KeyboardShortcutKeys.EnglishLayoutDisplay("r")})";
        BtnPitch.ToolTip = "Pitch: click two roof-slope points to place a rise:12 label";
        BtnHighlight.ToolTip = $"Highlighter ({KeyboardShortcutKeys.EnglishLayoutDisplay("h")})";
        BtnDrawLine.ToolTip = $"Draw line ({KeyboardShortcutKeys.EnglishLayoutDisplay("d")})";
        BtnDrawRect.ToolTip = "Draw box";
        BtnDrawCloud.ToolTip = "Cloud annotation";
        BtnDrawAreaAnnot.ToolTip = "Filled area annotation";
        BtnNote.ToolTip = $"Sheet note ({KeyboardShortcutKeys.EnglishLayoutDisplay("n")})";
        BtnPoint.ToolTip = $"Count item ({KeyboardShortcutKeys.EnglishLayoutDisplay("p")})";
        BtnLine.ToolTip = $"Line item ({KeyboardShortcutKeys.EnglishLayoutDisplay("l")})";
        BtnArea.ToolTip = $"Area item ({KeyboardShortcutKeys.EnglishLayoutDisplay("a")})";
        BtnJoistArea.ToolTip = $"Joist area ({KeyboardShortcutKeys.EnglishLayoutDisplay("j")})";
        BtnBeam.ToolTip = $"Beam: measure length, create Count item, and place first Count mark ({KeyboardShortcutKeys.EnglishLayoutDisplay("b")})";
        BtnOpenings.ToolTip = $"Openings: measure box, create Count item, and place the Count mark in the center ({KeyboardShortcutKeys.EnglishLayoutDisplay("o")})";
        BtnAreaCut.ToolTip = $"Area cut ({KeyboardShortcutKeys.EnglishLayoutDisplay("x")})";
        return buttons;
    }
}
