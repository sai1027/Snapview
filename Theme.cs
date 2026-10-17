using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Snapview;

internal static class Theme
{
    private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Snapview", "appearance.json");
    public static bool IsDark { get; private set; }
    private static readonly (string Key, string Light, string Dark)[] Palette =
    [
        ("Ink", "#242B29", "#EDF2EF"), ("Muted", "#777F79", "#A9B6AF"),
        ("Accent", "#426958", "#80B49A"), ("Surface", "#FAFBF9", "#202723"),
        ("Canvas", "#F0F2ED", "#151B18"), ("Markup", "#F4F6F1", "#262F29"),
        ("Divider", "#DCE0DA", "#404C44"), ("Selected", "#DAE4D5", "#425E4D"),
        ("SoftButton", "#E8EEE6", "#35463B"), ("Hover", "#E9EDE7", "#39473E"),
        ("Pressed", "#DCE5DC", "#465A4C"), ("Field", "#FFFFFF", "#1B231E"),
        ("PrimaryBrush", "#426958", "#3D7056"), ("PrimaryHover", "#335443", "#4C8567"),
        ("SwatchRing", "#245D9A", "#96C5FF"), ("SwatchSelected", "#E0EBF7", "#344F69")
    ];

    public static void Load()
    {
        bool dark = false;
        try { dark = JsonSerializer.Deserialize<bool>(File.ReadAllText(SettingsPath)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        Apply(dark);
    }
    public static void Apply(bool dark, bool save = false)
    {
        IsDark = dark;
        foreach (var (key, light, night) in Palette)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? night : light));
            brush.Freeze(); Application.Current.Resources[key] = brush;
        }
        foreach (Window window in Application.Current.Windows) UpdateCaption(window);
        if (save)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(dark));
        }
    }
    public static void UpdateCaption(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;
        int dark = IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
