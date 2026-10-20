using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Snapview;

internal readonly record struct ToolbarPosition(Rect Tools, Rect Options);

internal static class ToolbarPlacement
{
    internal static ToolbarPosition Calculate(Size area, Size tools, Size options, bool docked, Point position)
    {
        double width = Math.Max(0, area.Width), height = Math.Max(0, area.Height);
        double x = docked ? (width - tools.Width) / 2 : position.X;
        double y = docked ? 5 : position.Y;
        x = Math.Clamp(x, 4, Math.Max(4, width - tools.Width - 4));
        y = Math.Clamp(y, 4, Math.Max(4, height - tools.Height - 4));
        double ox = Math.Clamp(x + (tools.Width - options.Width) / 2, 4, Math.Max(4, width - options.Width - 4));
        double oy = y + tools.Height + 5;
        if (oy + options.Height > height - 4) oy = y - options.Height - 5;
        oy = Math.Clamp(oy, 4, Math.Max(4, height - options.Height - 4));
        return new(new Rect(new Point(x, y), tools), new Rect(new Point(ox, oy), options));
    }
}

// UI-only placement; never changes image coordinates, document state, or saved pixels.
internal sealed class FloatingToolbar
{
    private readonly Canvas area;
    private readonly FrameworkElement tools, options;
    private readonly Thumb grip;
    private Point position, dragStart;
    private bool docked = true, startedDocked;

    internal FloatingToolbar(Canvas area, FrameworkElement tools, FrameworkElement options, Thumb grip)
    {
        this.area = area; this.tools = tools; this.options = options; this.grip = grip;
        area.SizeChanged += (_, _) => Arrange();
        tools.SizeChanged += (_, _) => Arrange();
        options.SizeChanged += (_, _) => Arrange();
        options.IsVisibleChanged += (_, _) => Arrange();
        area.Loaded += (_, _) => Arrange();
        grip.DragStarted += (_, _) => { dragStart = position; startedDocked = docked; };
        grip.DragDelta += (_, e) => { docked = false; position += new Vector(e.HorizontalChange, e.VerticalChange); Arrange(); };
        grip.DragCompleted += (_, e) => { if (e.Canceled) { docked = startedDocked; position = dragStart; Arrange(); } };
        grip.MouseDoubleClick += (_, e) => { grip.CancelDrag(); Dock(); e.Handled = true; };
    }

    internal void Dock() { docked = true; Arrange(); }

    internal bool HandleKey(KeyEventArgs e)
    {
        if (!grip.IsKeyboardFocusWithin) return false;
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        if (e.Key == Key.Home) Dock();
        else if (e.Key == Key.Escape && grip.IsDragging) grip.CancelDrag();
        else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            docked = false;
            position += e.Key switch { Key.Left => new Vector(-step, 0), Key.Right => new Vector(step, 0), Key.Up => new Vector(0, -step), _ => new Vector(0, step) };
            Arrange();
        }
        else return false;
        e.Handled = true; return true;
    }

    internal void Arrange()
    {
        if (area.ActualWidth <= 0 || area.ActualHeight <= 0) return;
        tools.Measure(new Size(area.ActualWidth, double.PositiveInfinity));
        options.Measure(new Size(Math.Max(0, area.ActualWidth - 8), double.PositiveInfinity));
        var layout = ToolbarPlacement.Calculate(new Size(area.ActualWidth, area.ActualHeight), tools.DesiredSize,
            options.Visibility == Visibility.Visible ? options.DesiredSize : new Size(), docked, position);
        position = layout.Tools.TopLeft;
        Canvas.SetLeft(tools, position.X); Canvas.SetTop(tools, position.Y);
        Canvas.SetLeft(options, layout.Options.X); Canvas.SetTop(options, layout.Options.Y);
    }
}
