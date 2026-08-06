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
            MessageBox.Show(
                $"Modern Notepad could not start.\n\n{exception.Message}",
                "Startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
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
