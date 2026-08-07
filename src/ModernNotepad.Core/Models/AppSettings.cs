using ModernNotepad.Core.Analysis;

namespace ModernNotepad.Core.Models;

public enum ThemeMode
{
    Light,
    Dark
}

public enum GrammarAnalysisMode
{
    Traditional,
    AI
}

public sealed class AppSettings
{
    public string DefaultFontFamily { get; set; } = "Segoe UI";
    public double DefaultFontSize { get; set; } = 16;
    public ThemeMode Theme { get; set; } = ThemeMode.Light;
    public string AccentColor { get; set; } = "#4F6BED";
    public int AutoSaveIntervalSeconds { get; set; } = 30;
    public bool SmartColoringEnabled { get; set; }
    public GrammarAnalysisMode GrammarAnalysisMode { get; set; } = GrammarAnalysisMode.Traditional;
    public bool DuplicateDetectionEnabled { get; set; }
    public int DuplicateThreshold { get; set; } = 3;
    public bool StrictDuplicateChecking { get; set; }
    public string DuplicateHighlightColor { get; set; } = "#FFF3A3";
    public string SpellCheckLanguage { get; set; } = "en-US";
    public string DefaultFileFormat { get; set; } = ".txt";
    public bool WordWrap { get; set; } = true;
    public bool TabsEnabled { get; set; } = true;
    public bool RestorePreviousSession { get; set; } = true;
    public bool SmartPanelVisible { get; set; } = true;
    public int LongSentenceWordThreshold { get; set; } = 30;
    public bool PassiveVoiceDetectionEnabled { get; set; } = true;
    public int MaxVisualAnalysisSpans { get; set; } = 2500;
    public List<string> RecentFiles { get; set; } = [];
    public HashSet<string> IgnoredWarningIds { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<GrammarCategory, string> GrammarColors { get; set; } = new()
    {
        [GrammarCategory.SubjectNoun] = "#2563EB",
        [GrammarCategory.Verb] = "#DC2626",
        [GrammarCategory.ObjectNoun] = "#7C3AED",
        [GrammarCategory.Adjective] = "#D97706",
        [GrammarCategory.Adverb] = "#059669",
        [GrammarCategory.Pronoun] = "#DB2777",
        [GrammarCategory.Preposition] = "#0891B2",
        [GrammarCategory.Conjunction] = "#6B7280",
        [GrammarCategory.Interrogative] = "#F97316",
        [GrammarCategory.Quantifier] = "#8B5CF6"
    };

    public void Normalize()
    {
        DefaultFontFamily = string.IsNullOrWhiteSpace(DefaultFontFamily)
            ? "Segoe UI"
            : DefaultFontFamily.Trim();
        DefaultFontSize = Math.Clamp(DefaultFontSize, 6, 144);
        AutoSaveIntervalSeconds = Math.Clamp(AutoSaveIntervalSeconds, 5, 3600);
        DuplicateThreshold = Math.Clamp(DuplicateThreshold, 2, 100);
        LongSentenceWordThreshold = Math.Clamp(LongSentenceWordThreshold, 10, 200);
        MaxVisualAnalysisSpans = Math.Clamp(MaxVisualAnalysisSpans, 100, 10000);
        SpellCheckLanguage = string.IsNullOrWhiteSpace(SpellCheckLanguage)
            ? "en-US"
            : SpellCheckLanguage.Trim();

        var allowedFormats = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".rtf", ".md", ".yaml", ".yml", ".json", ".xml"
        };
        if (!allowedFormats.Contains(DefaultFileFormat))
        {
            DefaultFileFormat = ".txt";
        }

        RecentFiles ??= [];
        RecentFiles = RecentFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        IgnoredWarningIds ??= new HashSet<string>(StringComparer.Ordinal);
        GrammarColors ??= new Dictionary<GrammarCategory, string>();

        foreach (var pair in CreateDefaults().GrammarColors)
        {
            GrammarColors.TryAdd(pair.Key, pair.Value);
        }
    }

    public AppSettings Clone()
    {
        return new AppSettings
        {
            DefaultFontFamily = DefaultFontFamily,
            DefaultFontSize = DefaultFontSize,
            Theme = Theme,
            AccentColor = AccentColor,
            AutoSaveIntervalSeconds = AutoSaveIntervalSeconds,
            SmartColoringEnabled = SmartColoringEnabled,
            GrammarAnalysisMode = GrammarAnalysisMode,
            DuplicateDetectionEnabled = DuplicateDetectionEnabled,
            DuplicateThreshold = DuplicateThreshold,
            StrictDuplicateChecking = StrictDuplicateChecking,
            DuplicateHighlightColor = DuplicateHighlightColor,
            SpellCheckLanguage = SpellCheckLanguage,
            DefaultFileFormat = DefaultFileFormat,
            WordWrap = WordWrap,
            TabsEnabled = TabsEnabled,
            RestorePreviousSession = RestorePreviousSession,
            SmartPanelVisible = SmartPanelVisible,
            LongSentenceWordThreshold = LongSentenceWordThreshold,
            PassiveVoiceDetectionEnabled = PassiveVoiceDetectionEnabled,
            MaxVisualAnalysisSpans = MaxVisualAnalysisSpans,
            RecentFiles = [.. RecentFiles],
            IgnoredWarningIds = new HashSet<string>(IgnoredWarningIds, StringComparer.Ordinal),
            GrammarColors = GrammarColors.ToDictionary(pair => pair.Key, pair => pair.Value)
        };
    }

    public void CopyFrom(AppSettings source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var copy = source.Clone();

        DefaultFontFamily = copy.DefaultFontFamily;
        DefaultFontSize = copy.DefaultFontSize;
        Theme = copy.Theme;
        AccentColor = copy.AccentColor;
        AutoSaveIntervalSeconds = copy.AutoSaveIntervalSeconds;
        SmartColoringEnabled = copy.SmartColoringEnabled;
        GrammarAnalysisMode = copy.GrammarAnalysisMode;
        DuplicateDetectionEnabled = copy.DuplicateDetectionEnabled;
        DuplicateThreshold = copy.DuplicateThreshold;
        StrictDuplicateChecking = copy.StrictDuplicateChecking;
        DuplicateHighlightColor = copy.DuplicateHighlightColor;
        SpellCheckLanguage = copy.SpellCheckLanguage;
        DefaultFileFormat = copy.DefaultFileFormat;
        WordWrap = copy.WordWrap;
        TabsEnabled = copy.TabsEnabled;
        RestorePreviousSession = copy.RestorePreviousSession;
        SmartPanelVisible = copy.SmartPanelVisible;
        LongSentenceWordThreshold = copy.LongSentenceWordThreshold;
        PassiveVoiceDetectionEnabled = copy.PassiveVoiceDetectionEnabled;
        MaxVisualAnalysisSpans = copy.MaxVisualAnalysisSpans;
        RecentFiles = copy.RecentFiles;
        IgnoredWarningIds = copy.IgnoredWarningIds;
        GrammarColors = copy.GrammarColors;
        Normalize();
    }

    public static AppSettings CreateDefaults() => new();
}
