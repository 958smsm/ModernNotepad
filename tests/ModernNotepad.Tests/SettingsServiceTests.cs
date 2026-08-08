using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModernNotepad.Core.Analysis;
using ModernNotepad.Core.Models;
using ModernNotepad.Core.Services;

namespace ModernNotepad.Tests;

[TestClass]
public sealed class SettingsServiceTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "ModernNotepad.Settings.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // Best-effort test cleanup.
        }
    }

    [TestMethod]
    public async Task SaveAndLoad_RoundTripsCustomSettings()
    {
        var service = new SettingsService(_directory);
        var settings = AppSettings.CreateDefaults();
        settings.Theme = ThemeMode.Dark;
        settings.DefaultFontFamily = "Consolas";
        settings.DefaultFontSize = 18;
        settings.SmartColoringEnabled = true;
        settings.GrammarMode = GrammarAnalysisMode.GoogleCloudNaturalLanguage;
        settings.PythonTransport = PythonGrammarTransport.SharedMemory;
        settings.DuplicateThreshold = 5;
        settings.GrammarColors[GrammarCategory.Verb] = "#123456";
        settings.RecentFiles.Add(Path.Combine(_directory, "sample.txt"));

        await service.SaveAsync(settings);
        var savedJson = await File.ReadAllTextAsync(service.SettingsPath);
        var loaded = await service.LoadAsync();

        StringAssert.Contains(savedJson, "\"GrammarMode\": \"GoogleCloudNaturalLanguage\"");
        Assert.AreEqual(ThemeMode.Dark, loaded.Theme);
        Assert.AreEqual("Consolas", loaded.DefaultFontFamily);
        Assert.AreEqual(18d, loaded.DefaultFontSize);
        Assert.IsTrue(loaded.SmartColoringEnabled);
        Assert.AreEqual(GrammarAnalysisMode.GoogleCloudNaturalLanguage, loaded.GrammarMode);
        Assert.AreEqual(GrammarAnalysisProvider.GoogleCloudNaturalLanguage, loaded.GrammarProvider);
        Assert.AreEqual(PythonGrammarTransport.SharedMemory, loaded.PythonTransport);
        Assert.AreEqual(5, loaded.DuplicateThreshold);
        Assert.AreEqual("#123456", loaded.GrammarColors[GrammarCategory.Verb]);
        Assert.AreEqual(1, loaded.RecentFiles.Count);
    }

    [TestMethod]
    public async Task Load_CorruptedSettingsReturnsDefaultsAndPreservesBadFile()
    {
        var service = new SettingsService(_directory);
        await File.WriteAllTextAsync(service.SettingsPath, "{ not-json }");

        var loaded = await service.LoadAsync();

        Assert.AreEqual(AppSettings.CreateDefaults().DefaultFontFamily, loaded.DefaultFontFamily);
        Assert.IsFalse(string.IsNullOrWhiteSpace(service.LastLoadWarning));
        Assert.AreEqual(1, Directory.EnumerateFiles(_directory, "settings.corrupt.*.json").Count());
    }

    [TestMethod]
    public void Normalize_ClampsInvalidNumericValuesAndRestoresGrammarColors()
    {
        var settings = new AppSettings
        {
            DefaultFontSize = 500,
            AutoSaveIntervalSeconds = 1,
            DuplicateThreshold = 500,
            GrammarMode = (GrammarAnalysisMode)999,
            GrammarProvider = (GrammarAnalysisProvider)999,
            PythonTransport = (PythonGrammarTransport)999,
            GrammarColors = new Dictionary<GrammarCategory, string>()
        };

        settings.Normalize();

        Assert.AreEqual(144d, settings.DefaultFontSize);
        Assert.AreEqual(5, settings.AutoSaveIntervalSeconds);
        Assert.AreEqual(100, settings.DuplicateThreshold);
        Assert.AreEqual(GrammarAnalysisMode.Traditional, settings.GrammarMode);
        Assert.AreEqual(GrammarAnalysisProvider.PythonSpacy, settings.GrammarProvider);
        Assert.AreEqual(PythonGrammarTransport.NamedPipes, settings.PythonTransport);
        Assert.IsTrue(settings.GrammarColors.ContainsKey(GrammarCategory.Verb));
    }

    [TestMethod]
    public async Task Load_LegacyAiGrammarModeMapsToOpenAi()
    {
        var service = new SettingsService(_directory);
        await File.WriteAllTextAsync(service.SettingsPath, """{"GrammarMode":"AI"}""");

        var loaded = await service.LoadAsync();

        Assert.AreEqual(GrammarAnalysisMode.OpenAI, loaded.GrammarMode);
        Assert.IsNull(service.LastLoadWarning);
    }

    [TestMethod]
    public async Task Load_IntermediateProviderModeMapsToDirectProviderMode()
    {
        var service = new SettingsService(_directory);
        await File.WriteAllTextAsync(
            service.SettingsPath,
            """{"GrammarMode":"Provider","GrammarProvider":"PythonNltk","PythonTransport":"SharedMemory"}""");

        var loaded = await service.LoadAsync();

        Assert.AreEqual(GrammarAnalysisMode.PythonNltk, loaded.GrammarMode);
        Assert.AreEqual(GrammarAnalysisProvider.PythonNltk, loaded.GrammarProvider);
        Assert.AreEqual(PythonGrammarTransport.SharedMemory, loaded.PythonTransport);
        Assert.IsNull(service.LastLoadWarning);
    }

    [TestMethod]
    public async Task SaveAndLoad_OpenAiModePersists()
    {
        var service = new SettingsService(_directory);
        var settings = AppSettings.CreateDefaults();
        settings.GrammarMode = GrammarAnalysisMode.OpenAI;

        await service.SaveAsync(settings);
        var savedJson = await File.ReadAllTextAsync(service.SettingsPath);
        var loaded = await service.LoadAsync();

        StringAssert.Contains(savedJson, "\"GrammarMode\": \"OpenAI\"");
        Assert.AreEqual(GrammarAnalysisMode.OpenAI, loaded.GrammarMode);
    }

    [TestMethod]
    public async Task Load_NullCollectionsAreNormalizedSafely()
    {
        var service = new SettingsService(_directory);
        await File.WriteAllTextAsync(
            service.SettingsPath,
            """{"RecentFiles":null,"IgnoredWarningIds":null,"GrammarColors":null}""");

        var loaded = await service.LoadAsync();

        Assert.IsNotNull(loaded.RecentFiles);
        Assert.IsNotNull(loaded.IgnoredWarningIds);
        Assert.IsTrue(loaded.GrammarColors.ContainsKey(GrammarCategory.Verb));
        Assert.IsNull(service.LastLoadWarning);
    }

}
