using System.Windows;
using System.Windows.Controls;

namespace Snapview;

internal static class Dialogs
{
    public static Window Create(Window owner, string title, string description, double width = 420)
    {
        var window = new Window { Owner = owner, Title = title, Width = width, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, MaxHeight = 650 };
        window.SourceInitialized += (_, _) => Theme.UpdateCaption(window);
        var panel = new StackPanel { Margin = new Thickness(26) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        var hint = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 22) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); panel.Children.Add(hint);
        window.Content = panel; return window;
    }
    public static Button Action(Window window, string text, Action action)
    {
        var button = new Button { Content = text, Style = (Style)Application.Current.FindResource("Primary"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        button.Click += (_, _) => action(); ((StackPanel)window.Content).Children.Add(button); return button;
    }
    public static (int Width, int Height)? Resize(Window owner, int width, int height)
    {
        var window = Create(owner, "Resize image", "Set the dimensions in pixels. Your original stays available in Revert.");
        var panel = (StackPanel)window.Content;
        panel.Children.Add(new TextBlock { Text = "Width", Margin = new Thickness(0, 0, 0, 5) });
        var w = new TextBox { Text = width.ToString() }; panel.Children.Add(w);
        panel.Children.Add(new TextBlock { Text = "Height", Margin = new Thickness(0, 14, 0, 5) });
        var h = new TextBox { Text = height.ToString() }; panel.Children.Add(h);
        var locked = new CheckBox { Content = "Keep proportions", IsChecked = true, Margin = new Thickness(0, 16, 0, 0) }; panel.Children.Add(locked);
        var error = new TextBlock { Foreground = System.Windows.Media.Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) }; panel.Children.Add(error);
        bool updating = false;
        w.TextChanged += (_, _) => { if (!updating && locked.IsChecked == true && int.TryParse(w.Text, out int n) && n > 0 && n <= 32000) { updating = true; h.Text = Math.Max(1, (int)Math.Round(n * (double)height / width)).ToString(); updating = false; } };
        h.TextChanged += (_, _) => { if (!updating && locked.IsChecked == true && int.TryParse(h.Text, out int n) && n > 0 && n <= 32000) { updating = true; w.Text = Math.Max(1, (int)Math.Round(n * (double)width / height)).ToString(); updating = false; } };
        (int, int)? result = null;
        Action(window, "Resize", () =>
        {
            try
            {
                if (!int.TryParse(w.Text, out int newWidth) || !int.TryParse(h.Text, out int newHeight)) throw new InvalidOperationException("Enter whole numbers for both dimensions.");
                Imaging.CheckSize(newWidth, newHeight); result = (newWidth, newHeight); window.DialogResult = true;
            }
            catch (InvalidOperationException ex) { error.Text = ex.Message; }
        });
        window.ShowDialog(); return result;
    }
    public static void Ocr(Window owner, string text)
    {
        var window = Create(owner, "Text, out of the picture", "Recognized locally on your device. Select any part, or copy everything.", 540);
        var panel = (StackPanel)window.Content;
        var input = new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 280, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(input);
        var action = Action(window, "Copy all text", () => { try { Clipboard.SetText(input.Text); window.Close(); } catch { MessageBox.Show(window, "The clipboard is busy. Try again.", "Clipboard"); } });
        action.IsEnabled = !string.IsNullOrWhiteSpace(text);
        window.Loaded += (_, _) => { input.Focus(); input.SelectAll(); }; window.ShowDialog();
    }
}
