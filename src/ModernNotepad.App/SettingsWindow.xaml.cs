using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ModernNotepad.Core.Analysis;
using ModernNotepad.Core.Models;
using AppThemeMode = ModernNotepad.Core.Models.ThemeMode;

namespace ModernNotepad.App;

public partial class SettingsWindow : Window
{
    private AppSettings _workingSettings;
    private bool _loading;

    public SettingsWindow(AppSettings settings)
    {
        _workingSettings = settings ?? throw new ArgumentNullException(nameof(settings));
        ResultSettings = _workingSettings.Clone();
        InitializeComponent();
        PopulateFonts();
        LoadSettings();
    }

    public AppSettings ResultSettings { get; private set; }

    private void PopulateFonts()
    {
        try
        {
            DefaultFontCombo.ItemsSource = Fonts.SystemFontFamilies
                .Select(font => font.Source)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch
        {
            DefaultFontCombo.ItemsSource = new[] { "Segoe UI", "Calibri", "Arial", "Consolas" };
        }
    }

    private void LoadSettings()
    {
        _loading = true;
        try
        {
            var settings = _workingSettings;
            DefaultFontCombo.Text = settings.DefaultFontFamily;
            DefaultFontSizeTextBox.Text = settings.DefaultFontSize.ToString("0.#", CultureInfo.CurrentCulture);
            SelectComboByTag(DefaultFormatCombo, settings.DefaultFileFormat);
            AutoSaveTextBox.Text = settings.AutoSaveIntervalSeconds.ToString(CultureInfo.CurrentCulture);
            SelectComboByTag(SpellLanguageCombo, settings.SpellCheckLanguage);
            if (SpellLanguageCombo.SelectedItem is null)
            {
                SpellLanguageCombo.Text = settings.SpellCheckLanguage;
            }

            WordWrapCheckBox.IsChecked = settings.WordWrap;
            TabsCheckBox.IsChecked = settings.TabsEnabled;
            RestoreSessionCheckBox.IsChecked = settings.RestorePreviousSession;

            SelectComboByTag(ThemeCombo, settings.Theme.ToString());
            AccentColorTextBox.Text = settings.AccentColor;

            SmartColoringCheckBox.IsChecked = settings.SmartColoringEnabled;
            SelectComboByTag(GrammarModeCombo, AnalysisCoordinator.ResolveConfiguredMode(settings).ToString());
            SelectComboByTag(PythonTransportCombo, settings.PythonTransport.ToString());
            UpdateGrammarModeControls();
            DuplicateDetectionCheckBox.IsChecked = settings.DuplicateDetectionEnabled;
            SmartPanelCheckBox.IsChecked = settings.SmartPanelVisible;
            DuplicateThresholdTextBox.Text = settings.DuplicateThreshold.ToString(CultureInfo.CurrentCulture);
            DuplicateColorTextBox.Text = settings.DuplicateHighlightColor;
            StrictDuplicatesCheckBox.IsChecked = settings.StrictDuplicateChecking;
            LongSentenceTextBox.Text = settings.LongSentenceWordThreshold.ToString(CultureInfo.CurrentCulture);
            PassiveVoiceCheckBox.IsChecked = settings.PassiveVoiceDetectionEnabled;
            MaxSpansTextBox.Text = settings.MaxVisualAnalysisSpans.ToString(CultureInfo.CurrentCulture);

            SubjectColorTextBox.Text = GetGrammarColor(settings, GrammarCategory.SubjectNoun);
            VerbColorTextBox.Text = GetGrammarColor(settings, GrammarCategory.Verb);
            ObjectColorTextBox.Text = GetGrammarColor(settings, GrammarCategory.ObjectNoun);
            AdjectiveColorTextBox.Text = GetGrammarColor(settings, GrammarCategory.Adjective);
            AdverbColorTextBox.Text = GetGrammarColor(settings, GrammarCategory.Adverb);
            PronounColorTextBox.Text = GetGrammarColor(settings, GrammarCategory.Pronoun);
            PrepositionColorTextBox.Text = GetGrammarColor(settings, GrammarCategory.Preposition);
            ConjunctionColorTextBox.Text = GetGrammarColor(settings, GrammarCategory.Conjunction);
            InterrogativeColorTextBox.Text = GetGrammarColor(settings, GrammarCategory.Interrogative);
            QuantifierColorTextBox.Text = GetGrammarColor(settings, GrammarCategory.Quantifier);
            DeterminerColorTextBox.Text = GetGrammarColor(settings, GrammarCategory.Determiner);
            ParticleColorTextBox.Text = GetGrammarColor(settings, GrammarCategory.Particle);
            UpdateAccentPreview();
        }
        finally
        {
            _loading = false;
        }
    }

    private void SettingsNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GeneralPage is null || AppearancePage is null || SmartFeaturesPage is null)
        {
            return;
        }

