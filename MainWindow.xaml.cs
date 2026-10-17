using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Snapview;

public partial class MainWindow : Window
{
    private readonly ImageDocument document = new();
    private readonly RenameHistory renameHistory = new();
    private readonly DispatcherTimer toastTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer titleClickTimer = new();
    private string? pendingTitlePath;
    private string? renamePath;
    private bool finishingRename;
    private string tool = "View";
    private Color ink = Colors.White;
    private bool fit = true, busy, drawing, panning;
    private Point start, panStart;
    private double panX, panY;
    private CropPreview? preview;
    private bool syncingAnnotationControls;
    private Rect? crop;
    private string[] siblings = [];
    private string? folder;
    private PhotoMetadata? metadata;
    private long metadataRevision;
    private double Scale => ImageScale.ScaleX;
    private double InkWidth => double.Parse(((ComboBoxItem)StrokeSize.SelectedItem).Content.ToString()!, CultureInfo.InvariantCulture);

    public MainWindow()
    {
        InitializeComponent();
        WindowBounds.Attach(this);
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessage);
        Annotations.Document = document;
        Annotations.Changed += () => { renameHistory.ClearRedo(); Refresh(); };
        Annotations.SelectionChanged += AnnotationSelectionChanged;
        toastTimer.Tick += (_, _) => { ToastBox.Visibility = Visibility.Collapsed; toastTimer.Stop(); };
        titleClickTimer.Interval = TimeSpan.FromMilliseconds(FileActions.GetDoubleClickTime());
        titleClickTimer.Tick += (_, _) =>
        {
            titleClickTimer.Stop();
            if (!busy && pendingTitlePath != null && document.Path == pendingTitlePath) CopyText(pendingTitlePath, "Image path copied");
            pendingTitlePath = null;
        };
        Viewer.PreviewMouseDown += PanDown;
        Viewer.PreviewMouseMove += PanMove;
        Viewer.PreviewMouseUp += PanUp;
    }
    private void Toast(string message)
    { ToastText.Text = message; ToastBox.Visibility = Visibility.Visible; toastTimer.Stop(); toastTimer.Start(); }
    private void Error(Exception ex) => MessageBox.Show(this, ex.Message, "Snapview", MessageBoxButton.OK, MessageBoxImage.Warning);
    private async Task Work(Func<Task> action)
    {
        if (busy) return;
        busy = true; Mouse.OverrideCursor = Cursors.Wait; EditTools.IsEnabled = false; SaveButton.IsEnabled = false;
        Annotations.CancelInteraction(); Annotations.FinishTextEditing(); Annotations.IsEnabled = false;
        try { await action(); }
        catch (Exception ex) { Error(ex); }
        finally { busy = false; Mouse.OverrideCursor = null; Refresh(); }
    }
    private void Refresh(bool resetFit = false)
    {
        var image = document.BaseImage;
        bool loaded = image != null;
        EditTools.IsEnabled = ZoomTools.IsEnabled = SaveButton.IsEnabled = InfoButton.IsEnabled = loaded && !busy;
        UndoButton.IsEnabled = (document.CanUndo || renameHistory.CanUndo(document)) && !busy;
        RedoButton.IsEnabled = (document.CanRedo || renameHistory.CanRedo(document)) && !busy;
        CopyPathButton.IsEnabled = document.Path != null && !busy;
        DeleteImageButton.IsEnabled = document.Path != null && loaded && !busy;
        CopyPathButton.ToolTip = !loaded ? "Open an image to copy or rename its file" : document.Path == null ? "No file path for a clipboard image" : document.Path + "\nClick to copy path · double-click to rename";
        Annotations.IsEnabled = !busy;
        Viewer.Visibility = loaded ? Visibility.Visible : Visibility.Collapsed;
        Welcome.Visibility = loaded ? Visibility.Collapsed : Visibility.Visible;
        if (image == null)
        {
            Photo.Source = null; Annotations.RefreshScene();
            Title = "Snapview"; TitleLabel.Text = "A clearer view"; FileStatus.Text = "Just you and your images";
            PositionLabel.Text = "LOCAL. SIMPLE. YOURS."; ZoomLabel.Content = "100%";
            PrevButton.Visibility = NextButton.Visibility = Visibility.Collapsed;
            return;
        }
        Photo.Source = image;
        ImageHost.Width = Overlay.Width = Photo.Width = image.PixelWidth;
        ImageHost.Height = Overlay.Height = Photo.Height = image.PixelHeight;
        Annotations.Width = image.PixelWidth; Annotations.Height = image.PixelHeight;
        Annotations.RefreshScene();
        AnnotationSelectionChanged();
        var name = document.Path == null ? "Clipboard image" : System.IO.Path.GetFileName(document.Path);
        TitleLabel.Text = name + (document.Dirty ? "  ·  Edited" : "");
        Title = $"{name}{(document.Dirty ? " •" : "")} — Snapview";
        string fileSize = "Unsaved";
        try { if (document.Path != null) fileSize = Imaging.SizeText(new FileInfo(document.Path).Length); } catch (IOException) { }
        string format = document.Path == null ? "IMAGE" : System.IO.Path.GetExtension(document.Path).TrimStart('.').ToUpperInvariant();
        FileStatus.Text = $"{image.PixelWidth:N0} × {image.PixelHeight:N0}  ·  {format}  ·  {fileSize}";
        if (resetFit) fit = true;
        if (fit) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Fit());
        int index = Array.FindIndex(siblings, x => string.Equals(x, document.Path, StringComparison.OrdinalIgnoreCase));
        PositionLabel.Text = index >= 0 ? $"{index + 1} / {siblings.Length}" : "";
        PrevButton.Visibility = NextButton.Visibility = siblings.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
        PrevButton.IsEnabled = index > 0; NextButton.IsEnabled = index >= 0 && index < siblings.Length - 1;
        if (InfoPanel.Visibility == Visibility.Visible) PopulateInfo();
    }
    private bool ConfirmLeave()
    {
        if (!document.Dirty) return true;
        var answer = MessageBox.Show(this, "Save your changes before leaving this image?\n\nYes saves your edits. No discards unsaved changes.", "Unsaved changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel) return false;
        if (answer == MessageBoxResult.No) return true;
        // Saving is synchronous here so closing can reliably honor cancellation or failure.
        return SaveInteractive(document.Path == null || !CanOverwrite(document.Path));
    }
    private static bool CanOverwrite(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".tif" or ".tiff";

    public async void OpenPath(string path)
    {
        if (busy || !ConfirmLeave()) return;
        await LoadPath(path);
    }
    private async Task LoadPath(string path)
    {
        await Work(async () =>
        {
            var fullPath = System.IO.Path.GetFullPath(path);
            var image = await Task.Run(() => Imaging.Load(fullPath));
            document.Load(image, fullPath); renameHistory.Clear(); metadata = null; metadataRevision++;
            SetTool("View"); MarkupBar.Visibility = Visibility.Collapsed; Overlay.Children.Clear();
            var directory = System.IO.Path.GetDirectoryName(fullPath)!;
            if (!string.Equals(folder, directory, StringComparison.OrdinalIgnoreCase))
            {
                siblings = await Task.Run(() =>
                {
                    try { return System.IO.Directory.EnumerateFiles(directory).Where(x => Imaging.Extensions.Contains(System.IO.Path.GetExtension(x).ToLowerInvariant())).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new[] { fullPath }; }
                });
                folder = directory;
            }
            Refresh(true);
            var ext = System.IO.Path.GetExtension(fullPath).ToLowerInvariant();
            if (ext is ".gif" or ".tif" or ".tiff" or ".ico") Toast("Showing the first frame. Saving exports this frame as a still image.");
        });
        if (InfoPanel.Visibility == Visibility.Visible) await ReadMetadata();
    }
    private void OpenClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new OpenFileDialog { Filter = Imaging.OpenFilter, Title = "Open an image" };
        if (dialog.ShowDialog(this) == true) OpenPath(dialog.FileName);
    }
    private void WindowDragOver(object sender, DragEventArgs e)
    { e.Effects = !busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private void WindowDrop(object sender, DragEventArgs e)
    { if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) OpenPath(files[0]); }
    private void WindowClosing(object? sender, CancelEventArgs e) { titleClickTimer.Stop(); if (!FinishRename() || busy || !ConfirmLeave()) e.Cancel = true; }
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private void SetZoom(double value)
    {
        if (document.BaseImage == null) return;
        double old = Scale;
        value = Math.Clamp(value, 0.01, 16);
        double centerX = (Viewer.HorizontalOffset + Viewer.ViewportWidth / 2) / old;
        double centerY = (Viewer.VerticalOffset + Viewer.ViewportHeight / 2) / old;
        ImageScale.ScaleX = ImageScale.ScaleY = value;
        Annotations.SetZoom(value);
        if (preview != null) { preview.Zoom = value; preview.InvalidateVisual(); }
        ZoomLabel.Content = $"{value * 100:0.#}%";
        Viewer.UpdateLayout();
        Viewer.ScrollToHorizontalOffset(centerX * value - Viewer.ViewportWidth / 2);
        Viewer.ScrollToVerticalOffset(centerY * value - Viewer.ViewportHeight / 2);
    }
    private void Fit()
    {
        if (document.BaseImage == null || Viewer.ActualWidth < 70 || Viewer.ActualHeight < 70) return;
        double width = Math.Max(1, Viewer.ActualWidth - 78), height = Math.Max(1, Viewer.ActualHeight - 78);
        SetZoom(Math.Min(1, Math.Min(width / document.BaseImage!.PixelWidth, height / document.BaseImage!.PixelHeight)));
        Viewer.ScrollToHorizontalOffset(0); Viewer.ScrollToVerticalOffset(0);
    }
    private void FitClick(object sender, RoutedEventArgs e) { fit = true; Fit(); }
    private void ActualClick(object sender, RoutedEventArgs e) { fit = false; SetZoom(1); }
    private void ZoomInClick(object sender, RoutedEventArgs e) { fit = false; SetZoom(Scale * 1.25); }
    private void ZoomOutClick(object sender, RoutedEventArgs e) { fit = false; SetZoom(Scale / 1.25); }
    private void ViewerSizeChanged(object sender, SizeChangedEventArgs e) { if (fit) Fit(); }
    private void ViewerScrolled(object sender, ScrollChangedEventArgs e) { if (fit && (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0)) Fit(); }
    private void ViewerMouseWheel(object sender, MouseWheelEventArgs e)
    { if (document.BaseImage != null) { fit = false; SetZoom(Scale * (e.Delta > 0 ? 1.12 : 1 / 1.12)); e.Handled = true; } }
    private void PanDown(object sender, MouseButtonEventArgs e)
    {
        if (document.BaseImage == null || busy) return;
        if (e.ChangedButton == MouseButton.Left && !Keyboard.IsKeyDown(Key.Space) && Annotations.WillHandle(e.GetPosition(Annotations))) return;
        if (e.ChangedButton == MouseButton.Middle || (e.ChangedButton == MouseButton.Left && (tool == "View" || Keyboard.IsKeyDown(Key.Space))))
        { panning = true; panStart = e.GetPosition(Viewer); panX = Viewer.HorizontalOffset; panY = Viewer.VerticalOffset; Viewer.CaptureMouse(); Viewer.Cursor = Cursors.ScrollAll; e.Handled = true; }
    }
    private void PanMove(object sender, MouseEventArgs e)
    { if (panning) { var p = e.GetPosition(Viewer); Viewer.ScrollToHorizontalOffset(panX - p.X + panStart.X); Viewer.ScrollToVerticalOffset(panY - p.Y + panStart.Y); e.Handled = true; } }
    private void PanUp(object sender, MouseButtonEventArgs e)
    { if (panning) { panning = false; Viewer.ReleaseMouseCapture(); Viewer.Cursor = Cursors.Arrow; e.Handled = true; } }

    private void Commit(BitmapSource bitmap, bool resetFit = false)
    { Overlay.Children.Clear(); crop = null; renameHistory.ClearRedo(); document.Commit(bitmap); Refresh(resetFit); }
    private void UndoClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        Annotations.CancelInteraction(); Annotations.FinishTextEditing(); CancelGesture();
        if (renameHistory.CanUndo(document)) UndoRename(false); else { document.Undo(); Refresh(); }
    }
    private void RedoClick(object sender, RoutedEventArgs e) => Redo();
    private void Redo()
    {
        if (busy) return;
        Annotations.CancelInteraction(); Annotations.FinishTextEditing(); CancelGesture();
        if (renameHistory.CanRedo(document)) UndoRename(true); else { document.Redo(); Refresh(); }
    }
    private void RotateClick(object sender, RoutedEventArgs e)
    { if (document.BaseImage != null && !busy) { Annotations.FinishTextEditing(); CancelGesture(); Commit(Imaging.Normalize(Imaging.Orient(document.Current!, 6)), true); } }
    private void ResizeClick(object sender, RoutedEventArgs e)
    {
        if (document.BaseImage == null || busy) return;
        if (Dialogs.Resize(this, document.BaseImage!.PixelWidth, document.BaseImage!.PixelHeight) is { } size)
        { Annotations.FinishTextEditing(); CancelGesture(); Commit(Imaging.Resize(document.Current!, size.Width, size.Height), true); }
    }
    private void SetTool(string value)
    {
        CancelGesture(); tool = value;
        Annotations.SetTool(value);
        Annotations.Ink = ink; Annotations.StrokeWidth = InkWidth;
        Overlay.Visibility = value == "Crop" ? Visibility.Visible : Visibility.Collapsed;
        AnnotationHint.Visibility = value == "Crop" ? Visibility.Collapsed : Visibility.Visible;
        AddTextButton.SetResourceReference(BackgroundProperty, value == "Text" ? "Selected" : "TransparentBrush");
        Overlay.Cursor = value == "View" ? Cursors.Arrow : value == "Text" ? Cursors.IBeam : Cursors.Cross;
        DrawingTools.Visibility = value == "Crop" ? Visibility.Collapsed : Visibility.Visible;
        CropHint.Visibility = ApplyCropButton.Visibility = value == "Crop" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in DrawingTools.Children.OfType<Button>().Where(b => b.Tag is string s && !s.StartsWith('#')))
            button.SetResourceReference(BackgroundProperty, (string)button.Tag == value ? "Selected" : "TransparentBrush");
        AnnotationSelectionChanged();
    }
    private void CropClick(object sender, RoutedEventArgs e)
    { if (document.BaseImage != null && !busy) { MarkupBar.Visibility = Visibility.Visible; SetTool("Crop"); } }
    private void MarkupClick(object sender, RoutedEventArgs e)
    {
        if (document.BaseImage == null || busy) return;
        if (MarkupBar.Visibility == Visibility.Visible && tool != "Crop") { DoneClick(sender, e); return; }
        MarkupBar.Visibility = Visibility.Visible; SetTool("Select"); Toast("Draw a shape, or select an existing object to move and resize it.");
    }
    private void DoneClick(object sender, RoutedEventArgs e) { SetTool("View"); MarkupBar.Visibility = Visibility.Collapsed; }
    private void TextClick(object sender, RoutedEventArgs e)
    {
        if (document.BaseImage == null || busy) return;
        MarkupBar.Visibility = Visibility.Visible;
        SetTool("Text");
        Toast("Click the image and type. Drag side handles to wrap; corners stretch the text.");
    }
    private void ToolClick(object sender, RoutedEventArgs e)
    {
        if ((string)((Button)sender).Tag == "Text") TextClick(sender, e);
        else if (!busy) SetTool((string)((Button)sender).Tag);
    }
    private void ColorClick(object sender, RoutedEventArgs e)
    {
        if (busy) { SyncInkSwatches(); return; }
        ink = (Color)ColorConverter.ConvertFromString((string)((RadioButton)sender).Tag);
        Annotations.ChangeColor(ink); SyncInkSwatches();
    }
    private void SyncInkSwatches()
    {
        foreach (var swatch in DrawingTools.Children.OfType<RadioButton>())
            swatch.IsChecked = (Color)ColorConverter.ConvertFromString((string)swatch.Tag) == ink;
    }
    private void AnnotationSelectionChanged()
    {
        if (FontSizeInput == null) return;
        syncingAnnotationControls = true;
        var selected = Annotations.Selected;
        bool text = selected?.Kind == "Text" || (selected == null && tool == "Text");
        FontLabel.Visibility = FontSizeInput.Visibility = text ? Visibility.Visible : Visibility.Collapsed;
        BoldButton.Visibility = ItalicButton.Visibility = text ? Visibility.Visible : Visibility.Collapsed;
        var textStyle = Annotations.TextSelectionStyle;
        BoldButton.IsChecked = textStyle?.Bold ?? selected?.Bold ?? false;
        ItalicButton.IsChecked = textStyle?.Italic ?? selected?.Italic ?? false;
        StrokeLabel.Visibility = StrokeSize.Visibility = text ? Visibility.Collapsed : Visibility.Visible;
        DeleteAnnotationButton.IsEnabled = selected != null;
        if (selected != null)
        {
            if (tool == "View") { tool = "Select"; Annotations.SetTool("Select"); }
            MarkupBar.Visibility = Visibility.Visible;
            var style = Annotations.TextSelectionStyle;
            ink = style?.Color ?? selected.Color; Annotations.Ink = ink;
            if (text && !FontSizeInput.IsKeyboardFocusWithin) FontSizeInput.Text = (style?.FontSize ?? selected.FontSize).ToString("0.#", CultureInfo.CurrentCulture);
            else if (!text) foreach (ComboBoxItem item in StrokeSize.Items) if (item.Content.ToString() == selected.Stroke.ToString(CultureInfo.InvariantCulture)) StrokeSize.SelectedItem = item;
        }
        AnnotationHint.Text = text ? "Select words to change their color or size · no text selection styles the whole box · drag Move to reposition"
            : selected?.Kind == "Arrow" ? "Drag either end handle to shorten or redirect the arrow. Drag the line or Move handle to reposition."
            : "Click an object to select it. Drag to move; use handles to resize. Delete removes it; Ctrl+Z undoes.";
        SyncInkSwatches(); syncingAnnotationControls = false;
    }
    private void StrokeChanged(object sender, SelectionChangedEventArgs e)
    { if (!syncingAnnotationControls && Annotations?.Document != null) Annotations.ChangeStroke(InkWidth); }
    private void FontSizeChanged(object sender, KeyboardFocusChangedEventArgs e) => ApplyFontSize();
    private void BoldClick(object sender, RoutedEventArgs e) { if (!busy) Annotations.ChangeEmphasis(bold: BoldButton.IsChecked == true); }
    private void ItalicClick(object sender, RoutedEventArgs e) { if (!busy) Annotations.ChangeEmphasis(italic: ItalicButton.IsChecked == true); }
    private void FontSizeKeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter) { ApplyFontSize(); e.Handled = true; } }
    private void ApplyFontSize()
    {
        if (syncingAnnotationControls || busy) return;
        if (double.TryParse(FontSizeInput.Text, out double value) && double.IsFinite(value) && value is >= 8 and <= 1000) Annotations.ChangeFont(value);
        else Toast("Choose a text size between 8 and 1,000 pixels.");
    }
    private void DeleteAnnotationClick(object sender, RoutedEventArgs e) { if (!busy) Annotations.DeleteSelected(); }
    private Point ImagePoint(MouseEventArgs e)
    { var p = e.GetPosition(Overlay); return new Point(Math.Clamp(p.X, 0, Overlay.Width), Math.Clamp(p.Y, 0, Overlay.Height)); }
    private void CanvasDown(object sender, MouseButtonEventArgs e)
    {
        if (document.BaseImage == null || busy || tool != "Crop") return;
        start = ImagePoint(e); crop = null; Overlay.Children.Clear(); drawing = true;
        preview = new CropPreview { Width = Overlay.Width, Height = Overlay.Height, Zoom = Scale, IsHitTestVisible = false };
        Overlay.Children.Add(preview); Overlay.CaptureMouse(); e.Handled = true;
    }
    private void CanvasMove(object sender, MouseEventArgs e)
    {
        if (!drawing || preview == null) return;
        var rect = new Rect(start, ImagePoint(e)); crop = rect;
        preview.Selection = rect; preview.InvalidateVisual();
    }
    private void CanvasUp(object sender, MouseButtonEventArgs e)
    {
        if (!drawing) return;
        CanvasMove(sender, e); drawing = false; Overlay.ReleaseMouseCapture();
    }
    private void CanvasLostCapture(object sender, MouseEventArgs e) { if (drawing) CancelGesture(); }
    private void CancelGesture()
    { drawing = false; preview = null; crop = null; Overlay.Children.Clear(); if (Overlay.IsMouseCaptured) Overlay.ReleaseMouseCapture(); }
    private void ApplyCropClick(object sender, RoutedEventArgs e)
    {
        if (busy || document.BaseImage == null) return;
        if (crop is not Rect rect || rect.Width < 1 || rect.Height < 1) { Toast("Drag on the image to select a crop first."); return; }
        var bounds = CropPreview.PixelBounds(rect, document.BaseImage.PixelWidth, document.BaseImage.PixelHeight);
        var result = new CroppedBitmap(document.Current, bounds); result.Freeze();
        Commit(Imaging.Normalize(result), true); DoneClick(sender, e); Toast("Cropped. Ctrl+Z to undo.");
    }

    private bool SaveInteractive(bool copy)
    {
        if (document.BaseImage == null) return false;
        Annotations.FinishTextEditing();
        string? path = document.Path;
        if (copy || path == null || !CanOverwrite(path))
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save image · exports omit camera and location metadata",
                Filter = "PNG image|*.png|JPEG image|*.jpg|WebP image|*.webp|Bitmap image|*.bmp|TIFF image|*.tif",
                DefaultExt = ".png", AddExtension = true, OverwritePrompt = true,
                FileName = path == null ? "Image" : System.IO.Path.GetFileNameWithoutExtension(path) + " - edited",
                InitialDirectory = path == null ? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) : System.IO.Path.GetDirectoryName(path)
            };
            if (dialog.ShowDialog(this) != true) return false;
            path = dialog.FileName;
        }
        try
        {
            bool recoveryIndexed = true;
            string? backup = File.Exists(path) ? Originals.Capture(path) : null;
            Imaging.Save(document.Current!, path);
            if (backup != null)
            {
                try { Originals.Record(path, backup); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { recoveryIndexed = false; }
            }
            if (document.Path != path) renameHistory.Clear();
            document.MarkSaved(path); metadata = null; metadataRevision++;
            if (!siblings.Contains(path, StringComparer.OrdinalIgnoreCase)) { siblings = [path]; folder = null; }
            Refresh();
            if (InfoPanel.Visibility == Visibility.Visible) _ = ReadMetadata();
            Toast(recoveryIndexed ? "Saved · camera and location metadata omitted" : "Saved. The original recovery copy exists, but automatic Revert could not be indexed."); return true;
        }
        catch (Exception ex) { Error(ex); return false; }
    }
    private void SaveCopyClick(object sender, RoutedEventArgs e) { if (!busy) SaveInteractive(true); }
    private void SaveClick(object sender, RoutedEventArgs e) { if (!busy) SaveInteractive(false); }
    private async void RevertClick(object sender, RoutedEventArgs e)
    {
        if (document.BaseImage == null || busy) return;
        await Work(async () =>
        {
            var original = document.Original!;
            if (document.Path != null && await Task.Run(() => Originals.Find(document.Path)) is string backup)
                original = await Task.Run(() => Imaging.Load(backup));
            CancelGesture(); Commit(original, true); Toast("Original restored to the canvas. Save to keep it, or undo to return to your edits.");
        });
    }
    private async void OcrClick(object sender, RoutedEventArgs e)
    {
        if (document.BaseImage == null || busy) return;
        string? text = null;
        // Keep OCR off the viewing path, and downsample very large images for bounded work.
        var image = document.Current!;
        if (Math.Max(image.PixelWidth, image.PixelHeight) > 4000)
        { double ratio = 4000d / Math.Max(image.PixelWidth, image.PixelHeight); image = Imaging.Resize(image, Math.Max(1, (int)(image.PixelWidth * ratio)), Math.Max(1, (int)(image.PixelHeight * ratio))); }
        var bytes = Imaging.Png(image);
        var originalContent = OcrButton.Content;
        Toast("Reading text locally…"); OcrButton.Content = "Reading…";
        await Work(async () => text = await Task.Run(() =>
        {
            string data = System.IO.Path.Combine(AppContext.BaseDirectory, "tessdata");
            if (!File.Exists(System.IO.Path.Combine(data, "eng.traineddata"))) throw new InvalidOperationException("The English OCR model is missing. Run scripts/setup.ps1 and rebuild Snapview.");
            using var engine = new Tesseract.TesseractEngine(data, "eng", Tesseract.EngineMode.LstmOnly);
            using var pix = Tesseract.Pix.LoadFromMemory(bytes);
            using var page = engine.Process(pix);
            return page.GetText().Trim();
        }));
        OcrButton.Content = originalContent;
        if (text != null)
        {
            ToastBox.Visibility = Visibility.Collapsed;
            if (text.Length == 0) Toast("No readable text found. Try a larger or sharper image.");
            else Dialogs.Ocr(this, text);
        }
    }
    private async void InfoClick(object sender, RoutedEventArgs e)
    {
        if (document.BaseImage == null) return;
        InfoPanel.Visibility = InfoPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (InfoPanel.Visibility == Visibility.Visible) { PopulateInfo(); await ReadMetadata(); }
    }
    private async Task ReadMetadata()
    {
        if (metadata != null || document.Path == null) return;
        string path = document.Path; long revision = metadataRevision;
        var result = await Task.Run(() => MetadataReader.Read(path));
        if (revision == metadataRevision && path == document.Path) { metadata = result; PopulateInfo(); }
    }
    private void AddInfo(string title, string value)
    {
        var heading = new TextBlock { Text = title.ToUpperInvariant(), FontSize = 10, Margin = new Thickness(0, 17, 0, 5) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); InfoContents.Children.Add(heading);
        InfoContents.Children.Add(new TextBlock { Text = value, FontSize = 13, TextWrapping = TextWrapping.Wrap, IsHitTestVisible = true, ToolTip = value });
    }
    private void CopyPathClick(object sender, RoutedEventArgs e)
    { if (!busy && document.Path is string path) CopyText(path, "Image path copied"); }
    private void TitleClick(object sender, RoutedEventArgs e)
    {
        if (busy || document.Path == null) return;
        pendingTitlePath = document.Path; titleClickTimer.Stop(); titleClickTimer.Start();
    }
    private void TitleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        titleClickTimer.Stop(); pendingTitlePath = null; e.Handled = true;
        RenameImage();
    }
    private void RenameImage()
    {
        if (busy || document.Path is not string path) return;
        Annotations.FinishTextEditing();
        renamePath = path;
        RenameInput.IsUndoEnabled = false;
        RenameInput.Text = System.IO.Path.GetFileNameWithoutExtension(path);
        RenameInput.IsUndoEnabled = true;
        RenameInput.Width = Math.Clamp(TitleLabel.ActualWidth + 24, 160, 340);
        RenameExtension.Text = System.IO.Path.GetExtension(path);
        CopyPathButton.Visibility = Visibility.Collapsed; RenameHost.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (renamePath != null) { RenameInput.Focus(); RenameInput.SelectAll(); }
        });
    }
    private void EndRename()
    {
        renamePath = null; RenameHost.Visibility = Visibility.Collapsed; CopyPathButton.Visibility = Visibility.Visible;
    }
    private bool FinishRename()
    {
        if (renamePath is not string before || finishingRename) return true;
        finishingRename = true;
        try
        {
            if (document.Path != before || RenameInput.Text == System.IO.Path.GetFileNameWithoutExtension(before)) { EndRename(); return true; }
            var renamed = FileActions.Rename(before, RenameInput.Text);
            document.Relocate(renamed.Path); renameHistory.Record(before, document);
            EndRename(); RenamedPath(before);
            Toast(renamed.RecoveryPreserved ? "Image renamed · Ctrl+Z to undo" : "Image renamed. The recovery copy exists, but its new path could not be indexed.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { Toast(ex.Message); return false; }
        finally { finishingRename = false; }
    }
    private void RenamedPath(string before)
    {
        siblings = siblings.Select(x => string.Equals(x, before, StringComparison.OrdinalIgnoreCase) ? document.Path! : x).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        metadataRevision++; Refresh();
    }
    private void UndoRename(bool forward)
    {
        string before = document.Path!;
        try
        {
            bool recovery = forward ? renameHistory.Redo(document) : renameHistory.Undo(document);
            RenamedPath(before);
            Toast(recovery ? forward ? "Rename redone" : "Rename undone" : "Name restored. The recovery copy could not be indexed at its new path.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { Toast(ex.Message); }
    }
    private void RenameLostFocus(object sender, KeyboardFocusChangedEventArgs e) => FinishRename();
    private void WindowDeactivated(object? sender, EventArgs e) => FinishRename();
    private void WindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (renamePath == null || e.OriginalSource is not DependencyObject source || source == RenameHost || RenameHost.IsAncestorOf(source)) return;
        if (!FinishRename()) { e.Handled = true; RenameInput.Focus(); }
    }
    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        // Caption clicks are native non-client events and do not reach PreviewMouseDown.
        if (message == 0x00A1 && !FinishRename()) handled = true;
        return 0;
    }
    private void DeleteImageClick(object sender, RoutedEventArgs e)
    {
        if (busy || document.Path is not string path) return;
        titleClickTimer.Stop(); pendingTitlePath = null;
        bool skip = FileActions.SkipDeleteConfirmation;
        if (!skip)
        {
            var answer = Dialogs.DeleteImage(this, path, document.Dirty);
            if (!answer.Confirmed) return;
            skip = answer.Skip;
        }
        try
        {
            Annotations.CancelInteraction(); Annotations.FinishTextEditing();
            FileActions.DeleteDocument(document);
            renameHistory.Clear();
            metadata = null; metadataRevision++; siblings = []; folder = null;
            SetTool("View"); MarkupBar.Visibility = InfoPanel.Visibility = Visibility.Collapsed;
            Refresh(); Toast("Image moved to the Recycle Bin");
            if (skip != FileActions.SkipDeleteConfirmation)
            {
                try { FileActions.RememberDeleteConfirmation(skip); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Toast("Image deleted. The confirmation preference could not be saved."); }
            }
        }
        catch (Exception ex) { Error(ex); }
    }
    private void CopyText(string text, string message)
    { try { Clipboard.SetText(text); Toast(message); } catch (Exception ex) { Error(ex); } }
    private void PopulateInfo()
    {
        InfoContents.Children.Clear(); var image = document.BaseImage; if (image == null) return;
        AddInfo("File name", document.Path == null ? "Clipboard image" : System.IO.Path.GetFileName(document.Path));
        AddInfo("Resolution · current canvas", $"{image.PixelWidth:N0} × {image.PixelHeight:N0} pixels");
        AddInfo("Megapixels", $"{image.PixelWidth * (double)image.PixelHeight / 1_000_000:0.##} MP");
        if (document.Path != null)
        {
            try
            {
                var file = new FileInfo(document.Path);
                AddInfo("Format · on disk", file.Extension.TrimStart('.').ToUpperInvariant());
                AddInfo("File size · on disk", Imaging.SizeText(file.Length));
                AddInfo("Modified", file.LastWriteTime.ToString("dd MMM yyyy, HH:mm"));
            }
            catch (IOException) { AddInfo("File", "Source file is no longer available"); }
            AddInfo("Location on disk", document.Path);
            var copyPath = new Button { Content = "Copy image path", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0), ToolTip = "Copy the full file path to the clipboard" };
            copyPath.SetResourceReference(BackgroundProperty, "SoftButton"); copyPath.Click += CopyPathClick;
            InfoContents.Children.Add(copyPath);
        }
        if (document.Dirty) AddInfo("Edits", "Unsaved changes. File size updates after saving.");
        if (metadata != null)
        {
            foreach (var (label, value) in metadata.Values.Distinct()) AddInfo(label, value);
            if (metadata.Latitude is double lat && metadata.Longitude is double lon)
            {
                AddInfo("Photo location · source file", $"{lat:0.00000}, {lon:0.00000}");
                var button = new Button { Content = "View on map ↗", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
                button.SetResourceReference(BackgroundProperty, "SoftButton");
                button.ToolTip = "Open OpenStreetMap in your browser with these coordinates";
                button.Click += (_, _) =>
                {
                    try { Process.Start(new ProcessStartInfo(FormattableString.Invariant($"https://www.openstreetmap.org/?mlat={lat}&mlon={lon}#map=15/{lat}/{lon}")) { UseShellExecute = true }); } catch (Exception ex) { Error(ex); }
                };
                InfoContents.Children.Add(button);
                var copyLocation = new Button { Content = "Copy coordinates", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
                copyLocation.SetResourceReference(BackgroundProperty, "SoftButton");
                copyLocation.Click += (_, _) => CopyText(FormattableString.Invariant($"{lat:0.00000}, {lon:0.00000}"), "Photo coordinates copied");
                InfoContents.Children.Add(copyLocation);
            }
            else AddInfo("Photo location", "No location data in this file");
        }
        else if (document.Path != null) AddInfo("Camera metadata", "Open this panel again to load camera details.");
        AddInfo("About exports", "Saved images contain your visible edits. Camera and GPS metadata is omitted. Transparent areas become white in JPEG and BMP.");
    }
    private void MoreClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        var appearance = new MenuItem { Header = "Appearance" };
        foreach (bool dark in new[] { false, true })
        {
            var item = new MenuItem { Header = dark ? "Dark" : "Light", IsCheckable = true, IsChecked = Theme.IsDark == dark };
            item.Click += (_, _) => { try { Theme.Apply(dark, save: true); } catch (Exception ex) { Error(ex); } };
            appearance.Items.Add(item);
        }
        menu.Items.Add(appearance); menu.Items.Add(new Separator());
        var confirmDelete = new MenuItem { Header = "Confirm before deleting images", IsCheckable = true, IsChecked = !FileActions.SkipDeleteConfirmation };
        confirmDelete.Click += (_, _) => { try { FileActions.RememberDeleteConfirmation(confirmDelete.IsChecked == false); } catch (Exception ex) { Error(ex); } };
        menu.Items.Add(confirmDelete); menu.Items.Add(new Separator());
        void Item(string title, RoutedEventHandler handler, bool enabled = true)
        { var item = new MenuItem { Header = title, IsEnabled = enabled }; item.Click += handler; menu.Items.Add(item); }
        Item("Save                         Ctrl+S", SaveClick, document.BaseImage != null && !busy);
        Item("Save copy / convert…    Ctrl+Shift+S", SaveCopyClick, document.BaseImage != null && !busy);
        Item("Revert to original", RevertClick, document.BaseImage != null && !busy);
        Item("Redo                         Ctrl+Y", (_, _) => Redo(), (document.CanRedo || renameHistory.CanRedo(document)) && !busy);
        menu.Items.Add(new Separator());
        Item("Paste image                Ctrl+V", (_, _) => Paste(), !busy);
        Item("Copy image                 Ctrl+C", (_, _) => CopyImage(), document.BaseImage != null && !busy);
        menu.Items.Add(new Separator());
        Item("Set as default viewer…", (_, _) => DefaultViewer());
        Item("Keyboard shortcuts", (_, _) => MessageBox.Show(this,
            "Ctrl+O  Open image\nCtrl+V  Paste image\nCtrl+C  Copy edited image\nCtrl+S  Save\nCtrl+Shift+S  Save copy / convert\nCtrl+Z / Ctrl+Y  Undo / redo\nCtrl+0 / Ctrl+1  Fit / actual size\n+ / − or mouse wheel  Zoom\nDrag / middle drag / Space+drag  Pan\n← / →  Previous / next in folder\nR  Rotate clockwise\nC  Crop · Enter to apply · Esc to cancel\nM  Markup · T to add text\nCtrl+I  Image details\nCtrl+Shift+C  Extract text\nF11  Toggle maximized view", "Keyboard shortcuts"));
        menu.PlacementTarget = (Button)sender; menu.IsOpen = true;
    }
    private void DefaultViewer()
    {
        try
        {
            Associations.Register(Environment.ProcessPath!);
            Process.Start(new ProcessStartInfo("ms-settings:defaultapps?registeredAppUser=Snapview") { UseShellExecute = true });
            Toast("Snapview is registered. Choose it for your image types in Windows Settings.");
        }
        catch (Exception ex) { Error(ex); }
    }
    private void Paste()
    {
        if (busy) return;
        try
        {
            if (!Clipboard.ContainsImage()) { Toast("Copy an image first, then paste it here."); return; }
            if (!ConfirmLeave()) return;
            var image = Clipboard.GetImage(); if (image == null) return;
            Imaging.CheckSize(image.PixelWidth, image.PixelHeight); document.Load(Imaging.Normalize(image), null); renameHistory.Clear();
            metadata = null; metadataRevision++; siblings = []; folder = null; SetTool("View"); MarkupBar.Visibility = Visibility.Collapsed; Refresh(true);
        }
        catch (Exception ex) { Error(ex); }
    }
    private void CopyImage()
    { try { if (document.BaseImage != null) { Clipboard.SetImage(document.Current!); Toast("Image copied"); } } catch (Exception ex) { Error(ex); } }
    private void PrevClick(object sender, RoutedEventArgs e) => Navigate(-1);
    private void NextClick(object sender, RoutedEventArgs e) => Navigate(1);
    private void Navigate(int step)
    {
        if (busy) return;
        int index = Array.FindIndex(siblings, x => string.Equals(x, document.Path, StringComparison.OrdinalIgnoreCase));
        if (index >= 0 && index + step >= 0 && index + step < siblings.Length) OpenPath(siblings[index + step]);
    }
    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (busy) return;
        if (renamePath != null && RenameInput.IsKeyboardFocusWithin)
        {
            if (e.Key == Key.Escape) { EndRename(); Keyboard.ClearFocus(); e.Handled = true; }
            else if (e.Key == Key.Enter) { if (FinishRename()) Keyboard.ClearFocus(); e.Handled = true; }
            return; // TextBox handles Ctrl+Z while the file name is still a draft.
        }
        if (e.OriginalSource is TextBox || Annotations.TextInputHasFocus)
        {
            if (Annotations.TextInputHasFocus && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key is Key.B or Key.I)
            {
                var style = Annotations.TextSelectionStyle;
                if (e.Key == Key.B) Annotations.ChangeEmphasis(bold: !(style?.Bold ?? false));
                else Annotations.ChangeEmphasis(italic: !(style?.Italic ?? false));
                e.Handled = true; return;
            }
            if (Annotations.TextInputHasFocus && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key is Key.Z or Key.Y)
            { if (e.Key == Key.Y || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) Redo(); else UndoClick(this, e); e.Handled = true; return; }
            if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            { Annotations.FinishTextEditing(); if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) SaveCopyClick(this, e); else SaveClick(this, e); e.Handled = true; }
            return;
        }
        if (Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift && Annotations.HasSelection && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            var delta = e.Key switch { Key.Left => new Vector(-step, 0), Key.Right => new Vector(step, 0), Key.Up => new Vector(0, -step), _ => new Vector(0, step) };
            Annotations.Nudge(delta); e.Handled = true; return;
        }
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.O: OpenClick(this, e); break;
                case Key.S: if (shift) SaveCopyClick(this, e); else SaveClick(this, e); break;
                case Key.Z: if (shift) Redo(); else UndoClick(this, e); break;
                case Key.Y: Redo(); break;
                case Key.I: InfoClick(this, e); break;
                case Key.B: if (Annotations.Selected is not { Kind: "Text" } selected) return; Annotations.ChangeEmphasis(bold: !selected.Bold); break;
                case Key.D0: FitClick(this, e); break;
                case Key.D1: ActualClick(this, e); break;
                case Key.V: Paste(); break;
                case Key.C: if (shift) OcrClick(this, e); else CopyImage(); break;
                default: return;
            }
        }
        else switch (e.Key)
        {
            case Key.Delete: case Key.Back: if (!Annotations.DeleteSelected()) return; break;
            case Key.F2: Annotations.EditSelectedText(); break;
            case Key.V: if (document.BaseImage != null) { MarkupBar.Visibility = Visibility.Visible; SetTool("Select"); } break;
            case Key.R: RotateClick(this, e); break;
            case Key.C: CropClick(this, e); break;
            case Key.M: MarkupClick(this, e); break;
            case Key.T: TextClick(this, e); break;
            case Key.Add: case Key.OemPlus: ZoomInClick(this, e); break;
            case Key.Subtract: case Key.OemMinus: ZoomOutClick(this, e); break;
            case Key.Left: Navigate(-1); break;
            case Key.Right: Navigate(1); break;
            case Key.Enter: if (tool == "Crop") ApplyCropClick(this, e); else return; break;
            case Key.Escape: DoneClick(this, e); break;
            case Key.F11: WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; break;
            default: return;
        }
        e.Handled = true;
    }
}
