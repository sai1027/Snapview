using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Snapview;

/// <summary>Live image-space objects; selection controls are never part of an export.</summary>
public sealed class AnnotationEditor : Canvas
{
    private sealed class Artwork(AnnotationEditor owner) : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc)
        {
            if (owner.Document == null) return;
            foreach (var item in owner.Document.Annotations)
                if (owner.editor == null || item.Id != owner.selected) item.Draw(dc);
            owner.draft?.Draw(dc);
        }
    }
    internal ImageDocument? Document { get; set; }
    internal string Tool { get; private set; } = "View";
    internal Color Ink { get; set; } = Colors.White;
    internal double StrokeWidth { get; set; } = 5;
    internal double TextSize { get; set; } = 48;
    internal event Action? Changed;
    internal event Action? SelectionChanged;
    internal Annotation? Selected => Document?.Annotations.FirstOrDefault(x => x.Id == selected);
    internal bool HasSelection => Selected != null;
    private readonly Artwork artwork;
    private readonly Rectangle outline = new() { IsHitTestVisible = false, Stroke = new SolidColorBrush(Color.FromRgb(42, 133, 229)), Fill = null };
    private readonly Dictionary<string, Thumb> handles = [];
    private Guid? selected;
    private Annotation? draft, gestureOriginal;
    private Point gestureStart;
    private string? gesture;
    private bool gestureCommitted, typingGroup, syncingEditor;
    private TextBox? editor;
    private double zoom = 1;

    public AnnotationEditor()
    {
        Background = Brushes.Transparent; Focusable = true;
        artwork = new(this) { IsHitTestVisible = false };
        Children.Add(artwork);
        SizeChanged += (_, _) => { artwork.Width = ActualWidth; artwork.Height = ActualHeight; };
        LostMouseCapture += (_, _) => { if (IsMouseCaptured) return; draft = null; gesture = null; artwork.InvalidateVisual(); };
    }
    internal void SetZoom(double value) { zoom = Math.Max(.01, value); PositionChrome(); }
    internal void SetTool(string value)
    {
        CancelInteraction();
        FinishTextEditing(); Tool = value;
        Cursor = value == "Text" ? Cursors.IBeam : value is "View" or "Select" ? Cursors.Arrow : Cursors.Cross;
        if (value is "View" or "Crop") Select(null);
    }
    internal bool WillHandle(Point point) => Tool is not ("View" or "Crop") || Hit(point) != null;
    private Annotation? Hit(Point point) => Document?.Annotations.LastOrDefault(a => a.Hit(point, 6 / zoom));
    private Point Position(MouseEventArgs e)
    { var p = e.GetPosition(this); return new(Math.Clamp(p.X, 0, Math.Max(1, Width)), Math.Clamp(p.Y, 0, Math.Max(1, Height))); }

    internal void RefreshScene()
    {
        if (selected != null && Selected == null) { FinishTextEditing(); selected = null; BuildChrome(); SelectionChanged?.Invoke(); }
        if (editor != null && Selected is { Kind: "Text" } text)
        {
            syncingEditor = true;
            if (editor.Text != text.Text) editor.Text = text.Text;
            syncingEditor = false; PositionEditor(text);
        }
        artwork.InvalidateVisual(); PositionChrome();
    }
    private void Select(Guid? id, bool edit = false)
    {
        if (selected != id) { FinishTextEditing(); selected = id; BuildChrome(); }
        if (edit && Selected?.Kind == "Text") BeginTextEditing();
        PositionChrome(); SelectionChanged?.Invoke();
    }
    internal void Deselect() { FinishTextEditing(); Select(null); }
    internal void CancelInteraction()
    {
        draft = null; gesture = null; gestureOriginal = null; gestureCommitted = false;
        if (Mouse.Captured is DependencyObject captured && (captured == this || IsAncestorOf(captured))) Mouse.Capture(null);
        artwork.InvalidateVisual();
    }
    private void Update(Annotation value, bool coalesce = false)
    {
        if (Document == null) return;
        if (Document.Annotations.FirstOrDefault(x => x.Id == value.Id) == value) return;
        Document.SetAnnotations(Document.Annotations.Select(x => x.Id == value.Id ? value : x).ToArray(), coalesce);
        RefreshScene(); Changed?.Invoke();
    }
    internal void ChangeColor(Color color)
    { Ink = color; if (Selected is { } item) Update(item with { Color = color }); }
    internal void ChangeStroke(double size)
    { StrokeWidth = size; if (Selected is { Kind: not "Text" } item) Update(item with { Stroke = size }); }
    internal void ChangeFont(double size)
    { TextSize = size; if (Selected is { Kind: "Text" } item) Update(item with { FontSize = size }); }
    internal bool DeleteSelected()
    {
        if (Document == null || selected == null) return false;
        FinishTextEditing(); var id = selected; Select(null);
        Document.SetAnnotations(Document.Annotations.Where(x => x.Id != id).ToArray());
        RefreshScene(); Changed?.Invoke(); return true;
    }
    internal bool Nudge(Vector delta)
    { if (Selected is not { } item) return false; Update(item.Move(ConstrainMove(item, delta))); return true; }
    private Vector ConstrainMove(Annotation a, Vector delta)
    {
        var b = a.Bounds;
        return new(Math.Clamp(delta.X, -b.Left, Math.Max(-b.Left, Width - b.Right)),
            Math.Clamp(delta.Y, -b.Top, Math.Max(-b.Top, Height - b.Bottom)));
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Document?.BaseImage == null || Tool == "Crop") return;
        var p = Position(e); var hit = Hit(p);
        if (hit != null)
        {
            Select(hit.Id, e.ClickCount >= 2 && hit.Kind == "Text");
            if (editor == null) StartGesture("Move", hit, p, true);
            e.Handled = true; return;
        }
        FinishTextEditing(); Select(null);
        if (Tool == "View") return;
        Focus(); gestureStart = p;
        if (Tool == "Text")
        {
            var width = Math.Min(360, Math.Max(40, Width));
            var text = new Annotation { Kind = "Text", Box = new Rect(Math.Max(0, Math.Min(p.X, Width - width)), p.Y, width, 60),
                FontSize = TextSize, Color = Ink };
            Document.SetAnnotations([.. Document.Annotations, text]);
            Select(text.Id, true); typingGroup = true; Changed?.Invoke();
        }
        else if (Tool != "Select")
        {
            draft = new Annotation { Kind = Tool, Color = Ink, Stroke = StrokeWidth, Box = new Rect(p, p), Start = p, End = p, Points = [p] };
            CaptureMouse();
        }
        e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (draft != null && IsMouseCaptured)
        {
            var p = Position(e);
            draft = draft with { Box = new Rect(gestureStart, p), End = p,
                Points = draft.Kind == "Pen" ? [.. draft.Points, p] : draft.Points };
            artwork.InvalidateVisual(); e.Handled = true;
        }
        else if (gesture != null && IsMouseCaptured) { ApplyGesture(Position(e)); e.Handled = true; }
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (draft != null && Document != null)
        {
            var completed = draft; draft = null;
            if (completed.Kind == "Pen")
            {
                var bounds = Rect.Empty; foreach (var p in completed.Points) bounds.Union(p);
                if (bounds.Width < 1) bounds.Width = 1; if (bounds.Height < 1) bounds.Height = 1;
                completed = completed with { Box = bounds };
            }
            if (completed.Kind == "Pen" || (completed.End - completed.Start).Length > 3)
            {
                Document.SetAnnotations([.. Document.Annotations, completed]); Select(completed.Id); Changed?.Invoke();
            }
        }
        gesture = null; gestureOriginal = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        artwork.InvalidateVisual();
    }
    private void StartGesture(string handle, Annotation original, Point point, bool captureCanvas = false)
    {
        FinishTextEditing(); gesture = handle; gestureOriginal = original; gestureStart = point;
        gestureCommitted = false;
        if (captureCanvas) { Focus(); CaptureMouse(); }
    }
    private void ApplyGesture(Point point)
    {
        if (gestureOriginal is not { } a || gesture == null) return;
        var delta = point - gestureStart;
        Annotation next;
        if (gesture == "Move") next = a.Move(ConstrainMove(a, delta));
        else if (gesture == "Start") next = a with { Start = point };
        else if (gesture == "End") next = a with { End = point };
        else
        {
            var b = a.Bounds; double min = 12 / zoom;
            double left = b.Left, top = b.Top, right = b.Right, bottom = b.Bottom;
            if (gesture.Contains('W')) left = Math.Min(right - min, b.Left + delta.X);
            if (gesture.Contains('E')) right = Math.Max(left + min, b.Right + delta.X);
            if (gesture.Contains('N')) top = Math.Min(bottom - min, b.Top + delta.Y);
            if (gesture.Contains('S')) bottom = Math.Max(top + min, b.Bottom + delta.Y);
            next = a.Resize(new Rect(new Point(left, top), new Point(right, bottom)), gesture is "W" or "E");
        }
        Update(next, gestureCommitted); gestureCommitted = true;
    }

    private void BuildChrome()
    {
        Children.Remove(outline);
        foreach (var thumb in handles.Values) Children.Remove(thumb);
        handles.Clear();
        if (Selected == null) return;
        Children.Add(outline);
        foreach (var key in Selected.Kind == "Arrow" ? new[] { "Start", "End", "Move" } : new[] { "NW", "N", "NE", "E", "SE", "S", "SW", "W", "Move" })
        {
            var thumb = new Thumb { Cursor = key == "Move" ? Cursors.SizeAll : key is "W" or "E" ? Cursors.SizeWE : key is "N" or "S" ? Cursors.SizeNS : key is "NW" or "SE" ? Cursors.SizeNWSE : Cursors.SizeNESW,
                ToolTip = key == "Move" ? "Drag to move" : key is "Start" or "End" ? "Drag this arrow end" : key is "W" or "E" && Selected.Kind == "Text" ? "Drag to wrap text or make it a single line" : "Drag to resize; text corners stretch the letters" };
            var frame = new FrameworkElementFactory(typeof(Border));
            frame.SetValue(Border.BackgroundProperty, key == "Move" ? new SolidColorBrush(Color.FromRgb(42, 133, 229)) : Brushes.White);
            frame.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(42, 133, 229)));
            frame.SetValue(Border.BorderThicknessProperty, new Thickness(1)); frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            if (key == "Move")
            {
                var label = new FrameworkElementFactory(typeof(TextBlock)); label.SetValue(TextBlock.TextProperty, "Move");
                label.SetValue(TextBlock.ForegroundProperty, Brushes.White); label.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                label.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
                label.SetBinding(TextBlock.FontSizeProperty, new System.Windows.Data.Binding("FontSize") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Thumb), 1) });
                frame.AppendChild(label);
            }
            thumb.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = frame };
            System.Windows.Automation.AutomationProperties.SetName(thumb, key == "Move" ? "Move annotation" : "Resize " + key);
            thumb.DragStarted += (_, _) => { if (Selected is { } a) StartGesture(key, a, Position(MouseEventArgsNow())); };
            thumb.DragDelta += (_, _) => ApplyGesture(Position(MouseEventArgsNow()));
            thumb.DragCompleted += (_, _) => { gesture = null; gestureOriginal = null; };
            handles.Add(key, thumb); Children.Add(thumb);
        }
    }
    private static MouseEventArgs MouseEventArgsNow() => new(Mouse.PrimaryDevice, Environment.TickCount);
    private void PositionChrome()
    {
        if (Selected is not { } a) return;
        var b = a.Bounds;
        outline.StrokeThickness = 1 / zoom; outline.StrokeDashArray = new DoubleCollection([4, 3]);
        outline.Width = Math.Max(1, b.Width); outline.Height = Math.Max(1, b.Height); SetLeft(outline, b.X); SetTop(outline, b.Y);
        foreach (var (key, thumb) in handles)
        {
            Point p = key switch
            {
                "Start" => a.Start, "End" => a.End,
                "NW" => b.TopLeft, "N" => new(b.X + b.Width / 2, b.Top), "NE" => b.TopRight,
                "E" => new(b.Right, b.Y + b.Height / 2), "SE" => b.BottomRight,
                "S" => new(b.X + b.Width / 2, b.Bottom), "SW" => b.BottomLeft,
                "W" => new(b.Left, b.Y + b.Height / 2), _ => new(b.Left + 26 / zoom, Math.Max(10 / zoom, b.Top - 14 / zoom))
            };
            double w = (key == "Move" ? 52 : 9) / zoom, h = (key == "Move" ? 20 : 9) / zoom;
            thumb.Width = w; thumb.Height = h;
            if (key == "Move") thumb.FontSize = 11 / zoom;
            SetLeft(thumb, p.X - w / 2); SetTop(thumb, p.Y - h / 2);
        }
    }
    private void BeginTextEditing()
    {
        if (editor != null || Selected is not { Kind: "Text" } text) return;
        editor = new TextBox { Text = text.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = new SolidColorBrush(Color.FromArgb(28, 120, 150, 180)),
            FontFamily = new FontFamily("Segoe UI"), VerticalContentAlignment = VerticalAlignment.Top, MinWidth = 8, MaxLength = 5000,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        System.Windows.Automation.AutomationProperties.SetName(editor, "Annotation text");
        editor.TextChanged += (_, _) =>
        {
            if (syncingEditor || Selected is not { } current || editor == null) return;
            Update(current with { Text = editor.Text }, typingGroup); typingGroup = true;
        };
        editor.LostKeyboardFocus += (_, _) => typingGroup = false;
        editor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape || (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
            { FinishTextEditing(); Focus(); e.Handled = true; }
        };
        Children.Insert(1, editor); PositionEditor(text); artwork.InvalidateVisual();
        editor.Focus(); editor.SelectAll();
    }
    private void PositionEditor(Annotation text)
    {
        if (editor == null) return;
        SetLeft(editor, text.Box.X); SetTop(editor, text.Box.Y);
        editor.Width = text.Box.Width; editor.Height = text.FormatText().Height + 5;
        editor.FontSize = text.FontSize; editor.Foreground = new SolidColorBrush(text.Color);
        editor.LayoutTransform = new ScaleTransform(text.StretchX, text.StretchY);
    }
    internal void FinishTextEditing()
    {
        if (editor == null) return;
        var previous = editor; editor = null; Children.Remove(previous); typingGroup = false;
        artwork.InvalidateVisual();
    }
    internal void EditSelectedText() { if (Selected?.Kind == "Text") BeginTextEditing(); }
}