        var tag = (SettingsNavList.SelectedItem as ListBoxItem)?.Tag as string ?? "General";
        GeneralPage.Visibility = tag == "General" ? Visibility.Visible : Visibility.Collapsed;
        AppearancePage.Visibility = tag == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        SmartFeaturesPage.Visibility = tag == "Smart" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void GrammarModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            UpdateGrammarModeControls();
        }
    }

    private void UpdateGrammarModeControls()
    {
        if (GrammarModeCombo is null || PythonTransportSettingsPanel is null)
        {
            return;
        }

        var mode = Enum.TryParse<GrammarAnalysisMode>(
            ComboTagOrText(GrammarModeCombo),
            ignoreCase: true,
            out var selectedMode)
                ? selectedMode
                : GrammarAnalysisMode.Traditional;
        PythonTransportSettingsPanel.Visibility = mode is GrammarAnalysisMode.PythonSpacy
            or GrammarAnalysisMode.PythonNltk
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private static string GetGrammarColor(AppSettings settings, GrammarCategory category) =>
        settings.GrammarColors.TryGetValue(category, out var color) ? color : "#667085";

    private static void SelectComboByTag(ComboBox combo, string value)
    {
        combo.SelectedItem = combo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(
                Convert.ToString(item.Tag, CultureInfo.InvariantCulture),
                value,
                StringComparison.OrdinalIgnoreCase));
    }

    private static string ComboTagOrText(ComboBox combo)
    {
        if (combo.SelectedItem is ComboBoxItem item && item.Tag is not null)
        {
            return Convert.ToString(item.Tag, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return combo.Text.Trim();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadDouble(DefaultFontSizeTextBox, 6, 144, "Default font size", out var fontSize)
            || !TryReadInt(AutoSaveTextBox, 5, 3600, "Auto-save interval", out var autoSave)
            || !TryReadInt(DuplicateThresholdTextBox, 2, 100, "Duplicate threshold", out var duplicateThreshold)
            || !TryReadInt(LongSentenceTextBox, 10, 200, "Long-sentence threshold", out var longSentence)
            || !TryReadInt(MaxSpansTextBox, 100, 10000, "Maximum visual highlights", out var maxSpans))
        {
            return;
        }

        var colorInputs = new (TextBox Box, string Label)[]
        {
            (AccentColorTextBox, "Accent color"),
            (DuplicateColorTextBox, "Duplicate highlight color"),
            (SubjectColorTextBox, "Subject color"),
            (VerbColorTextBox, "Verb color"),
            (ObjectColorTextBox, "Object color"),
            (AdjectiveColorTextBox, "Adjective color"),
            (AdverbColorTextBox, "Adverb color"),
            (PronounColorTextBox, "Pronoun color"),
            (PrepositionColorTextBox, "Preposition color"),
            (ConjunctionColorTextBox, "Conjunction color"),
            (InterrogativeColorTextBox, "Interrogative color"),
            (QuantifierColorTextBox, "Quantifier color"),
            (DeterminerColorTextBox, "Determiner color"),
            (ParticleColorTextBox, "Particle color")
        };
        foreach (var input in colorInputs)
        {
            if (!IsValidColor(input.Box.Text))
            {
                MessageBox.Show(
                    this,
                    $"{input.Label} must be a valid #RRGGBB or #AARRGGBB color.",
                    "Invalid setting",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                input.Box.Focus();
                input.Box.SelectAll();
                return;
            }
        }

        var fontFamily = DefaultFontCombo.Text.Trim();
        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            MessageBox.Show(
                this,
                "Choose a default font family.",
                "Invalid setting",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            DefaultFontCombo.Focus();
            return;
        }

        var spellLanguage = ComboTagOrText(SpellLanguageCombo);
        if (string.IsNullOrWhiteSpace(spellLanguage))
        {
            spellLanguage = "en-US";
        }

        var result = _workingSettings.Clone();
        result.DefaultFontFamily = fontFamily;
        result.DefaultFontSize = fontSize;
        result.DefaultFileFormat = ComboTagOrText(DefaultFormatCombo);
        result.AutoSaveIntervalSeconds = autoSave;
        result.SpellCheckLanguage = spellLanguage;
        result.WordWrap = WordWrapCheckBox.IsChecked == true;
        result.TabsEnabled = TabsCheckBox.IsChecked == true;
        result.RestorePreviousSession = RestoreSessionCheckBox.IsChecked == true;
        result.Theme = string.Equals(ComboTagOrText(ThemeCombo), "Dark", StringComparison.OrdinalIgnoreCase)
            ? AppThemeMode.Dark
            : AppThemeMode.Light;
        result.AccentColor = AccentColorTextBox.Text.Trim();

        result.SmartColoringEnabled = SmartColoringCheckBox.IsChecked == true;
        result.GrammarMode = Enum.TryParse<GrammarAnalysisMode>(
            ComboTagOrText(GrammarModeCombo),
            ignoreCase: true,
            out var grammarMode)
                && grammarMode != GrammarAnalysisMode.Provider
                    ? grammarMode
                    : GrammarAnalysisMode.Traditional;
        result.PythonTransport = Enum.TryParse<PythonGrammarTransport>(
            ComboTagOrText(PythonTransportCombo),
            ignoreCase: true,
            out var pythonTransport)
                ? pythonTransport
                : PythonGrammarTransport.NamedPipes;
        result.DuplicateDetectionEnabled = DuplicateDetectionCheckBox.IsChecked == true;
        result.SmartPanelVisible = SmartPanelCheckBox.IsChecked == true;
        result.DuplicateThreshold = duplicateThreshold;
        result.DuplicateHighlightColor = DuplicateColorTextBox.Text.Trim();
        result.StrictDuplicateChecking = StrictDuplicatesCheckBox.IsChecked == true;
        result.LongSentenceWordThreshold = longSentence;
        result.PassiveVoiceDetectionEnabled = PassiveVoiceCheckBox.IsChecked == true;
        result.MaxVisualAnalysisSpans = maxSpans;
        result.GrammarColors[GrammarCategory.SubjectNoun] = SubjectColorTextBox.Text.Trim();
        result.GrammarColors[GrammarCategory.Verb] = VerbColorTextBox.Text.Trim();
        result.GrammarColors[GrammarCategory.ObjectNoun] = ObjectColorTextBox.Text.Trim();
        result.GrammarColors[GrammarCategory.Adjective] = AdjectiveColorTextBox.Text.Trim();
        result.GrammarColors[GrammarCategory.Adverb] = AdverbColorTextBox.Text.Trim();
        result.GrammarColors[GrammarCategory.Pronoun] = PronounColorTextBox.Text.Trim();
        result.GrammarColors[GrammarCategory.Preposition] = PrepositionColorTextBox.Text.Trim();
        result.GrammarColors[GrammarCategory.Conjunction] = ConjunctionColorTextBox.Text.Trim();
        result.GrammarColors[GrammarCategory.Interrogative] = InterrogativeColorTextBox.Text.Trim();
        result.GrammarColors[GrammarCategory.Quantifier] = QuantifierColorTextBox.Text.Trim();
        result.GrammarColors[GrammarCategory.Determiner] = DeterminerColorTextBox.Text.Trim();
        result.GrammarColors[GrammarCategory.Particle] = ParticleColorTextBox.Text.Trim();
        result.Normalize();

        ResultSettings = result;
        DialogResult = true;
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            "Restore all settings on this screen to their defaults?",
            "Restore defaults",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var defaults = AppSettings.CreateDefaults();
        defaults.RecentFiles = [.. _workingSettings.RecentFiles];
        defaults.IgnoredWarningIds = new HashSet<string>(
            _workingSettings.IgnoredWarningIds,
            StringComparer.Ordinal);
        _workingSettings = defaults;
        LoadSettings();
    }

    private static bool TryReadInt(
        TextBox textBox,
        int minimum,
        int maximum,
        string label,
        out int value)
    {
        if (int.TryParse(textBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value)
            && value >= minimum
            && value <= maximum)
        {
            return true;
        }

        MessageBox.Show(
            Window.GetWindow(textBox),
            $"{label} must be a whole number from {minimum} to {maximum}.",
            "Invalid setting",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        textBox.Focus();
        textBox.SelectAll();
        return false;
    }

    private static bool TryReadDouble(
        TextBox textBox,
        double minimum,
        double maximum,
        string label,
        out double value)
    {
        var parsed = double.TryParse(textBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            || double.TryParse(textBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        if (parsed && double.IsFinite(value) && value >= minimum && value <= maximum)
        {
            return true;
        }

        MessageBox.Show(
            Window.GetWindow(textBox),
            $"{label} must be a number from {minimum} to {maximum}.",
            "Invalid setting",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        textBox.Focus();
        textBox.SelectAll();
        return false;
    }

    private static bool IsValidColor(string text)
    {
        try
        {
            _ = (Color)ColorConverter.ConvertFromString(text.Trim());
            return text.Trim().StartsWith('#') && text.Trim().Length is 7 or 9;
        }
        catch
        {
            return false;
        }
    }

    private void ColorTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading && ReferenceEquals(sender, AccentColorTextBox))
        {
            UpdateAccentPreview();
        }
    }

    private void UpdateAccentPreview()
    {
        try
        {
            AccentPreview.Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(AccentColorTextBox.Text.Trim()));
        }
        catch
        {
            AccentPreview.Background = Brushes.Transparent;
        }
    }
}
