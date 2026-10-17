using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace Snapview;

internal static class Associations
{
    [DllImport("shell32.dll")] private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
    public static void Register(string executable)
    {
        var command = $"\"{executable}\" \"%1\"";
        using (var app = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Applications\Snapview.exe"))
        {
            app.SetValue("FriendlyAppName", "Snapview");
            using var shell = app.CreateSubKey(@"shell\open\command"); shell.SetValue("", command);
            using var types = app.CreateSubKey("SupportedTypes");
            foreach (var ext in Imaging.Extensions) types.SetValue(ext, "");
        }
        using (var progId = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Snapview.Image"))
        {
            progId.SetValue("", "Snapview image");
            using var icon = progId.CreateSubKey("DefaultIcon"); icon.SetValue("", $"\"{executable}\",0");
            using var open = progId.CreateSubKey(@"shell\open\command"); open.SetValue("", command);
        }
        using (var capabilities = Registry.CurrentUser.CreateSubKey(@"Software\Snapview\Capabilities"))
        {
            capabilities.SetValue("ApplicationName", "Snapview");
            capabilities.SetValue("ApplicationDescription", "A quiet, fast image viewer with essential editing and local text extraction.");
            capabilities.SetValue("ApplicationIcon", $"\"{executable}\",0");
            using var associations = capabilities.CreateSubKey("FileAssociations");
            foreach (var ext in Imaging.Extensions)
            {
                associations.SetValue(ext, "Snapview.Image");
                using var openWith = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}\OpenWithProgids");
                openWith.SetValue("Snapview.Image", Array.Empty<byte>(), RegistryValueKind.None);
            }
        }
        using var registered = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications");
        registered.SetValue("Snapview", @"Software\Snapview\Capabilities");
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }
}
