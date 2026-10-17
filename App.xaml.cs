using System.IO;
using System.Windows;

namespace Snapview;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--self-test"))
        {
            try
            {
                SmokeTests.Run();
                var error = Path.Combine(AppContext.BaseDirectory, "self-test-error.txt");
                if (File.Exists(error)) File.Delete(error);
                Shutdown(0);
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-error.txt"), ex.ToString()); Shutdown(1); }
            return;
        }
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if (e.Args.FirstOrDefault(File.Exists) is string path)
            window.OpenPath(path);
    }
}
