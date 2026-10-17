using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Snapview;

// Runs the production image pipeline on an STA thread: Snapview.exe --self-test.
internal static class SmokeTests
{
    public static void Run()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "test-output");
        System.IO.Directory.CreateDirectory(directory);
        var results = new List<object>();
        void Check(string name, Action action)
        {
            var watch = Stopwatch.StartNew(); action(); results.Add(new { name, passed = true, milliseconds = watch.ElapsedMilliseconds });
            File.WriteAllText(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        }
        static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1200, 800));
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(79, 113, 88), Color.FromRgb(192, 207, 159), 35), null, new Rect(0, 0, 1200, 540));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(243, 211, 142)), null, new Point(920, 125), 57, 57);
            var mountain = Geometry.Parse("M0,540 L270,110 L540,430 L730,260 L1200,540 Z");
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(50, 80, 62)), null, mountain);
            dc.DrawText(new FormattedText("A QUIETER PERSPECTIVE", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 42, new SolidColorBrush(Color.FromRgb(42, 61, 47)), 1), new Point(60, 590));
            dc.DrawText(new FormattedText("SNAPVIEW LOCAL TEXT", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 32, Brushes.Black, 1), new Point(60, 670));
        }
        var image = Imaging.Render(visual, 1200, 800);
        var source = Path.Combine(directory, "sample.png");
        Check("PNG save and open", () => { Imaging.Save(image, source); var loaded = Imaging.Load(source); Assert(loaded.PixelWidth == 1200 && loaded.PixelHeight == 800, "PNG dimensions changed"); });
        Check("Resize, crop and rotate", () =>
        {
            var small = Imaging.Resize(image, 600, 400); Assert(small.PixelWidth == 600 && small.PixelHeight == 400, "Resize dimensions");
            var rotated = Imaging.Normalize(Imaging.Orient(small, 6)); Assert(rotated.PixelWidth == 400 && rotated.PixelHeight == 600, "Rotation dimensions");
            var cropped = new CroppedBitmap(rotated, new Int32Rect(10, 20, 120, 180)); Assert(cropped.PixelWidth == 120 && cropped.PixelHeight == 180, "Crop dimensions");
        });
        Check("Undo, redo and save tracking", () =>
        {
            var doc = new ImageDocument(); doc.Load(image, source); Assert(!doc.Dirty, "Fresh image dirty");
            doc.Commit(Imaging.Resize(image, 600, 400)); Assert(doc.Dirty && doc.CanUndo, "Edit not tracked");
            doc.Undo(); Assert(!doc.Dirty && doc.Current == image && doc.CanRedo, "Undo state");
            doc.Redo(); Assert(doc.Dirty && doc.Current!.PixelWidth == 600, "Redo state");
            doc.MarkSaved(source); Assert(!doc.Dirty, "Save state"); doc.Undo(); Assert(doc.Dirty, "Undo across save should be dirty");
        });
        Check("JPEG, WebP, BMP and TIFF conversion", () =>
        {
            foreach (var ext in new[] { ".jpg", ".webp", ".bmp", ".tif" })
            { var path = Path.Combine(directory, "converted" + ext); Imaging.Save(image, path); var loaded = Imaging.Load(path); Assert(loaded.PixelWidth == 1200 && loaded.PixelHeight == 800, "Conversion dimensions: " + ext); }
        });
        Check("Original backup survives overwrite and detects external changes", () =>
        {
            string path = Path.Combine(directory, "recovery.png"); Imaging.Save(image, path);
            string backup = Originals.Capture(path); Imaging.Save(Imaging.Resize(image, 300, 200), path); Originals.Record(path, backup);
            Assert(Originals.Find(path) == backup, "Recovery lookup failed"); Assert(Imaging.Load(backup).PixelWidth == 1200, "Original overwritten");
            Imaging.Save(Imaging.Resize(image, 100, 100), path); Assert(Originals.Find(path) == null, "External changes not detected");
        });
        Check("Metadata without GPS", () => { var metadata = MetadataReader.Read(source); Assert(metadata.Latitude == null && metadata.Longitude == null, "Fabricated location"); });
        Check("Offline OCR", () =>
        {
            using var engine = new Tesseract.TesseractEngine(Path.Combine(AppContext.BaseDirectory, "tessdata"), "eng", Tesseract.EngineMode.LstmOnly);
            using var pix = Tesseract.Pix.LoadFromMemory(Imaging.Png(image)); using var page = engine.Process(pix);
            var text = page.GetText(); File.WriteAllText(Path.Combine(directory, "ocr.txt"), text);
            Assert(text.Contains("SNAPVIEW", StringComparison.OrdinalIgnoreCase), "OCR did not recognize sample text");
        });
        Check("Invalid dimensions rejected", () =>
        {
            foreach (var (w, h) in new[] { (0, 50), (50000, 10), (12000, 12000) })
            { bool rejected = false; try { Imaging.CheckSize(w, h); } catch (InvalidOperationException) { rejected = true; } Assert(rejected, "Unsafe dimensions accepted"); }
        });
        Check("All eight EXIF orientation transforms", () =>
        {
            byte[] pixels = [0,0,255,255, 0,255,0,255, 255,0,0,255, 0,255,255,255, 128,0,128,255, 255,255,0,255];
            var input = BitmapSource.Create(3, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 12); input.Freeze();
            int[] firstPixels = [0, 2, 5, 3, 0, 3, 5, 2];
            for (int i = 1; i <= 8; i++)
            {
                var output = Imaging.Normalize(Imaging.Orient(input, i));
                Assert(output.PixelWidth == (i >= 5 ? 2 : 3) && output.PixelHeight == (i >= 5 ? 3 : 2), "Orientation dimensions " + i);
                byte[] first = new byte[4]; output.CopyPixels(new Int32Rect(0, 0, 1, 1), first, 4, 0);
                Assert(first.SequenceEqual(pixels.Skip(firstPixels[i - 1] * 4).Take(4)), "Orientation corner " + i);
            }
        });
        Check("High-DPI image retains pixel dimensions", () =>
        {
            var input = BitmapSource.Create(2, 2, 300, 300, PixelFormats.Bgra32, null, new byte[16], 8);
            var output = Imaging.Normalize(input);
            Assert(output.PixelWidth == 2 && output.PixelHeight == 2 && output.DpiX == 96, "DPI normalization changed dimensions");
        });
        Check("GPS metadata and source orientation", () =>
        {
            string path = Path.Combine(directory, "with-gps.jpg");
            using var jpeg = new ImageMagick.MagickImage(Imaging.Png(image));
            var profile = new ImageMagick.ExifProfile();
            profile.SetValue(ImageMagick.ExifTag.GPSLatitudeRef, "N");
            profile.SetValue(ImageMagick.ExifTag.GPSLongitudeRef, "E");
            profile.SetValue(ImageMagick.ExifTag.GPSLatitude, new[] { new ImageMagick.Rational(12), new ImageMagick.Rational(30), new ImageMagick.Rational(0) });
            profile.SetValue(ImageMagick.ExifTag.GPSLongitude, new[] { new ImageMagick.Rational(77), new ImageMagick.Rational(15), new ImageMagick.Rational(0) });
            profile.SetValue(ImageMagick.ExifTag.Orientation, (ushort)6);
            jpeg.SetProfile(profile); jpeg.Orientation = ImageMagick.OrientationType.RightTop; jpeg.Format = ImageMagick.MagickFormat.Jpeg; jpeg.Write(path);
            var data = MetadataReader.Read(path);
            Assert(Math.Abs(data.Latitude!.Value - 12.5) < .00001 && Math.Abs(data.Longitude!.Value - 77.25) < .00001, "GPS coordinate extraction");
            var loaded = Imaging.Load(path); Assert(loaded.PixelWidth == 800 && loaded.PixelHeight == 1200, "EXIF orientation ignored");
            var export = Path.Combine(directory, "gps-removed.png"); Imaging.Save(loaded, export);
            Assert(MetadataReader.Read(export).Latitude == null, "Location leaked into export");
        });
        Check("Editable layers, grouped gestures and saved scene identity", () =>
        {
            var doc = new ImageDocument(); doc.Load(image, source);
            var shape = new Annotation { Box = new Rect(20, 30, 100, 80), Color = Colors.Red };
            doc.SetAnnotations([shape]); var first = doc.Scene;
            doc.SetAnnotations([shape.Move(new Vector(10, 0))]);
            doc.SetAnnotations([shape.Move(new Vector(30, 0))], true);
            Assert(doc.BaseImage == image && doc.Annotations[0].Box.X == 50, "Moving an object copied or flattened the base");
            doc.Undo(); Assert(doc.Scene == first && doc.Annotations[0].Box.X == 20, "Gesture undo should restore its starting scene");
            doc.Redo(); doc.MarkSaved(source); var saved = doc.Scene;
            doc.SetAnnotations([doc.Annotations[0] with { Color = Colors.Blue }], true);
            Assert(doc.Dirty && doc.Annotations.Length == 1, "Style edit lost its layer");
            doc.Undo(); Assert(!doc.Dirty && doc.Scene == saved, "Undo across saved annotation state");
            doc.Undo(); doc.SetAnnotations([]); Assert(!doc.CanRedo, "New layer edits must clear redo");
        });
        Check("Shape resize and independent arrow endpoints", () =>
        {
            var shape = new Annotation { Kind = "Ellipse", Box = new Rect(10, 20, 100, 80) };
            var resized = shape.Resize(new Rect(30, 40, 200, 30), false);
            Assert(shape.Box.Width == 100 && resized.Box.Width == 200 && resized.Box.Height == 30, "Resize mutated the original or locked proportions");
            var arrow = new Annotation { Kind = "Arrow", Start = new Point(20, 30), End = new Point(200, 90) };
            var shortArrow = arrow with { Start = new Point(150, 70) };
            Assert(shortArrow.End == arrow.End && (shortArrow.End - shortArrow.Start).Length < (arrow.End - arrow.Start).Length, "Tail could not be shortened independently");
            Assert(shortArrow.Hit(new Point(175, 80), 5), "Arrow stroke hit-test failed");
        });
        Check("Text reflow differs from glyph stretch and supports style changes", () =>
        {
            var text = new Annotation { Kind = "Text", Text = "Editable text across several words", FontSize = 32, Box = new Rect(10, 20, 150, 50) };
            var wide = text.Resize(new Rect(10, 20, 900, text.Bounds.Height), true);
            Assert(wide.Bounds.Height < text.Bounds.Height && wide.StretchX == 1 && wide.Text == text.Text, "Side resizing did not reflow text");
            var stretched = wide.Resize(new Rect(10, 20, wide.Bounds.Width * .5, wide.Bounds.Height * 2), false);
            Assert(Math.Abs(stretched.StretchX - .5) < .001 && Math.Abs(stretched.StretchY - 2) < .001 && stretched.Box.Width == wide.Box.Width, "Corner stretching changed wrap width");
            var styled = stretched with { Color = Colors.Coral, FontSize = 64 };
            Assert(styled.Id == text.Id && styled.Text == text.Text && styled.Bounds.Height > stretched.Bounds.Height, "Text style edit lost identity or font size");
        });
        Check("Layer export composites pixels while keeping session objects editable", () =>
        {
            byte[] pixels = Enumerable.Repeat((byte)255, 240 * 160 * 4).ToArray();
            var white = BitmapSource.Create(240, 160, 96, 96, PixelFormats.Pbgra32, null, pixels, 240 * 4); white.Freeze();
            var doc = new ImageDocument(); doc.Load(white, source);
            var rectangle = new Annotation { Box = new Rect(20, 20, 80, 60), Color = Colors.Red, Stroke = 4 };
            doc.SetAnnotations([rectangle]);
            var path = Path.Combine(directory, "editable-export.png"); Imaging.Save(doc.Current!, path); doc.MarkSaved(path);
            var exported = Imaging.Load(path); byte[] p = new byte[4]; exported.CopyPixels(new Int32Rect(20, 30, 1, 1), p, 4, 0);
            Assert(p[2] > 240 && p[1] < 10 && p[0] < 10, "Export omitted the red layer");
            white.CopyPixels(new Int32Rect(20, 30, 1, 1), p, 4, 0); Assert(p[1] == 255, "Base photograph was mutated");
            Assert(doc.Annotations[0].Id == rectangle.Id && !doc.Dirty, "Saving flattened the live objects");
            doc.SetAnnotations([rectangle.Move(new Vector(30, 0))]); Assert(doc.Dirty, "Saved layers should remain editable");
        });
        Check("Undo restores layers after a whole-image raster operation", () =>
        {
            var doc = new ImageDocument(); doc.Load(image, source);
            var text = new Annotation { Kind = "Text", Text = "Keep editing", Box = new Rect(20, 20, 300, 60) };
            doc.SetAnnotations([text]);
            doc.Commit(Imaging.Resize(doc.Current!, 600, 400)); Assert(doc.Annotations.Length == 0, "Raster operation did not establish its new canvas");
            doc.Undo(); Assert(doc.Annotations.Length == 1 && doc.Annotations[0].Id == text.Id && doc.BaseImage == image, "Undo did not restore editable objects");
        });
        // Optional external codec fixtures, fetched by scripts/test.ps1. HEIC encoding is
        // deliberately unavailable in the Windows codec bundle, so use decoder fixtures.
        if (Environment.GetEnvironmentVariable("SNAPVIEW_CODEC_FIXTURES") is string fixtures)
        Check("HEIC and AVIF decoding", () =>
        {
            foreach (var ext in new[] { "heic", "avif" })
            {
                var decoded = Imaging.Load(Path.Combine(fixtures, "example." + ext));
                Assert(decoded.PixelWidth > 0 && decoded.PixelHeight > 0, "Extended codec decoding");
            }
        });
    }
}
