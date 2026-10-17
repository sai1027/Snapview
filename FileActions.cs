using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

namespace Snapview;

internal static class FileActions
{
    private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Snapview", "file-actions.json");
    public static bool SkipDeleteConfirmation { get; private set; } = ReadPreference(SettingsPath);

    internal static bool ReadPreference(string path)
    {
        try { return JsonSerializer.Deserialize<bool>(File.ReadAllText(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }
    public static void RememberDeleteConfirmation(bool skip)
    {
        WritePreference(SettingsPath, skip);
        SkipDeleteConfirmation = skip;
    }
    internal static void WritePreference(string path, bool skip)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(skip));
    }
    public static string RenameDestination(string path, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.EndsWith('.') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Enter a file name without slashes, reserved characters, or spaces/dots at the ends.");
        string device = name.Split('.')[0].ToUpperInvariant();
        if (device is "CON" or "PRN" or "AUX" or "NUL" ||
            device.Length == 4 && (device.StartsWith("COM") || device.StartsWith("LPT")) && "123456789¹²³".Contains(device[3]))
            throw new ArgumentException("That name is reserved by Windows. Choose another name.");
        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, name + Path.GetExtension(path));
    }
    public static (string Path, bool RecoveryPreserved) Rename(string path, string name)
    {
        string destination = RenameDestination(path, name);
        if (string.Equals(path, destination, StringComparison.Ordinal)) return (path, true);
        if (!string.Equals(path, destination, StringComparison.OrdinalIgnoreCase) && File.Exists(destination))
            throw new IOException("An image with that name already exists. Choose another name.");
        string? backup = Originals.Find(path);
        File.Move(path, destination); // never replace another file
        bool recoveryPreserved = true;
        if (backup != null)
        {
            try { Originals.Record(destination, backup); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { recoveryPreserved = false; }
        }
        return (destination, recoveryPreserved);
    }
    public static void Delete(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("This image is no longer on disk.", path);
        FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
    }
    public static void DeleteDocument(ImageDocument document)
    {
        Delete(document.Path ?? throw new InvalidOperationException("This image has no file on disk."));
        document.Clear();
    }
    [DllImport("user32.dll")]
    internal static extern uint GetDoubleClickTime();
}
