using System.IO;
using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace QuotaRing;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        base.OnStartup(e);

        if (e.Args.Length >= 2 && string.Equals(e.Args[0], "--screenshot", StringComparison.OrdinalIgnoreCase))
        {
            var win = new MainWindow(screenshotOutputDir: e.Args[1]);
            win.Show();
            return;
        }

        var main = new MainWindow();
        main.Show();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);
        MessageBox.Show($"QuotaRing 发生错误：\n{e.Exception.Message}", "QuotaRing", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            WriteCrashLog(ex);
        }
    }

    private static void WriteCrashLog(Exception ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuotaRing");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch (Exception logEx)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to write crash log: {logEx.Message}");
        }
    }
}
