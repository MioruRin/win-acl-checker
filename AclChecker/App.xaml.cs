using Microsoft.UI.Xaml;
using System.IO;

namespace AclChecker;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }

    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "AclChecker_boot.log");

    public App()
    {
        Log("App() START");

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Log($"[UnhandledException] {ex}");
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Log($"[UnobservedTaskException] {e.Exception}");
        };

        // Self-contained deployment: no Bootstrap needed
        // WindowsAppSDKSelfContained=true handles runtime initialization automatically
        Log("Self-contained mode, skipping Bootstrap");

        Log("Calling InitializeComponent");
        this.InitializeComponent();
        Log("InitializeComponent DONE");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log("OnLaunched START");

        try
        {
            MainWindow = new MainWindow();
            Log("MainWindow created");

            MainWindow.Activate();
            Log("MainWindow activated");
        }
        catch (System.Exception ex)
        {
            Log($"OnLaunched EXCEPTION: {ex}");
        }
    }

    public static void Log(string message)
    {
        var line = $"[{System.DateTime.Now:HH:mm:ss.fff}] {message}\n";
        try { File.AppendAllText(LogPath, line); } catch { }
    }
}
