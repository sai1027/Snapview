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
        ("Ink", "#303640", "#D8DCE2"), ("Muted", "#66707D", "#99A1AC"),
        ("Accent", "#2869C5", "#5799F2"), ("Surface", "#F8F9FB", "#1B1D20"),
        ("Canvas", "#ECEFF2", "#101112"), ("Markup", "#FFFFFF", "#1B1D20"),
        ("Divider", "#D1D7DF", "#3A3D43"), ("Selected", "#E6EFFC", "#243954"),
        ("SoftButton", "#E9EDF3", "#292D34"), ("Hover", "#E9EDF3", "#292D34"),
        ("Pressed", "#DCE5F1", "#334053"), ("Field", "#FFFFFF", "#1B1D20"),
        ("PrimaryBrush", "#2869C5", "#2869C5"), ("PrimaryHover", "#2057A5", "#347ADD"),
        ("SwatchRing", "#2869C5", "#5799F2"), ("SwatchSelected", "#E6EFFC", "#243954"),
        ("ToastSurface", "#303640", "#303640"), ("ToastInk", "#FFFFFF", "#FFFFFF")
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
