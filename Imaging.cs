using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace Snapview;

internal sealed class ImageDocument
{
    public const long MaxPixels = 64_000_000;
    private readonly List<ImageScene> undo = [];
    private readonly List<ImageScene> redo = [];
    private ImageScene? saved;
    public ImageScene? Scene { get; private set; }
    public BitmapSource? BaseImage => Scene?.Image;
    public BitmapSource? Current => Scene?.Composite;
    public Annotation[] Annotations => Scene?.Annotations ?? [];
    public BitmapSource? Original { get; private set; }
    public string? Path { get; private set; }
    public bool Dirty => Scene != null && Scene != saved;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public void Load(BitmapSource bitmap, string? path)
    {
        Original = bitmap; Scene = new(bitmap, []); saved = path == null ? null : Scene; Path = path; undo.Clear(); redo.Clear();
    }
    public void Commit(BitmapSource bitmap)
    {
        if (Scene != null) CommitScene(new(bitmap, []), false);
    }
    public void SetAnnotations(Annotation[] annotations, bool coalesce = false)
    { if (Scene != null) CommitScene(new(Scene.Image, annotations), coalesce); }
    private void CommitScene(ImageScene scene, bool coalesce)
    {
        if (Scene == null) return;
        Scene.ReleaseComposite();
        if (!coalesce || Scene == saved) undo.Add(Scene);
        redo.Clear(); Scene = scene;
        long Bytes() => undo.Select(x => x.Image).Distinct().Sum(x => (long)x.PixelWidth * x.PixelHeight * 4);
        // Shared base images are counted once; vector-only edits do not copy photograph pixels.
        while (undo.Count > 1 && (undo.Count > 150 || Bytes() > 192L * 1024 * 1024)) undo.RemoveAt(0);
    }
    public void Undo() { if (!CanUndo) return; Scene?.ReleaseComposite(); redo.Add(Scene!); Scene = undo[^1]; undo.RemoveAt(undo.Count - 1); }
    public void Redo() { if (!CanRedo) return; Scene?.ReleaseComposite(); undo.Add(Scene!); Scene = redo[^1]; redo.RemoveAt(redo.Count - 1); }
    public void MarkSaved(string path) { Path = path; saved = Scene; }
}

