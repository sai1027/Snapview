using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Snapview;

// Immutable values: history shares the base bitmap and snapshots these small objects.
internal sealed record Annotation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Kind { get; init; } = "Rectangle";
    public Rect Box { get; init; }
    public Color Color { get; init; } = Colors.White;
    public double Stroke { get; init; } = 5;
    public string Text { get; init; } = "Text";
    public double FontSize { get; init; } = 48;
    public double StretchX { get; init; } = 1;
    public double StretchY { get; init; } = 1;
    public Point Start { get; init; }
    public Point End { get; init; }
    public Point[] Points { get; init; } = [];

    public FormattedText FormatText()
    {
        var result = new FormattedText(string.IsNullOrEmpty(Text) ? " " : Text,
            CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"),
            FontSize, new SolidColorBrush(Color), 1);
        result.MaxTextWidth = Math.Max(8, Box.Width);
        return result;
    }
    public Rect Bounds => Kind == "Text"
        ? new Rect(Box.X, Box.Y, Box.Width * StretchX, FormatText().Height * StretchY)
        : Kind == "Arrow" ? new Rect(Start, End) : Box;

    public Annotation Move(Vector delta) => this with
    {
        Box = new Rect(Box.Location + delta, Box.Size), Start = Start + delta, End = End + delta,
        Points = Points.Select(p => p + delta).ToArray()
    };
    public Annotation Resize(Rect bounds, bool reflowText)
    {
        var old = Bounds;
        if (Kind == "Text")
        {
            if (reflowText) return this with { Box = new Rect(bounds.X, Box.Y, Math.Max(8, bounds.Width / StretchX), Box.Height) };
            return this with { Box = new Rect(bounds.Location, Box.Size),
                StretchX = Math.Clamp(bounds.Width / Box.Width, .05, 40),
                StretchY = Math.Clamp(bounds.Height / FormatText().Height, .05, 40) };
        }
        Point Map(Point p) => new(bounds.X + (p.X - old.X) * bounds.Width / Math.Max(1, old.Width),
            bounds.Y + (p.Y - old.Y) * bounds.Height / Math.Max(1, old.Height));
        return this with { Box = bounds, Start = Map(Start), End = Map(End), Points = Points.Select(Map).ToArray() };
    }
    public Geometry Geometry()
    {
        if (Kind == "Rectangle") return new RectangleGeometry(Box);
        if (Kind == "Ellipse") return new EllipseGeometry(Box);
        var geometry = new StreamGeometry();
        using var c = geometry.Open();
        if (Kind == "Arrow")
        {
            c.BeginFigure(Start, false, false); c.LineTo(End, true, false);
            var direction = End - Start;
            if (direction.Length > .1)
            {
                double length = Math.Min(direction.Length / 2, Math.Max(Stroke * 4, 16));
                direction.Normalize(); var normal = new Vector(-direction.Y, direction.X);
                c.BeginFigure(End - direction * length + normal * length * .55, false, false);
                c.LineTo(End, true, false); c.LineTo(End - direction * length - normal * length * .55, true, false);
            }
        }
        else if (Points.Length > 0)
        {
            c.BeginFigure(Points[0], false, false);
            foreach (var p in Points.Skip(1)) c.LineTo(p, true, false);
            if (Points.Length == 1) c.LineTo(Points[0] + new Vector(.01, .01), true, false);
        }
        return geometry;
    }
    public bool Hit(Point point, double tolerance)
    {
        if (Kind == "Text") return Bounds.Contains(point);
        var shape = Geometry();
        return shape.StrokeContains(new Pen(Brushes.Black, Math.Max(Stroke, tolerance * 2)), point)
            || ((Kind is "Rectangle" or "Ellipse") && shape.FillContains(point));
    }
    public void Draw(DrawingContext dc)
    {
        if (Kind == "Text")
        {
            dc.PushTransform(new TranslateTransform(Box.X, Box.Y));
            dc.PushTransform(new ScaleTransform(StretchX, StretchY));
            dc.DrawText(FormatText(), new Point()); dc.Pop(); dc.Pop();
        }
        else dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color), Stroke)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, Geometry());
    }
}

internal sealed class ImageScene(BitmapSource image, Annotation[] annotations)
{
    public BitmapSource Image { get; } = image;
    public Annotation[] Annotations { get; } = annotations;
    private BitmapSource? composite;
    public BitmapSource Composite
    {
        get
        {
            if (Annotations.Length == 0) return Image;
            if (composite != null) return composite;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(Image, new Rect(0, 0, Image.PixelWidth, Image.PixelHeight));
                foreach (var item in Annotations) item.Draw(dc);
            }
            return composite = Imaging.Render(visual, Image.PixelWidth, Image.PixelHeight);
        }
    }
    public void ReleaseComposite() => composite = null;
}
