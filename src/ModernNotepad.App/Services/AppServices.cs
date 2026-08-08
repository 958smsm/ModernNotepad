using ModernNotepad.Core.Analysis;
using ModernNotepad.Core.Models;
using ModernNotepad.Core.Services;
using ModernNotepad.Core.Structured;

namespace ModernNotepad.App.Services;

public sealed class AppServices : IDisposable
{
    private AppServices(
        SettingsService settingsService,
        AppSettings settings,
        FileService fileService,
        RecoveryService recoveryService,
        SessionService sessionService,
        AnalysisCoordinator analysisCoordinator,
        StructuredTextService structuredTextService)
    {
        SettingsService = settingsService;
        Settings = settings;
        FileService = fileService;
        RecoveryService = recoveryService;
        SessionService = sessionService;
        AnalysisCoordinator = analysisCoordinator;
        StructuredTextService = structuredTextService;
    }

    public SettingsService SettingsService { get; }
    public AppSettings Settings { get; }
    public FileService FileService { get; }
    public RecoveryService RecoveryService { get; }
    public SessionService SessionService { get; }
    public AnalysisCoordinator AnalysisCoordinator { get; }
    public StructuredTextService StructuredTextService { get; }

    public static async Task<AppServices> CreateAsync(CancellationToken cancellationToken = default)
    {
        var settingsService = new SettingsService();
        var settings = await settingsService.LoadAsync(cancellationToken);

        return new AppServices(
            settingsService,
            settings,
            new FileService(),
            new RecoveryService(settingsService.BaseDirectory),
            new SessionService(settingsService.BaseDirectory),
            new AnalysisCoordinator(),
            new StructuredTextService());
    }

    public Task SaveSettingsAsync(CancellationToken cancellationToken = default) =>
        SettingsService.SaveAsync(Settings, cancellationToken);

    public void Dispose() => AnalysisCoordinator.Dispose();
}