internal static class Imaging
{
    public static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".heic", ".heif", ".avif"];
    public const string OpenFilter = "Images|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.gif;*.tif;*.tiff;*.ico;*.heic;*.heif;*.avif|All files|*.*";
    public static void CheckSize(long width, long height)
    {
        if (width < 1 || height < 1 || width > 32000 || height > 32000 || width * height > ImageDocument.MaxPixels)
            throw new InvalidOperationException("This image is too large. Snapview supports images up to 64 megapixels and 32,000 pixels per side.");
    }
    public static BitmapSource Load(string path)
    {
        // WIC is the fast path. Extended codecs are loaded only when needed.
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".webp" or ".heic" or ".heif" or ".avif") return LoadExtended(path);
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        CheckSize(frame.PixelWidth, frame.PixelHeight);
        BitmapSource result = frame;
        int orientation = 1;
        if (frame.Metadata is BitmapMetadata metadata)
        {
            foreach (var query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
            {
                try { if (metadata.ContainsQuery(query)) { orientation = Convert.ToInt32(metadata.GetQuery(query)); break; } }
                catch (NotSupportedException) { }
            }
        }
        result = Orient(result, orientation);
        result = Normalize(result);
        result.Freeze(); return result;
    }
    private static BitmapSource LoadExtended(string path)
    {
        var info = new MagickImageInfo(path); CheckSize(info.Width, info.Height);
        using var image = new MagickImage(path); image.AutoOrient();
        return FromBytes(image.ToByteArray(MagickFormat.Png));
    }
    public static BitmapSource FromBytes(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var image = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        CheckSize(image.PixelWidth, image.PixelHeight);
        return Normalize(image);
    }
    // Every edit uses 96-DPI pixel coordinates, independently of source DPI metadata.
    public static BitmapSource Normalize(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        int stride = converted.PixelWidth * 4;
        byte[] pixels = new byte[stride * converted.PixelHeight]; converted.CopyPixels(pixels, stride, 0);
        var result = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        result.Freeze(); return result;
    }
    public static BitmapSource Orient(BitmapSource source, int orientation)
    {
        var group = new TransformGroup();
        switch (orientation)
        {
            case 2: group.Children.Add(new ScaleTransform(-1, 1)); break;
            case 3: group.Children.Add(new RotateTransform(180)); break;
            case 4: group.Children.Add(new ScaleTransform(1, -1)); break;
            case 5: group.Children.Add(new ScaleTransform(-1, 1)); group.Children.Add(new RotateTransform(270)); break;
            case 6: group.Children.Add(new RotateTransform(90)); break;
            case 7: group.Children.Add(new ScaleTransform(-1, 1)); group.Children.Add(new RotateTransform(90)); break;
            case 8: group.Children.Add(new RotateTransform(270)); break;
            default: return source;
        }
        var result = new TransformedBitmap(source, group); result.Freeze(); return result;
    }
    public static BitmapSource Resize(BitmapSource source, int width, int height)
    {
        CheckSize(width, height);
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen()) dc.DrawImage(source, new Rect(0, 0, width, height));
        return Render(visual, width, height);
    }
    public static BitmapSource Render(Visual visual, int width, int height)
    {
        var result = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual); result.Freeze(); return result;
    }
    public static byte[] Png(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    public static void Save(BitmapSource source, string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".tif" or ".tiff"))
            throw new InvalidOperationException("Choose PNG, JPEG, WebP, BMP or TIFF to save.");
        string temporary = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, $".snapview-{Guid.NewGuid():N}{ext}");
        try
        {
            if (ext == ".png") File.WriteAllBytes(temporary, Png(source));
            else EncodeExtended(source, temporary, ext);
            // Atomic replacement on the same volume; a failed encode leaves the existing file intact.
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void EncodeExtended(BitmapSource source, string path, string ext)
    {
        using var image = new MagickImage(Png(source));
        if (ext is ".jpg" or ".jpeg" or ".bmp")
        { image.BackgroundColor = MagickColors.White; image.Alpha(AlphaOption.Remove); }
        image.Strip(); image.Quality = 92;
        image.Format = ext switch { ".jpg" or ".jpeg" => MagickFormat.Jpeg, ".webp" => MagickFormat.WebP, ".bmp" => MagickFormat.Bmp, _ => MagickFormat.Tiff };
        image.Write(path);
    }
    public static string SizeText(long bytes) => bytes >= 1_048_576 ? $"{bytes / 1048576d:0.##} MB" : bytes >= 1024 ? $"{bytes / 1024d:0.#} KB" : $"{bytes} B";
}

internal sealed record PhotoMetadata(List<(string Label, string Value)> Values, double? Latitude, double? Longitude);
internal static class MetadataReader
{
    public static PhotoMetadata Read(string path)
    {
        var values = new List<(string, string)>(); double? latitude = null, longitude = null;
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(path);
            foreach (var directory in directories)
            {
                if (directory is GpsDirectory gps && gps.TryGetGeoLocation(out var location))
                { latitude = location.Latitude; longitude = location.Longitude; }
                foreach (var tag in directory.Tags)
                {
                    if (tag.Description is not { Length: > 0 } description) continue;
                    if (tag.Name is "Make" or "Model" or "Lens Model" or "Date/Time Original" or "Exposure Time" or "F-Number" or "ISO Speed Ratings" or "Focal Length" or "Software" or "Copyright" or "Artist" or "Color Space")
                        values.Add((tag.Name, description));
                }
            }
        }
        catch (Exception ex) when (ex is ImageProcessingException or IOException or ArgumentException)
        { values.Add(("Metadata", "No readable camera metadata")); }
        return new(values, latitude, longitude);
    }
}

internal static class Originals
{
    private sealed record BackupRecord(string BackupPath, string SavedHash);
    private static string Folder => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Snapview", "Originals");
    private static string RecordPath(string path) => System.IO.Path.Combine(Folder, Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(path).ToUpperInvariant()))) + ".json");
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    public static string? Find(string path)
    {
        try
        {
            var record = JsonSerializer.Deserialize<BackupRecord>(File.ReadAllText(RecordPath(path)));
            return record != null && File.Exists(record.BackupPath) && record.SavedHash == Hash(path) ? record.BackupPath : null;
        }
        catch { return null; }
    }
    public static string Capture(string path)
    {
        System.IO.Directory.CreateDirectory(Folder);
        if (Find(path) is string existing) return existing;
        var backup = System.IO.Path.Combine(Folder, Guid.NewGuid().ToString("N") + System.IO.Path.GetExtension(path));
        File.Copy(path, backup, false); return backup;
    }
    public static void Record(string path, string backup)
        => File.WriteAllText(RecordPath(path), JsonSerializer.Serialize(new BackupRecord(backup, Hash(path))));
}
