using System.IO;
using System.Windows;
using System.Windows.Threading;
using ModernNotepad.App.Services;

namespace ModernNotepad.App;

public partial class App : Application
{
    public AppServices Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            Services = await AppServices.CreateAsync();
            ThemeManager.Apply(Services.Settings);

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
            await mainWindow.InitializeAsync(e.Args);
        }
        catch (Exception exception)
        {
            var logPath = TryWriteStartupErrorLog(exception);
            var diagnosticHint = logPath is null
                ? string.Empty
                : $"\n\nDiagnostic details were written to:\n{logPath}";

            MessageBox.Show(
                $"Modern Notepad could not start.\n\n{exception.Message}{diagnosticHint}",
                "Startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (Services is not null)
        {
            Services.Dispose();
        }

        base.OnExit(e);
    }

    private static string? TryWriteStartupErrorLog(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ModernNotepad");
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, "startup-error.log");
            File.WriteAllText(
                path,
                $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}{Environment.NewLine}");
            return path;
        }
        catch
        {
            // Diagnostics must never replace the original startup error.
            return null;
        }
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"An unexpected error occurred. Your recovery data is kept automatically.\n\n{e.Exception.Message}",
            "Modern Notepad",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
