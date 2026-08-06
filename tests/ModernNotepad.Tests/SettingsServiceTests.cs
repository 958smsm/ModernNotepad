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
        settings.DuplicateThreshold = 5;
        settings.GrammarColors[GrammarCategory.Verb] = "#123456";
        settings.RecentFiles.Add(Path.Combine(_directory, "sample.txt"));

        await service.SaveAsync(settings);
        var loaded = await service.LoadAsync();

        Assert.AreEqual(ThemeMode.Dark, loaded.Theme);
        Assert.AreEqual("Consolas", loaded.DefaultFontFamily);
        Assert.AreEqual(18d, loaded.DefaultFontSize);
        Assert.IsTrue(loaded.SmartColoringEnabled);
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
            GrammarColors = new Dictionary<GrammarCategory, string>()
        };

        settings.Normalize();

        Assert.AreEqual(144d, settings.DefaultFontSize);
        Assert.AreEqual(5, settings.AutoSaveIntervalSeconds);
        Assert.AreEqual(100, settings.DuplicateThreshold);
        Assert.IsTrue(settings.GrammarColors.ContainsKey(GrammarCategory.Verb));
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
