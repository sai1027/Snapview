using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Snapview;

// UI-only crop chrome. PixelBounds is also used by Apply so the badge matches the export.
internal sealed class CropPreview : FrameworkElement
{
    public Rect Selection { get; set; }
    public double Zoom { get; set; } = 1;

    public static Int32Rect PixelBounds(Rect selection, int width, int height)
    {
        int left = Math.Clamp((int)Math.Floor(selection.Left), 0, width - 1);
        int top = Math.Clamp((int)Math.Floor(selection.Top), 0, height - 1);
        int right = Math.Clamp((int)Math.Ceiling(selection.Right), left + 1, width);
        int bottom = Math.Clamp((int)Math.Ceiling(selection.Bottom), top + 1, height);
        return new Int32Rect(left, top, right - left, bottom - top);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Width < 1 || Height < 1 || Selection.Width < 1 || Selection.Height < 1) return;
        var pixels = PixelBounds(Selection, (int)Width, (int)Height);
        var selected = new Rect(pixels.X, pixels.Y, pixels.Width, pixels.Height);
        var mask = new GeometryGroup { FillRule = FillRule.EvenOdd };
        mask.Children.Add(new RectangleGeometry(new Rect(0, 0, Width, Height)));
        mask.Children.Add(new RectangleGeometry(selected));
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(150, 15, 23, 20)), null, mask);
        // Both strokes remain legible over light and dark photographs, at any zoom.
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(200, 20, 30, 25)), 3 / Zoom), selected);
        dc.DrawRectangle(null, new Pen(Brushes.White, 1.4 / Zoom), selected);

        string ratio = pixels.Width >= pixels.Height
            ? $"{pixels.Width / (double)pixels.Height:0.##}:1"
            : $"1:{pixels.Height / (double)pixels.Width:0.##}";
        var label = new FormattedText($"{pixels.Width:N0} × {pixels.Height:N0} px  ·  {ratio}",
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"),
            12 / Zoom, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        double padding = 8 / Zoom, gap = 8 / Zoom;
        double badgeWidth = Math.Min(Width, label.Width + padding * 2);
        double badgeHeight = label.Height + padding;
        double x = Math.Clamp(selected.Right - badgeWidth, 0, Math.Max(0, Width - badgeWidth));
        double y = selected.Bottom + gap;
        if (y + badgeHeight > Height) y = selected.Bottom - badgeHeight - gap;
        y = Math.Clamp(y, 0, Math.Max(0, Height - badgeHeight));
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(240, 30, 42, 36)), null,
            new Rect(x, y, badgeWidth, badgeHeight), 5 / Zoom, 5 / Zoom);
        dc.DrawText(label, new Point(x + padding, y + padding / 2));
    }
}
