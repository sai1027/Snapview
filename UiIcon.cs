using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Snapview;

// One optical size and stroke weight, independent of installed symbol fonts.
public sealed class UiIcon : Control
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(UiIcon),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }

    private static readonly IReadOnlyDictionary<string, Geometry> Shapes = new Dictionary<string, string>
    {
        ["Open"] = "M3,7 V5 Q3,4 4,4 H9 L12,7 H20 Q21,7 21,8 V19 Q21,20 20,20 H4 Q3,20 3,19 Z M3,9 H21",
        ["Crop"] = "M6,2 V17 Q6,18 7,18 H22 M2,6 H17 Q18,6 18,7 V22",
        ["Resize"] = "M14,3 H21 V10 M21,3 L13,11 M3,14 V21 H10 M3,21 L11,13",
        ["Rotate"] = "M19,8 A8,8 0 1 0 20,15 M19,3 V8 H14",
        ["Pen"] = "M4,16 L15,5 Q17,3 19,5 Q21,7 19,9 L8,20 L3,21 Z M13,7 L17,11 M4,16 L8,20",
        ["Text"] = "M4,5 V3 H20 V5 M12,3 V21 M8,21 H16",
        ["Ocr"] = "M8,3 H3 V8 M16,3 H21 V8 M21,16 V21 H16 M8,21 H3 V16 M7,8 H17 M7,12 H17 M7,16 H14",
        ["Undo"] = "M8,4 L3,9 L8,14 M3,9 H15 A6,6 0 0 1 15,21 H11",
        ["Redo"] = "M16,4 L21,9 L16,14 M21,9 H9 A6,6 0 0 0 9,21 H13",
        ["Info"] = "M12,2 A10,10 0 1 1 12,22 A10,10 0 1 1 12,2 M12,11 V17 M12,7 V7.3",
        ["Delete"] = "M3,5 H21 M9,5 V2 H15 V5 M5,5 L6,21 H18 L19,5 M10,9 V17 M14,9 V17",
        ["Save"] = "M14,4 H20 V10 M20,4 L11,13 M9,5 H5 Q3,5 3,7 V19 Q3,21 5,21 H17 Q19,21 19,19 V14",
        ["Select"] = "M5,2 L20,13 L13,14 L10,21 Z",
        ["Rectangle"] = "M3,5 H21 V19 H3 Z",
        ["Ellipse"] = "M12,4 A9,8 0 1 1 12,20 A9,8 0 1 1 12,4",
        ["Arrow"] = "M4,20 L20,4 M9,4 H20 V15",
        ["Check"] = "M3,12 L9,18 L21,5",
        ["Close"] = "M5,5 L19,19 M19,5 L5,19",
        ["Minimize"] = "M5,12 H19",
        ["Maximize"] = "M5,5 H19 V19 H5 Z",
        ["Restore"] = "M8,5 V3 H21 V16 H19 M3,8 H16 V21 H3 Z",
        ["Minus"] = "M5,12 H19",
        ["Plus"] = "M5,12 H19 M12,5 V19",
        ["Previous"] = "M15,4 L7,12 L15,20",
        ["Next"] = "M9,4 L17,12 L9,20",
        ["Italic"] = "M8,3 H20 M15,3 L9,21 M3,21 H15"
    }.ToDictionary(pair => pair.Key, pair => { var geometry = Geometry.Parse(pair.Value); geometry.Freeze(); return geometry; });

    static UiIcon()
    {
        WidthProperty.OverrideMetadata(typeof(UiIcon), new FrameworkPropertyMetadata(16d));
        HeightProperty.OverrideMetadata(typeof(UiIcon), new FrameworkPropertyMetadata(16d));
        FocusableProperty.OverrideMetadata(typeof(UiIcon), new FrameworkPropertyMetadata(false));
        IsHitTestVisibleProperty.OverrideMetadata(typeof(UiIcon), new FrameworkPropertyMetadata(false));
        ForegroundProperty.OverrideMetadata(typeof(UiIcon), new FrameworkPropertyMetadata(Brushes.Black,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double scale = Math.Min(ActualWidth, ActualHeight) / 24;
        dc.PushTransform(new TranslateTransform((ActualWidth - scale * 24) / 2, (ActualHeight - scale * 24) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        if (Kind is "Grip" or "More")
        {
            foreach (int y in Kind == "Grip" ? new[] { 5, 12, 19 } : new[] { 12 })
                foreach (int x in Kind == "Grip" ? new[] { 8, 16 } : new[] { 4, 12, 20 })
                    dc.DrawEllipse(Foreground, null, new Point(x, y), 1.4, 1.4);
        }
        else if (Shapes.TryGetValue(Kind, out var geometry))
            dc.DrawGeometry(null, new Pen(Foreground, 1.7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, geometry);
        dc.Pop(); dc.Pop();
    }
}
