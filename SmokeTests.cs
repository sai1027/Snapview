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
        Check("Crop preview pixels match the applied crop at fractional zoom and edges", () =>
        {
            foreach (var (selection, expected) in new[]
            {
                (new Rect(10, 20, 120, 180), new Int32Rect(10, 20, 120, 180)),
                (new Rect(10.8, 20.3, 120.4, 180.8), new Int32Rect(10, 20, 122, 182)),
                (new Rect(new Point(1200, 800), new Point(1197.4, 797.2)), new Int32Rect(1197, 797, 3, 3)),
                (new Rect(0, 0, 1200, 800), new Int32Rect(0, 0, 1200, 800))
            })
            {
                var bounds = CropPreview.PixelBounds(selection, image.PixelWidth, image.PixelHeight);
                Assert(bounds == expected, "Unexpected pixel bounds: " + bounds);
                var result = new CroppedBitmap(image, bounds);
                Assert(result.PixelWidth == expected.Width && result.PixelHeight == expected.Height, "Crop badge/export mismatch");
            }
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
        Check("Selected-word color and size preserve other words through undo and redo", () =>
        {
            var text = new Annotation { Kind = "Text", Text = "Small bright words", Color = Colors.Black, FontSize = 32, Box = new Rect(10, 20, 600, 60) };
            var colored = text.WithTextStyle(6, 6, Colors.Red);
            var styled = colored.WithTextStyle(6, 6, size: 64);
            Assert(styled.Text == text.Text && styled.Id == text.Id, "Styling changed text or object identity");
            Assert(styled.StyleAt(0) == (Colors.Black, 32d) && styled.StyleAt(13) == (Colors.Black, 32d), "Unselected words changed");
            Assert(styled.StyleAt(7) == (Colors.Red, 64d), "Selected word did not retain both color and size");
            var wholeColor = styled.WithTextStyle(0, styled.Text.Length, Colors.Blue);
            Assert(wholeColor.StyleAt(7) == (Colors.Blue, 64d) && wholeColor.StyleAt(0) == (Colors.Blue, 32d), "Whole-box color discarded font differences");
            Assert(styled.WithTextStyle(6, 6, Colors.Red) == styled, "Unchanged style created another history state");
            var doc = new ImageDocument(); doc.Load(image, source); doc.SetAnnotations([colored]); doc.SetAnnotations([styled]);
            doc.Undo(); Assert(doc.Annotations[0].StyleAt(7) == (Colors.Red, 32d), "Undo lost the previous word style");
            doc.Redo(); Assert(doc.Annotations[0].StyleAt(7) == (Colors.Red, 64d), "Redo lost the word size");
        });
        Check("Inline rich text round trip, retained selection and editing styled text", () =>
        {
            var original = new Annotation { Kind = "Text", Text = "One blue word\nSecond line\n", Color = Colors.Black, FontSize = 32, Box = new Rect(0, 0, 600, 60) }
                .WithTextStyle(4, 4, Colors.Blue, 60);
            var editor = new System.Windows.Controls.RichTextBox();
            AnnotationText.Load(editor, original, 4, 4);
            Assert(AnnotationText.PlainText(editor) == original.Text, "Newlines changed when opening the editor");
            Assert(editor.Selection.Text == "blue" && AnnotationText.Selection(editor) == (4, 4), "Word selection offsets were lost");
            editor.Selection.ApplyPropertyValue(System.Windows.Documents.TextElement.ForegroundProperty, Brushes.Red);
            var edited = AnnotationText.Read(editor, original);
            Assert(edited.Text == original.Text && edited.StyleAt(4) == (Colors.Red, 60d) && edited.StyleAt(0) == (Colors.Black, 32d), "Editor applied style outside the selected word");
            editor.Selection.Text = "NEW";
            var replaced = AnnotationText.Read(editor, original);
            Assert(replaced.Text == "One NEW word\nSecond line\n", "Replacing a styled word damaged surrounding text");
            AnnotationText.Load(editor, edited, 4, 4);
            Assert(editor.Selection.Text == "blue" && AnnotationText.Read(editor, edited).StyleAt(5) == (Colors.Red, 60d), "Re-entering text lost word styling");
            var multiline = new System.Windows.Controls.RichTextBox();
            multiline.Document.Blocks.Clear();
            multiline.Document.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("First")));
            multiline.Document.Blocks.Add(new System.Windows.Documents.Paragraph());
            multiline.Document.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("Third")));
            Assert(AnnotationText.Read(multiline, original).Text == "First\n\nThird", "Paragraph editing lost blank lines");
        });
        Check("Mixed text styles survive image export", () =>
        {
            var text = new Annotation { Kind = "Text", Text = "RED BLACK", Color = Colors.Black, FontSize = 32, Box = new Rect(40, 40, 900, 80) }
                .WithTextStyle(0, 3, Colors.Red, 64);
            var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1200, 800));
            var doc = new ImageDocument(); doc.Load(Imaging.Render(visual, 1200, 800), source); doc.SetAnnotations([text]);
            string path = Path.Combine(directory, "styled-words.png"); Imaging.Save(doc.Current!, path);
            var saved = Imaging.Load(path); byte[] pixels = new byte[1200 * 800 * 4]; saved.CopyPixels(pixels, 1200 * 4, 0);
            int red = 0, black = 0;
            for (int i = 0; i < pixels.Length; i += 4)
            { if (pixels[i + 2] > 200 && pixels[i + 1] < 30 && pixels[i] < 30) red++; if (pixels[i] < 30 && pixels[i + 1] < 30 && pixels[i + 2] < 30) black++; }
            Assert(red > 200 && black > 100, "Export did not render both word colors");
            doc.MarkSaved(path); Assert(doc.Annotations[0].StyleAt(0) == (Colors.Red, 64d), "Save discarded the editable styled word");
        });
        Check("Selected-word bold and italic round trip and render through history", () =>
        {
            var original = new Annotation { Kind = "Text", Text = "Small bold words", Color = Colors.Black, FontSize = 32, Box = new Rect(20, 20, 600, 100) };
            var bold = original.WithTextStyle(6, 4, bold: true);
            var both = bold.WithTextStyle(6, 4, italic: true).WithTextStyle(6, 4, Colors.Red, 48);
            Assert(!both.AppearanceAt(0).Bold && !both.AppearanceAt(11).Italic, "Emphasis changed unselected words");
            Assert(both.AppearanceAt(7) == new TextAppearance(Colors.Red, 48, true, true), "Color/size changes lost emphasis");
            var editor = new System.Windows.Controls.RichTextBox(); AnnotationText.Load(editor, both, 6, 4);
            Assert(editor.Selection.Text == "bold", "Emphasis lost the word selection");
            var restored = AnnotationText.Read(editor, both);
            Assert(restored.AppearanceAt(7) == both.AppearanceAt(7) && !restored.AppearanceAt(0).Bold, "Rich text round trip lost emphasis");
            editor.Selection.ApplyPropertyValue(System.Windows.Documents.TextElement.FontWeightProperty, FontWeights.Normal);
            var normal = AnnotationText.Read(editor, both);
            Assert(!normal.AppearanceAt(7).Bold && normal.AppearanceAt(7).Italic, "Removing bold discarded italic");
            var doc = new ImageDocument(); doc.Load(image, source); doc.SetAnnotations([bold]); doc.SetAnnotations([both]);
            doc.Undo(); Assert(doc.Annotations[0].AppearanceAt(7).Bold && !doc.Annotations[0].AppearanceAt(7).Italic, "Undo lost emphasis state");
            doc.Redo(); Assert(doc.Annotations[0].AppearanceAt(7).Italic, "Redo lost italic state");
            var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) { dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1200, 800)); both.Draw(dc); }
            string path = Path.Combine(directory, "bold-italic.png"); Imaging.Save(Imaging.Render(visual, 1200, 800), path);
            byte[] rendered = Imaging.Png(Imaging.Load(path));
            var plain = both.WithTextStyle(6, 4, bold: false, italic: false);
            var plainVisual = new DrawingVisual(); using (var dc = plainVisual.RenderOpen()) { dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1200, 800)); plain.Draw(dc); }
            Assert(!rendered.SequenceEqual(Imaging.Png(Imaging.Render(plainVisual, 1200, 800))), "Export ignored bold/italic fonts");
        });
        Check("Rename validates names and preserves bytes, edits, history and recovery", () =>
        {
            string oldPath = Path.Combine(directory, "rename-source.png"); Imaging.Save(image, oldPath);
            string backup = Originals.Capture(oldPath); Imaging.Save(Imaging.Resize(image, 600, 400), oldPath); Originals.Record(oldPath, backup);
            byte[] bytes = File.ReadAllBytes(oldPath);
            var doc = new ImageDocument(); doc.Load(Imaging.Load(oldPath), oldPath);
            doc.SetAnnotations([new Annotation { Kind = "Text", Text = "Unsaved", Box = new Rect(10, 10, 200, 60) }]);
            var scene = doc.Scene;
            string name = "renamed-" + Guid.NewGuid().ToString("N");
            var result = FileActions.Rename(oldPath, name); doc.Relocate(result.Path);
            Assert(!File.Exists(oldPath) && Path.GetFileName(result.Path) == name + ".png" && bytes.SequenceEqual(File.ReadAllBytes(result.Path)), "Rename changed bytes or extension");
            Assert(result.RecoveryPreserved && Originals.Find(result.Path) == backup, "Rename lost persistent original recovery");
            Assert(doc.Scene == scene && doc.Dirty && doc.CanUndo && doc.Path == result.Path, "Rename lost editable state");
            doc.Undo(); Assert(!doc.Dirty && doc.Path == result.Path, "Undo reverted the new file path");
            doc.Redo(); Assert(doc.Dirty && doc.Annotations.Length == 1, "Redo after rename lost layers");
            string collision = Path.Combine(directory, "rename-collision.png"); File.WriteAllBytes(collision, bytes);
            bool rejected = false; try { FileActions.Rename(result.Path, "rename-collision"); } catch (IOException) { rejected = true; }
            Assert(rejected && File.Exists(result.Path) && bytes.SequenceEqual(File.ReadAllBytes(collision)), "Rename replaced an existing image");
            foreach (string bad in new[] { "", "..", "CON", "NUL.txt", "COM1", "bad/name", "bad\\name", "name.", "name ", "name:stream" })
            { rejected = false; try { FileActions.RenameDestination(result.Path, bad); } catch (ArgumentException) { rejected = true; } Assert(rejected, "Unsafe file name accepted: " + bad); }
        });
        Check("Delete removes a disposable file and clears history only after success", () =>
        {
            string path = Path.Combine(directory, "delete-disposable-" + Guid.NewGuid().ToString("N") + ".png"); Imaging.Save(image, path);
            var doc = new ImageDocument(); doc.Load(image, path); doc.SetAnnotations([new Annotation { Box = new Rect(1, 1, 10, 10) }]);
            FileActions.DeleteDocument(doc);
            Assert(!File.Exists(path) && doc.Path == null && doc.Current == null && doc.Original == null && !doc.Dirty && !doc.CanUndo && !doc.CanRedo, "Delete did not clear the image and history");
            doc.Load(image, path); var scene = doc.Scene;
            bool failed = false; try { FileActions.DeleteDocument(doc); } catch (FileNotFoundException) { failed = true; }
            Assert(failed && doc.Scene == scene && doc.Path == path, "Failed deletion cleared the loaded document");
        });
        Check("Deletion browses next, then previous, then the empty canvas", () =>
        {
            string folder = Path.Combine(directory, "delete-browse-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string[] paths = new[] { "a.png", "B.png", "c.png", "d.png" }.Select(name => Path.Combine(folder, name)).ToArray();
            foreach (string path in paths) Imaging.Save(image, path);
            var doc = new ImageDocument();
            foreach (var (index, expected) in new (int, string?)[] { (1, paths[2]), (0, paths[2]), (3, paths[2]), (2, null) })
            {
                doc.Load(image, paths[index]);
                doc.SetAnnotations([new Annotation { Box = new Rect(1, 1, 10, 10) }]);
                FileActions.DeleteDocument(doc);
                var remaining = paths.Where(File.Exists).Reverse().ToArray();
                string? next = FileActions.NeighborAfterDeletion(paths[index], remaining);
                Assert(next == expected, "Deletion chose the wrong folder neighbor");
                if (next != null) doc.Load(Imaging.Load(next), next);
                Assert(doc.Path == expected && !doc.Dirty && !doc.CanUndo && !doc.CanRedo, "Deletion retained the previous image's history");
                Assert((doc.BaseImage != null) == (expected != null), "Last deletion did not return to the empty canvas");
            }
            Assert(FileActions.NeighborAfterDeletion(paths[1], [paths[1].ToUpperInvariant()]) == null, "Deleted path was selected again");
        });
        Check("Delete confirmation preference defaults safe and can be restored", () =>
        {
            string path = Path.Combine(directory, "delete-preference.json");
            File.WriteAllText(path, "broken-json"); Assert(!FileActions.ReadPreference(path), "Invalid settings skipped confirmation");
            FileActions.WritePreference(path, true); Assert(FileActions.ReadPreference(path), "Skip preference was not saved");
            FileActions.WritePreference(path, false); Assert(!FileActions.ReadPreference(path), "Confirmation could not be restored");
        });
        Check("Rename undo and redo follow the order of file names and image edits", () =>
        {
            string id = Guid.NewGuid().ToString("N");
            string initial = Path.Combine(directory, "history-" + id + ".png"); Imaging.Save(image, initial);
            var doc = new ImageDocument(); doc.Load(image, initial);
            var history = new RenameHistory();
            void Rename(string name)
            { string before = doc.Path!; doc.Relocate(FileActions.Rename(before, name).Path); history.Record(before, doc); }
            Rename("first-" + id); string first = doc.Path!;
            Assert(history.CanUndo(doc) && !doc.CanUndo && !doc.Dirty, "File-only rename should be undoable without dirty pixels");
            doc.SetAnnotations([new Annotation { Box = new Rect(20, 20, 30, 30) }]);
            Assert(!history.CanUndo(doc), "Rename incorrectly took priority over a later image edit");
            Rename("second-" + id); string second = doc.Path!; var edited = doc.Scene;
            Assert(history.CanUndo(doc), "Latest rename was not available");
            history.Undo(doc); Assert(doc.Path == first && doc.Scene == edited && doc.Dirty, "Undo rename changed the pixels");
            doc.Undo(); Assert(history.CanUndo(doc) && !doc.Dirty, "Earlier rename was not restored at its history position");
            history.Undo(doc); Assert(doc.Path == initial && File.Exists(initial) && !File.Exists(first), "Original name was not restored on disk");
            history.Redo(doc); Assert(doc.Path == first && !history.CanRedo(doc), "Redo skipped an intervening image edit");
            doc.Redo(); Assert(history.CanRedo(doc), "Second rename was not available after the image redo");
            history.Redo(doc); Assert(doc.Path == second && doc.Scene == edited && File.Exists(second), "Redo did not restore the last name and edits");
        });
        Check("Rename undo collisions preserve the current file and pending undo", () =>
        {
            string id = Guid.NewGuid().ToString("N");
            string initial = Path.Combine(directory, "undo-collision-" + id + ".png"); Imaging.Save(image, initial);
            var doc = new ImageDocument(); doc.Load(image, initial);
            var history = new RenameHistory();
            string renamed = FileActions.Rename(initial, "undo-renamed-" + id).Path; doc.Relocate(renamed); history.Record(initial, doc);
            File.WriteAllText(initial, "another file");
            bool failed = false; try { history.Undo(doc); } catch (IOException) { failed = true; }
            Assert(failed && doc.Path == renamed && File.Exists(renamed) && history.CanUndo(doc) && File.ReadAllText(initial) == "another file", "Undo rename overwrote a collision or lost history");
            // Use the real branch invalidation method, without changing global preferences.
            history.Clear(); Assert(!history.CanUndo(doc) && !history.CanRedo(doc), "Loading another file would retain rename history");
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
