using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using ModernNotepad.App.Commands;
using ModernNotepad.App.Models;
using ModernNotepad.App.Services;
using ModernNotepad.App.Views;
using ModernNotepad.Core.Analysis;
using ModernNotepad.Core.Models;
using ModernNotepad.Core.Services;
using AppThemeMode = ModernNotepad.Core.Models.ThemeMode;

namespace ModernNotepad.App;

public partial class MainWindow : Window
{
    private static readonly double[] CommonFontSizes =
    [
        8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 28, 32, 36, 48, 64, 72, 96
    ];

    private static readonly (string Name, string Value)[] ColorPalette =
    [
        ("Black", "#000000"),
        ("Slate", "#475467"),
        ("Red", "#D92D20"),
        ("Orange", "#E04F16"),
        ("Amber", "#DC8A00"),
        ("Green", "#079455"),
        ("Teal", "#0E9384"),
        ("Blue", "#1570EF"),
        ("Indigo", "#4F46E5"),
        ("Purple", "#7F56D9"),
        ("Pink", "#DD2590"),
        ("White", "#FFFFFF")
    ];

    private readonly DispatcherTimer _autoSaveTimer;
    private bool _autoSaveRunning;
    private bool _allowWindowClose;
    private bool _windowCloseInProgress;
    private bool _updatingFormatControls;
    private bool _updatingFeatureToggles;
    private int _busyDepth;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        FontSizeCombo.ItemsSource = CommonFontSizes;
        _autoSaveTimer = new DispatcherTimer(DispatcherPriority.Background);
        _autoSaveTimer.Tick += AutoSaveTimer_Tick;
        SizeChanged += (_, _) => UpdateSmartPanelLayout();
    }

    public ObservableCollection<DocumentSession> Documents { get; } = [];

    private AppServices Services => ((App)Application.Current).Services;

    // Routed-command CanExecute queries can run while InitializeComponent is still
    // constructing the visual tree. Named XAML fields such as DocumentTabs are null
    // during that window, so startup-time command queries must be null-safe.
    private DocumentSession? ActiveSession => DocumentTabs?.SelectedItem as DocumentSession;

    private EditorDocumentView? ActiveView => ActiveSession?.View;

    public async Task InitializeAsync(IReadOnlyList<string> commandLinePaths)
    {
        PopulateFontFamilies();
        ApplySettingsToShell();
        ConfigureAutoSaveTimer();
        SetBusy(true, "Restoring your workspace…");

        try
        {
            await RestoreRecoveryDocumentsAsync();

            if (commandLinePaths.Count > 0)
            {
                foreach (var path in commandLinePaths.Where(File.Exists))
                {
                    await OpenFileAsync(path, enforceSingleDocumentMode: false);
                }
            }
            else if (Services.Settings.RestorePreviousSession)
            {
                await RestorePreviousSessionAsync();
            }

            if (Documents.Count == 0)
            {
                AddNewDocument(select: true);
            }

            UpdateRecentFilesMenu();
            UpdateShellForActiveDocument();
            OperationStatusText.Text = Documents.Any(document => document.IsRecovered)
                ? "Unsaved work was recovered. Review and save it when ready."
                : "Ready";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void PopulateFontFamilies()
    {
        try
        {
            FontFamilyCombo.ItemsSource = Fonts.SystemFontFamilies
                .Select(font => font.Source)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch
        {
            FontFamilyCombo.ItemsSource = new[] { "Segoe UI", "Calibri", "Arial", "Consolas" };
        }
    }

    private void ApplySettingsToShell(bool scheduleAnalysis = true)
    {
        _updatingFeatureToggles = true;
        try
        {
            var settings = Services.Settings;
            WordWrapMenuItem.IsChecked = settings.WordWrap;
            SmartPanelMenuItem.IsChecked = settings.SmartPanelVisible;
            DarkModeMenuItem.IsChecked = settings.Theme == AppThemeMode.Dark;
            DarkModeToggle.IsChecked = settings.Theme == AppThemeMode.Dark;
            SmartColoringMenuItem.IsChecked = settings.SmartColoringEnabled;
            SmartColorToggle.IsChecked = settings.SmartColoringEnabled;
            var aiGrammar = settings.GrammarMode == GrammarAnalysisMode.AI;
            GrammarAnalysisModeToggle.IsChecked = aiGrammar;
            GrammarAnalysisModeToggleText.Text = aiGrammar
                ? $"AI · {OpenAiGrammarAnalyzer.Model}"
                : "Logic & Traditional NLP";
            GrammarAnalysisModeHint.Text = aiGrammar
                ? "Uses OpenAI for grammar categories; sends document text to the API and requires OPENAI_API_KEY."
                : "Runs locally on this device with the existing grammar logic.";
            DuplicateDetectionMenuItem.IsChecked = settings.DuplicateDetectionEnabled;
            DuplicateToggle.IsChecked = settings.DuplicateDetectionEnabled;
        }
        finally
        {
            _updatingFeatureToggles = false;
        }

        SmartPanel.Visibility = Services.Settings.SmartPanelVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateSmartPanelLayout();

        foreach (var session in Documents)
        {
            session.View?.ApplySettings(scheduleAnalysis);
        }
    }

    private void UpdateSmartPanelLayout()
    {
        if (!Services.Settings.SmartPanelVisible)
        {
            SmartPanelColumn.Width = new GridLength(0);
            return;
        }

        var desired = ActualWidth < 980 ? 270d : Math.Min(340d, Math.Max(300d, ActualWidth * 0.27));
        SmartPanelColumn.Width = new GridLength(desired);
    }

    private void ConfigureAutoSaveTimer()
    {
        _autoSaveTimer.Stop();
        _autoSaveTimer.Interval = TimeSpan.FromSeconds(Services.Settings.AutoSaveIntervalSeconds);
        _autoSaveTimer.Start();
    }

    private async Task RestoreRecoveryDocumentsAsync()
    {
        var records = await Services.RecoveryService.LoadSnapshotsAsync();
        foreach (var record in records)
        {
            try
            {
                var document = record.IsRichText
                    ? DocumentFactory.FromRtf(record.Content, Services.Settings)
                    : await DocumentFactory.FromPlainTextAsync(
                        Encoding.UTF8.GetString(record.Content),
                        Services.Settings);

                var session = new DocumentSession(
                    document,
                    record.OriginalPath,
                    record.Format,
                    record.Encoding,
                    record.LineEndings,
                    record.RecoveryId)
                {
                    IsRecovered = true,
                    SourceLastWriteTimeUtc = record.OriginalPath is not null && File.Exists(record.OriginalPath)
                        ? File.GetLastWriteTimeUtc(record.OriginalPath)
                        : null
                };
                session.MarkDirty();
                Documents.Add(session);
            }
            catch
            {
                // One damaged recovery entry must not prevent the remaining entries from loading.
            }
        }

        if (Documents.Count > 0)
        {
            DocumentTabs.SelectedIndex = Documents.Count - 1;
        }
    }

    private async Task RestorePreviousSessionAsync()
    {
        var state = await Services.SessionService.LoadAsync();
        foreach (var path in state.OpenFilePaths.Where(File.Exists))
        {
            if (Documents.Any(document =>
                    string.Equals(document.FilePath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            await OpenFileAsync(path, enforceSingleDocumentMode: false, showErrors: false);
        }

        if (!string.IsNullOrWhiteSpace(state.SelectedFilePath))
        {
            var selected = Documents.FirstOrDefault(document =>
                string.Equals(document.FilePath, state.SelectedFilePath, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                DocumentTabs.SelectedItem = selected;
            }
        }
    }

    private DocumentSession AddNewDocument(bool select)
    {
        var format = DocumentFormatExtensions.FromPath("untitled" + Services.Settings.DefaultFileFormat);
        var session = new DocumentSession(
            DocumentFactory.CreateEmpty(Services.Settings),
            null,
            format,
            TextEncodingInfo.Utf8NoBom,
            LineEndingProfile.WindowsDefault);
        Documents.Add(session);

        if (select)
        {
            DocumentTabs.SelectedItem = session;
        }

        return session;
    }

    private async Task<bool> EnsureRoomForAnotherDocumentAsync()
    {
        if (Services.Settings.TabsEnabled || Documents.Count == 0)
        {
            return true;
        }

        return ActiveSession is null || await CloseSessionAsync(ActiveSession, createReplacement: false);
    }

    private async Task OpenFileAsync(
        string path,
        bool enforceSingleDocumentMode = true,
        bool showErrors = true)
    {
        var busyStarted = false;
        try
        {
            var fullPath = Path.GetFullPath(path);
            var existing = Documents.FirstOrDefault(document =>
                string.Equals(document.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                DocumentTabs.SelectedItem = existing;
                return;
            }

            if (enforceSingleDocumentMode && !await EnsureRoomForAnotherDocumentAsync())
            {
                return;
            }

            SetBusy(true, $"Opening {Path.GetFileName(fullPath)}…");
            busyStarted = true;
            var loaded = await Services.FileService.LoadAsync(fullPath);
            var document = loaded.IsRichText
                ? DocumentFactory.FromRtf(
                    loaded.RichTextBytes ?? throw new InvalidDataException("The RTF file contained no data."),
                    Services.Settings)
                : await DocumentFactory.FromPlainTextAsync(loaded.Text ?? string.Empty, Services.Settings);

            var session = new DocumentSession(
                document,
                loaded.Path,
                loaded.Format,
                loaded.Encoding,
                loaded.LineEndings)
            {
                SourceLastWriteTimeUtc = loaded.LastWriteTimeUtc
            };
            session.MarkSaved(loaded.LastWriteTimeUtc);
            Documents.Add(session);
            DocumentTabs.SelectedItem = session;

            RecentFilesManager.Add(Services.Settings, loaded.Path);
            await Services.SaveSettingsAsync();
            UpdateRecentFilesMenu();
            OperationStatusText.Text = $"Opened {Path.GetFileName(loaded.Path)}";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or DecoderFallbackException
                or InvalidDataException
                or ArgumentException)
        {
            if (showErrors)
            {
                ShowError(
                    "The file could not be opened.",
                    exception,
                    "Check that the file exists, is not locked, and that you have permission to read it.");
            }
        }
        finally
        {
            if (busyStarted)
            {
                SetBusy(false);
            }
        }
    }

    private async Task<bool> SaveSessionAsync(DocumentSession session, bool forceSaveAs)
    {
        ArgumentNullException.ThrowIfNull(session);
        var targetPath = session.FilePath;
        var targetFormat = session.Format;

        if (forceSaveAs || string.IsNullOrWhiteSpace(targetPath))
        {
            var dialog = CreateSaveDialog(session);
            if (dialog.ShowDialog(this) != true)
            {
                return false;
            }

            targetPath = Path.GetFullPath(EnsureExtensionForFilter(dialog.FileName, dialog.FilterIndex));
            targetFormat = DocumentFormatExtensions.FromPath(targetPath);

            if (session.Format.IsRichText() && !targetFormat.IsRichText())
            {
                var result = MessageBox.Show(
                    this,
                    "Saving as a plain-text format removes font, color, list, and paragraph formatting. Continue?",
                    "Formatting will be removed",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (result != MessageBoxResult.Yes)
                {
                    return false;
                }
            }
        }

        if (targetPath is null)
        {
            return false;
        }

        try
        {
            var sourceLastWriteTimeUtc = session.SourceLastWriteTimeUtc;
            if (!forceSaveAs
                && sourceLastWriteTimeUtc is not null
                && File.Exists(targetPath))
            {
                var currentWriteTime = File.GetLastWriteTimeUtc(targetPath);
                if (Math.Abs((currentWriteTime - sourceLastWriteTimeUtc.Value).TotalSeconds) > 1)
                {
                    var result = MessageBox.Show(
                        this,
                        "This file changed on disk after it was opened. Overwrite the newer disk version?",
                        "File changed externally",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning,
                        MessageBoxResult.No);
                    if (result != MessageBoxResult.Yes)
                    {
                        return false;
                    }
                }
            }

            session.SaveStatus = "Saving…";
            UpdateStatusBar();

            if (targetFormat.IsRichText())
            {
                var bytes = session.View?.GetRtfBytes(includeSmartColoring: true)
                    ?? DocumentFactory.ToRtf(session.Document);
                await Services.FileService.SaveRichTextAsync(targetPath, bytes);
            }
            else
            {
                var text = session.View?.GetPlainText()
                    ?? DocumentTextSnapshot.Create(session.Document).Text;
                try
                {
                    await Services.FileService.SaveTextAsync(
                        targetPath,
                        text,
                        session.Encoding,
                        session.LineEndings);
                }
                catch (EncoderFallbackException)
                {
                    var convert = MessageBox.Show(
                        this,
                        $"The current {session.Encoding} encoding cannot represent one or more characters. Save this document as UTF-8 instead?",
                        "Characters are not supported by the current encoding",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning,
                        MessageBoxResult.Yes);
                    if (convert != MessageBoxResult.Yes)
                    {
                        session.SaveStatus = "Save canceled";
                        UpdateStatusBar();
                        return false;
                    }

                    session.Encoding = TextEncodingInfo.Utf8NoBom;
                    await Services.FileService.SaveTextAsync(
                        targetPath,
                        text,
                        session.Encoding,
                        session.LineEndings);
                }
            }

            session.FilePath = targetPath;
            session.Format = targetFormat;
            session.MarkSaved(File.GetLastWriteTimeUtc(targetPath));
            await Services.RecoveryService.DeleteSnapshotAsync(session.RecoveryId);
            RecentFilesManager.Add(Services.Settings, targetPath);
            await Services.SaveSettingsAsync();
            UpdateRecentFilesMenu();
            UpdateShellForActiveDocument();
            OperationStatusText.Text = $"Saved {Path.GetFileName(targetPath)}";
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or EncoderFallbackException
                or ArgumentException)
        {
            session.SaveStatus = "Save failed";
            UpdateStatusBar();
            ShowError(
                "The document could not be saved.",
                exception,
                "Choose another location or encoding, and check available disk space and permissions.");
            return false;
        }
    }

    private SaveFileDialog CreateSaveDialog(DocumentSession session)
    {
        var currentExtension = session.FilePath is null
            ? session.Format.DefaultExtension()
            : Path.GetExtension(session.FilePath);

        return new SaveFileDialog
        {
            Title = "Save document",
            AddExtension = false,
            OverwritePrompt = true,
            CheckPathExists = true,
            DefaultExt = string.IsNullOrWhiteSpace(currentExtension) ? ".txt" : currentExtension,
            FileName = session.FilePath is null ? "Untitled" : Path.GetFileName(session.FilePath),
            InitialDirectory = session.FilePath is null ? null : Path.GetDirectoryName(session.FilePath),
            Filter = BuildSaveFilter(),
            FilterIndex = FormatToFilterIndex(session.Format)
        };
    }

    private async Task<bool> CloseSessionAsync(DocumentSession session, bool createReplacement)
    {
        if (session.IsDirty)
        {
            var result = AskToSave(session);
            if (result == MessageBoxResult.Cancel)
            {
                return false;
            }

            if (result == MessageBoxResult.Yes && !await SaveSessionAsync(session, forceSaveAs: false))
            {
                return false;
            }

            if (result == MessageBoxResult.No)
            {
                await Services.RecoveryService.DeleteSnapshotAsync(session.RecoveryId);
            }
        }
        else
        {
            await Services.RecoveryService.DeleteSnapshotAsync(session.RecoveryId);
        }

        Documents.Remove(session);
        if (Documents.Count == 0 && createReplacement)
        {
            AddNewDocument(select: true);
        }
        else if (Documents.Count > 0 && DocumentTabs.SelectedItem is null)
        {
            DocumentTabs.SelectedIndex = Math.Max(0, Documents.Count - 1);
        }

        UpdateShellForActiveDocument();
        return true;
    }

    private MessageBoxResult AskToSave(DocumentSession session) => MessageBox.Show(
        this,
        $"Save changes to {session.DisplayName}?",
        "Unsaved changes",
        MessageBoxButton.YesNoCancel,
        MessageBoxImage.Warning,
        MessageBoxResult.Yes);

    private async Task SaveRecoverySnapshotsAsync()
    {
        foreach (var session in Documents.Where(document => document.IsDirty))
        {
            try
            {
                byte[] content;
                var isRtf = session.Format.IsRichText();
                if (isRtf)
                {
                    content = session.View?.GetRtfBytes(includeSmartColoring: false)
                        ?? DocumentFactory.ToRtf(session.Document);
                }
                else
                {
                    var text = session.View?.GetPlainText()
                        ?? DocumentTextSnapshot.Create(session.Document).Text;
                    content = Encoding.UTF8.GetBytes(text);
                }

                var payload = new RecoveryPayload(
                    session.RecoveryId,
                    session.DisplayName,
                    session.FilePath,
                    session.Format,
                    session.Encoding,
                    session.LineEndings,
                    content,
                    isRtf,
                    DateTime.UtcNow);
                await Services.RecoveryService.SaveSnapshotAsync(payload);
                session.SaveStatus = "Recovery saved";
            }
            catch
            {
                session.SaveStatus = "Recovery failed";
            }
        }

        UpdateStatusBar();
    }

    private async Task SaveSessionStateAsync()
    {
        var openPaths = Documents
            .Select(document => document.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var state = new SessionState
        {
            OpenFilePaths = openPaths,
            SelectedFilePath = ActiveSession?.FilePath
        };
        await Services.SessionService.SaveAsync(state);
        await Services.SaveSettingsAsync();
    }

    private void PrepareForFormatting()
    {
        ActiveView?.ClearVisualOverlays();
        ActiveView?.TextEditor.Focus();
    }

    private void ApplyFontFamilyFromControl(bool returnFocusToEditor = true)
    {
        if (_updatingFormatControls || ActiveView is null)
        {
            return;
        }

        var value = FontFamilyCombo.Text.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        try
        {
            if (returnFocusToEditor)
            {
                PrepareForFormatting();
            }
            else
            {
                ActiveView.ClearVisualOverlays();
            }

            RichTextFormattingService.ApplyFontFamily(ActiveView.TextEditor, value);
        }
        catch (ArgumentException)
        {
            OperationStatusText.Text = $"“{value}” is not a valid installed font.";
        }
    }

    private void ApplyFontSizeFromControl(bool returnFocusToEditor = true)
    {
        if (_updatingFormatControls || ActiveView is null)
        {
            return;
        }

        if (!double.TryParse(
                FontSizeCombo.Text,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out var size)
            && !double.TryParse(
                FontSizeCombo.Text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out size))
        {
            OperationStatusText.Text = "Enter a font size between 6 and 144.";
            return;
        }

        if (size is < 6 or > 144)
        {
            OperationStatusText.Text = "Font size must be between 6 and 144.";
            return;
        }

        if (returnFocusToEditor)
        {
            PrepareForFormatting();
        }
        else
        {
            ActiveView.ClearVisualOverlays();
        }

        RichTextFormattingService.ApplyFontSize(ActiveView.TextEditor, size);
    }

    private void UpdateFormattingControls()
    {
        var editor = ActiveView?.TextEditor;
        if (editor is null)
        {
            return;
        }

        _updatingFormatControls = true;
        try
        {
            var family = editor.Selection.GetPropertyValue(TextElement.FontFamilyProperty);
            if (family is FontFamily fontFamily)
            {
                FontFamilyCombo.Text = fontFamily.Source;
            }

            var size = editor.Selection.GetPropertyValue(TextElement.FontSizeProperty);
            if (size is double fontSize && double.IsFinite(fontSize))
            {
                FontSizeCombo.Text = fontSize.ToString("0.#", CultureInfo.CurrentCulture);
            }

            var weight = editor.Selection.GetPropertyValue(TextElement.FontWeightProperty);
            BoldButton.IsChecked = weight is FontWeight fontWeight
                && fontWeight.ToOpenTypeWeight() >= FontWeights.Bold.ToOpenTypeWeight();

            var style = editor.Selection.GetPropertyValue(TextElement.FontStyleProperty);
            ItalicButton.IsChecked = style is FontStyle fontStyle && fontStyle == FontStyles.Italic;

            var decorations = editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty)
                as TextDecorationCollection;
            UnderlineButton.IsChecked = decorations?.Any(item =>
                item.Location == TextDecorationLocation.Underline) == true;
            StrikethroughButton.IsChecked = decorations?.Any(item =>
                item.Location == TextDecorationLocation.Strikethrough) == true;
        }
        finally
        {
            _updatingFormatControls = false;
        }
    }

    private void ShowColorMenu(Button source, bool highlight)
    {
        var menu = new ContextMenu();
        if (highlight)
        {
            var none = new MenuItem { Header = "No highlight" };
            none.Click += (_, _) =>
            {
                if (ActiveView is null)
                {
                    return;
                }

                PrepareForFormatting();
                RichTextFormattingService.ApplyBackground(ActiveView.TextEditor, Brushes.Transparent);
            };
            menu.Items.Add(none);
            menu.Items.Add(new Separator());
        }

        foreach (var color in ColorPalette)
        {
            var brush = new SolidColorBrush(ThemeManager.ParseColor(color.Value, Colors.Black));
            if (brush.CanFreeze)
            {
                brush.Freeze();
            }

            var swatch = new Border
            {
                Width = 14,
                Height = 14,
                Margin = new Thickness(0, 0, 7, 0),
                Background = brush,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1)
            };
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(swatch);
            header.Children.Add(new TextBlock { Text = color.Name, VerticalAlignment = VerticalAlignment.Center });
            var item = new MenuItem { Header = header, Tag = brush };
            item.Click += (_, _) =>
            {
                if (ActiveView is null || item.Tag is not Brush selectedBrush)
                {
                    return;
                }

                PrepareForFormatting();
                if (highlight)
                {
                    RichTextFormattingService.ApplyBackground(ActiveView.TextEditor, selectedBrush);
                }
                else
                {
                    RichTextFormattingService.ApplyForeground(ActiveView.TextEditor, selectedBrush);
                }
            };
            menu.Items.Add(item);
        }

        source.ContextMenu = menu;
        menu.PlacementTarget = source;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void UpdateShellForActiveDocument()
    {
        SmartPanel.DataContext = ActiveSession;
        UpdateStatusBar();
        UpdateFormattingControls();
        UpdateGrammarCounts();
        UpdateTitle();
        CommandManager.InvalidateRequerySuggested();
    }

    private void UpdateStatusBar()
    {
        var session = ActiveSession;
        if (session is null)
        {
            LineColumnText.Text = "Ln 1, Col 1";
            WordCountText.Text = "0 words";
            CharacterCountText.Text = "0 characters";
            ZoomText.Text = "100%";
            EncodingText.Text = "—";
            LineEndingsText.Text = "—";
            SaveStatusText.Text = "—";
            return;
        }

        var position = session.View?.GetLineAndColumn() ?? (1, 1);
        LineColumnText.Text = $"Ln {position.Item1}, Col {position.Item2}";
        WordCountText.Text = $"{session.Statistics.WordCount:N0} words";
        CharacterCountText.Text = $"{session.Statistics.CharacterCount:N0} characters";
        ZoomText.Text = $"{session.ZoomPercent}%";
        EncodingText.Text = session.Format.IsRichText() ? "RTF" : session.Encoding.ToString();
        LineEndingsText.Text = session.Format.IsRichText()
            ? "Rich text"
            : session.LineEndings.HasMixedLineEndings
                ? $"{session.LineEndings.PreferredText} (mixed)"
                : session.LineEndings.PreferredText;
        SaveStatusText.Text = session.SaveStatus;
    }

    private void UpdateTitle()
    {
        Title = ActiveSession is null
            ? "Modern Notepad"
            : $"{ActiveSession.HeaderText} — Modern Notepad";
    }

    private void UpdateGrammarCounts()
    {
        var counts = ActiveSession?.Statistics.GrammarCategoryCounts;
        var settings = Services.Settings;
        GrammarCountsList.ItemsSource = Enum.GetValues<GrammarCategory>()
            .Where(category => category != GrammarCategory.Other)
            .Select(category =>
            {
                var colorHex = settings.GrammarColors.TryGetValue(category, out var c) ? c : "#667085";
                var color = ThemeManager.ParseColor(colorHex, Colors.Gray);
                var brush = new SolidColorBrush(color);
                if (brush.CanFreeze) brush.Freeze();
                return new GrammarCountDisplay(
                    GrammarCategoryDisplayName(category),
                    counts is not null && counts.TryGetValue(category, out var count) ? count : 0,
                    category,
                    brush);
            })
            .ToArray();
    }

    private static string GrammarCategoryDisplayName(GrammarCategory category) => category switch
    {
        GrammarCategory.SubjectNoun => "Subjects / nouns",
        GrammarCategory.ObjectNoun => "Objects / nouns",
        GrammarCategory.Verb => "Verbs",
        GrammarCategory.Adjective => "Adjectives",
        GrammarCategory.Adverb => "Adverbs",
        GrammarCategory.Pronoun => "Pronouns",
        GrammarCategory.Preposition => "Prepositions",
        GrammarCategory.Conjunction => "Conjunctions",
        GrammarCategory.Interrogative => "Interrogatives",
        GrammarCategory.Quantifier => "Quantifiers / determiners",
        _ => "Other"
    };

    private void UpdateRecentFilesMenu()
    {
        RecentFilesMenu.Items.Clear();
        foreach (var path in Services.Settings.RecentFiles)
        {
            var item = new MenuItem
            {
                Header = Path.GetFileName(path),
                ToolTip = path,
                Tag = path
            };
            item.Click += RecentFile_Click;
            RecentFilesMenu.Items.Add(item);
        }

        if (RecentFilesMenu.Items.Count == 0)
        {
            RecentFilesMenu.Items.Add(new MenuItem { Header = "No recent files", IsEnabled = false });
            return;
        }

        RecentFilesMenu.Items.Add(new Separator());
        var clear = new MenuItem { Header = "Clear recent files" };
        clear.Click += async (_, _) =>
        {
            Services.Settings.RecentFiles.Clear();
            await Services.SaveSettingsAsync();
            UpdateRecentFilesMenu();
        };
        RecentFilesMenu.Items.Add(clear);
    }

    private static string BuildOpenFilter() =>
        "Supported documents|*.txt;*.rtf;*.md;*.yaml;*.yml;*.json;*.xml|" +
        "Text files (*.txt)|*.txt|Rich Text Format (*.rtf)|*.rtf|Markdown (*.md)|*.md|" +
        "YAML (*.yaml;*.yml)|*.yaml;*.yml|JSON (*.json)|*.json|XML (*.xml)|*.xml|All files (*.*)|*.*";

    private static string BuildSaveFilter() =>
        "Text files (*.txt)|*.txt|Rich Text Format (*.rtf)|*.rtf|Markdown (*.md)|*.md|" +
        "YAML (*.yaml)|*.yaml|YAML (*.yml)|*.yml|JSON (*.json)|*.json|XML (*.xml)|*.xml";


    private static string EnsureExtensionForFilter(string path, int filterIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!string.IsNullOrWhiteSpace(Path.GetExtension(path)))
        {
            return path;
        }

        var extension = filterIndex switch
        {
            2 => ".rtf",
            3 => ".md",
            4 => ".yaml",
            5 => ".yml",
            6 => ".json",
            7 => ".xml",
            _ => ".txt"
        };
        return path + extension;
    }

    private static int FormatToFilterIndex(DocumentFormat format) => format switch
    {
        DocumentFormat.RichText => 2,
        DocumentFormat.Markdown => 3,
        DocumentFormat.Yaml => 4,
        DocumentFormat.Json => 6,
        DocumentFormat.Xml => 7,
        _ => 1
    };

    private void SetBusy(bool isBusy, string? message = null)
    {
        if (isBusy)
        {
            _busyDepth++;
            BusyText.Text = string.IsNullOrWhiteSpace(message) ? "Working…" : message;
            BusyOverlay.Visibility = Visibility.Visible;
            Mouse.OverrideCursor = Cursors.Wait;
            return;
        }

        _busyDepth = Math.Max(0, _busyDepth - 1);
        if (_busyDepth == 0)
        {
            BusyOverlay.Visibility = Visibility.Collapsed;
            Mouse.OverrideCursor = null;
        }
    }

    private void ShowError(string heading, Exception exception, string guidance)
    {
        MessageBox.Show(
            this,
            $"{heading}\n\n{exception.Message}\n\n{guidance}",
            "Modern Notepad",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private async void AutoSaveTimer_Tick(object? sender, EventArgs e)
    {
        if (_autoSaveRunning)
        {
            return;
        }

        _autoSaveRunning = true;
        try
        {
            await SaveRecoverySnapshotsAsync();
        }
        finally
        {
            _autoSaveRunning = false;
        }
    }

    private async void NewDocument_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (!await EnsureRoomForAnotherDocumentAsync())
        {
            return;
        }

        AddNewDocument(select: true);
        OperationStatusText.Text = "New document";
    }

    private async void Open_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open document",
            Multiselect = Services.Settings.TabsEnabled,
            CheckFileExists = true,
            Filter = BuildOpenFilter()
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (var path in dialog.FileNames)
        {
            await OpenFileAsync(path);
        }
    }

    private async void Save_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (ActiveSession is not null)
        {
            await SaveSessionAsync(ActiveSession, forceSaveAs: false);
        }
    }

    private async void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (ActiveSession is not null)
        {
            await SaveSessionAsync(ActiveSession, forceSaveAs: true);
        }
    }

    private async void CloseDocument_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (ActiveSession is not null)
        {
            await CloseSessionAsync(ActiveSession, createReplacement: true);
        }
    }

    private void Find_Executed(object sender, ExecutedRoutedEventArgs e) => ActiveView?.ShowFind(showReplace: false);
    private void Replace_Executed(object sender, ExecutedRoutedEventArgs e) => ActiveView?.ShowFind(showReplace: true);
    private void FindNext_Executed(object sender, ExecutedRoutedEventArgs e) => ActiveView?.FindNext();
    private void ZoomIn_Executed(object sender, ExecutedRoutedEventArgs e) => ActiveView?.ChangeZoom(10);
    private void ZoomOut_Executed(object sender, ExecutedRoutedEventArgs e) => ActiveView?.ChangeZoom(-10);

    private void ResetZoom_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (ActiveSession is null || ActiveView is null)
        {
            return;
        }

        ActiveSession.ZoomPercent = 100;
        ActiveView.ApplyZoom(100);
        UpdateStatusBar();
    }

    private async void AnalyzeNow_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (ActiveView is null)
        {
            return;
        }

        OperationStatusText.Text = "Analyzing…";
        await ActiveView.AnalyzeNowAsync();
        OperationStatusText.Text = "Analysis updated";
    }

    private void DocumentCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = ActiveSession is not null;

    private void EditorCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = ActiveView is not null;

    private void Bold_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingFormatControls || ActiveView is null)
        {
            return;
        }

        PrepareForFormatting();
        RichTextFormattingService.ToggleBold(ActiveView.TextEditor);
        UpdateFormattingControls();
    }

    private void Italic_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingFormatControls || ActiveView is null)
        {
            return;
        }

        PrepareForFormatting();
        RichTextFormattingService.ToggleItalic(ActiveView.TextEditor);
        UpdateFormattingControls();
    }

    private void Underline_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingFormatControls || ActiveView is null)
        {
            return;
        }

        PrepareForFormatting();
        RichTextFormattingService.ToggleUnderline(ActiveView.TextEditor);
        UpdateFormattingControls();
    }

    private void Strikethrough_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingFormatControls || ActiveView is null)
        {
            return;
        }

        PrepareForFormatting();
        RichTextFormattingService.ToggleStrikethrough(ActiveView.TextEditor);
        UpdateFormattingControls();
    }

    private void AlignLeft_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveView is null) return;
        PrepareForFormatting();
        RichTextFormattingService.AlignLeft(ActiveView.TextEditor);
    }

    private void AlignCenter_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveView is null) return;
        PrepareForFormatting();
        RichTextFormattingService.AlignCenter(ActiveView.TextEditor);
    }

    private void AlignRight_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveView is null) return;
        PrepareForFormatting();
        RichTextFormattingService.AlignRight(ActiveView.TextEditor);
    }

    private void Bullets_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveView is null) return;
        PrepareForFormatting();
        RichTextFormattingService.ToggleBullets(ActiveView.TextEditor);
    }

    private void Numbering_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveView is null) return;
        PrepareForFormatting();
        RichTextFormattingService.ToggleNumbering(ActiveView.TextEditor);
    }

    private void IncreaseIndent_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveView is null) return;
        PrepareForFormatting();
        RichTextFormattingService.IncreaseIndentation(ActiveView.TextEditor);
    }

    private void DecreaseIndent_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveView is null) return;
        PrepareForFormatting();
        RichTextFormattingService.DecreaseIndentation(ActiveView.TextEditor);
    }

    private void ClearFormatting_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveView is null) return;
        PrepareForFormatting();
        RichTextFormattingService.ClearFormatting(
            ActiveView.TextEditor,
            Services.Settings.DefaultFontFamily,
            Services.Settings.DefaultFontSize);
        UpdateFormattingControls();
    }

    private void TextColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            ShowColorMenu(button, highlight: false);
        }
    }

    private void HighlightColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            ShowColorMenu(button, highlight: true);
        }
    }

    private void FontFamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FontFamilyCombo.SelectedItem is not null)
        {
            FontFamilyCombo.Text = FontFamilyCombo.SelectedItem.ToString() ?? FontFamilyCombo.Text;
            ApplyFontFamilyFromControl();
        }
    }

    private void FormattingCombo_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ComboBox combo || combo.IsDropDownOpen)
        {
            return;
        }

        // Consume the opening press so an editable template child cannot
        // immediately reverse IsDropDownOpen during the same mouse input.
        combo.Focus();
        combo.IsDropDownOpen = true;
        e.Handled = true;
    }

    private void FormattingCombo_DropDownClosed(object? sender, EventArgs e)
    {
        if (Mouse.LeftButton != MouseButtonState.Pressed || sender is not ComboBox closedCombo)
        {
            return;
        }

        var targetCombo = ReferenceEquals(closedCombo, FontFamilyCombo)
            ? FontSizeCombo
            : FontFamilyCombo;
        var pointer = Mouse.GetPosition(targetCombo);
        if (pointer.X < 0
            || pointer.X > targetCombo.ActualWidth
            || pointer.Y < 0
            || pointer.Y > targetCombo.ActualHeight)
        {
            return;
        }

        // ComboBox popups capture outside clicks. Reuse the click that closed
        // one formatting popup to open the other after capture is released.
        targetCombo.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                targetCombo.Focus();
                targetCombo.IsDropDownOpen = true;
            }));
    }

    private void FontFamilyCombo_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        ApplyFontFamilyFromControl(returnFocusToEditor: false);

    private void FontFamilyCombo_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyFontFamilyFromControl();
            ActiveView?.TextEditor.Focus();
            e.Handled = true;
        }
    }

    private void FontSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FontSizeCombo.SelectedItem is not null)
        {
            FontSizeCombo.Text = Convert.ToString(FontSizeCombo.SelectedItem, CultureInfo.CurrentCulture) ?? string.Empty;
            ApplyFontSizeFromControl();
        }
    }

    private void FontSizeCombo_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        ApplyFontSizeFromControl(returnFocusToEditor: false);

    private void FontSizeCombo_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyFontSizeFromControl();
            ActiveView?.TextEditor.Focus();
            e.Handled = true;
        }
    }

    private async void WordWrap_Click(object sender, RoutedEventArgs e)
    {
        Services.Settings.WordWrap = WordWrapMenuItem.IsChecked;
        foreach (var session in Documents)
        {
            session.View?.SetWordWrap(Services.Settings.WordWrap);
        }

        await Services.SaveSettingsAsync();
    }

    private async void SmartColoring_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingFeatureToggles)
        {
            return;
        }

        Services.Settings.SmartColoringEnabled = sender switch
        {
            MenuItem menuItem => menuItem.IsChecked,
            ToggleButton toggle => toggle.IsChecked == true,
            _ => !Services.Settings.SmartColoringEnabled
        };
        ApplySettingsToShell(scheduleAnalysis: false);
        await Services.SaveSettingsAsync();
        if (ActiveView is not null)
        {
            await ActiveView.AnalyzeNowAsync();
        }
    }

    private async void GrammarAnalysisMode_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingFeatureToggles)
        {
            return;
        }

        Services.Settings.GrammarMode = GrammarAnalysisModeToggle.IsChecked == true
            ? GrammarAnalysisMode.AI
            : GrammarAnalysisMode.Traditional;
        ApplySettingsToShell(scheduleAnalysis: false);
        await Services.SaveSettingsAsync();
        if (ActiveView is not null)
        {
            await ActiveView.AnalyzeNowAsync();
        }
    }

    private async void DuplicateDetection_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingFeatureToggles)
        {
            return;
        }

        Services.Settings.DuplicateDetectionEnabled = sender switch
        {
            MenuItem menuItem => menuItem.IsChecked,
            ToggleButton toggle => toggle.IsChecked == true,
            _ => !Services.Settings.DuplicateDetectionEnabled
        };
        ApplySettingsToShell(scheduleAnalysis: false);
        await Services.SaveSettingsAsync();
        if (ActiveView is not null)
        {
            await ActiveView.AnalyzeNowAsync();
        }
    }

    private async void DarkMode_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingFeatureToggles)
        {
            return;
        }

        var isDark = sender switch
        {
            MenuItem menuItem => menuItem.IsChecked,
            ToggleButton toggle => toggle.IsChecked == true,
            _ => Services.Settings.Theme != AppThemeMode.Dark
        };
        Services.Settings.Theme = isDark ? AppThemeMode.Dark : AppThemeMode.Light;
        ThemeManager.Apply(Services.Settings);
        ApplySettingsToShell(scheduleAnalysis: false);
        await Services.SaveSettingsAsync();
    }

    private async void SmartPanel_Click(object sender, RoutedEventArgs e)
    {
        Services.Settings.SmartPanelVisible = SmartPanelMenuItem.IsChecked;
        ApplySettingsToShell(scheduleAnalysis: false);
        await Services.SaveSettingsAsync();
        if (ActiveView is not null)
        {
            await ActiveView.AnalyzeNowAsync();
        }
    }

    private async void HideSmartPanel_Click(object sender, RoutedEventArgs e)
    {
        Services.Settings.SmartPanelVisible = false;
        ApplySettingsToShell(scheduleAnalysis: false);
        await Services.SaveSettingsAsync();
    }

    private async void ValidateDocument_Click(object sender, RoutedEventArgs e)
    {
        var session = ActiveSession;
        var view = ActiveView;
        if (session is null || view is null)
        {
            return;
        }

        if (!session.Format.IsStructured())
        {
            MessageBox.Show(
                this,
                "Validation is available for JSON, XML, and YAML documents.",
                "Validate document",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var text = view.GetPlainText();
        var format = session.Format;
        var result = await Task.Run(() =>
            Services.StructuredTextService.Validate(text, format));
        OperationStatusText.Text = result.Message;
        MessageBox.Show(
            this,
            result.Line is null
                ? result.Message
                : $"{result.Message}\n\nLine {result.Line}, column {result.Column ?? 1}",
            result.IsValid ? "Document is valid" : "Validation issue",
            MessageBoxButton.OK,
            result.IsValid ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async void FormatDocument_Click(object sender, RoutedEventArgs e)
    {
        var session = ActiveSession;
        var view = ActiveView;
        if (session is null || view is null)
        {
            return;
        }

        if (session.Format is not (DocumentFormat.Json or DocumentFormat.Xml))
        {
            MessageBox.Show(
                this,
                "Automatic formatting is currently implemented for JSON and XML. YAML is preserved exactly unless edited manually.",
                "Format document",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            var text = view.GetPlainText();
            var format = session.Format;
            var formatted = await Task.Run(() =>
                Services.StructuredTextService.Format(text, format));
            view.ReplaceAllText(formatted);
            OperationStatusText.Text = $"Formatted {session.Format.DisplayName()} document";
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or System.Xml.XmlException)
        {
            ShowError(
                "The document could not be formatted because it is not valid.",
                exception,
                "Run Validate Structured Document for a more precise location.");
        }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(Services.Settings.Clone()) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        Services.Settings.CopyFrom(dialog.ResultSettings);
        ThemeManager.Apply(Services.Settings);
        ApplySettingsToShell(scheduleAnalysis: false);
        ConfigureAutoSaveTimer();
        await Services.SaveSettingsAsync();
        if (ActiveView is not null)
        {
            await ActiveView.AnalyzeNowAsync();
        }
    }

    private void FindingsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindingsList.SelectedItem is TextFinding { Span: { } span })
        {
            ActiveView?.SelectSpan(span);
        }
    }

    private async void IgnoreFinding_Click(object sender, RoutedEventArgs e)
    {
        if (FindingsList.SelectedItem is not TextFinding finding)
        {
            OperationStatusText.Text = "Select a warning to ignore.";
            return;
        }

        Services.Settings.IgnoredWarningIds.Add(finding.Id);
        await Services.SaveSettingsAsync();
        if (ActiveView is not null)
        {
            await ActiveView.AnalyzeNowAsync();
        }
    }

    private async void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DocumentSession session })
        {
            e.Handled = true;
            await CloseSessionAsync(session, createReplacement: true);
        }
    }

    private void DocumentTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, DocumentTabs))
        {
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(UpdateShellForActiveDocument));
    }

    private void Editor_StatusChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, ActiveView))
        {
            UpdateStatusBar();
            UpdateFormattingControls();
            UpdateTitle();
        }
    }

    private void Editor_AnalysisUpdated(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, ActiveView))
        {
            SmartPanel.DataContext = null;
            SmartPanel.DataContext = ActiveSession;
            UpdateStatusBar();
            UpdateGrammarCounts();
        }
    }

    private async void RecentFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string path })
        {
            return;
        }

        if (!File.Exists(path))
        {
            RecentFilesManager.Remove(Services.Settings, path);
            await Services.SaveSettingsAsync();
            UpdateRecentFilesMenu();
            MessageBox.Show(
                this,
                "The recent file no longer exists and was removed from the list.",
                "File not found",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        await OpenFileAsync(path);
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)
            || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        foreach (var path in paths.Where(File.Exists))
        {
            await OpenFileAsync(path);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void KeyboardShortcuts_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Ctrl+N  New\nCtrl+O  Open\nCtrl+S  Save\nCtrl+Shift+S  Save As\n" +
            "Ctrl+W  Close document\nCtrl+F  Find\nCtrl+H  Replace\nF3  Find next\n" +
            "Ctrl+Z / Ctrl+Y  Undo / Redo\nCtrl+B / Ctrl+I / Ctrl+U  Formatting\n" +
            "Ctrl++ / Ctrl+- / Ctrl+0  Zoom\nF7  Analyze now",
            "Keyboard shortcuts",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Modern Notepad\n\nA lightweight, offline-first WPF editor for Windows desktop.\n\n" +
            "Rich formatting is saved in RTF. Smart Coloring and writing analysis are optional and run locally.",
            "About Modern Notepad",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowWindowClose)
        {
            return;
        }

        e.Cancel = true;
        if (_windowCloseInProgress)
        {
            return;
        }

        _windowCloseInProgress = true;
        var discardAfterConfirmation = new List<DocumentSession>();
        try
        {
            foreach (var session in Documents.ToArray())
            {
                if (!session.IsDirty)
                {
                    continue;
                }

                var result = AskToSave(session);
                if (result == MessageBoxResult.Cancel)
                {
                    await SaveRecoverySnapshotsAsync();
                    return;
                }

                if (result == MessageBoxResult.Yes)
                {
                    if (!await SaveSessionAsync(session, forceSaveAs: false))
                    {
                        await SaveRecoverySnapshotsAsync();
                        return;
                    }
                }
                else
                {
                    discardAfterConfirmation.Add(session);
                }
            }

            await SaveSessionStateAsync();
            foreach (var session in discardAfterConfirmation)
            {
                await Services.RecoveryService.DeleteSnapshotAsync(session.RecoveryId);
            }

            _autoSaveTimer.Stop();
            _allowWindowClose = true;
            Close();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowError(
                "Modern Notepad could not finish saving the session state.",
                exception,
                "Your document recovery snapshots are left in place.");
        }
        finally
        {
            _windowCloseInProgress = false;
        }
    }

    private sealed record GrammarCountDisplay(string Name, int Count, GrammarCategory Category, SolidColorBrush ColorBrush);

    private void GrammarColorSwatch_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not GrammarCountDisplay item)
            return;

        var menu = new ContextMenu();
        foreach (var color in ColorPalette)
        {
            var brush = new SolidColorBrush(ThemeManager.ParseColor(color.Value, Colors.Black));
            if (brush.CanFreeze) brush.Freeze();

            var swatch = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(7),
                Margin = new Thickness(0, 0, 7, 0),
                Background = brush,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1)
            };
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(swatch);
            header.Children.Add(new TextBlock { Text = color.Name, VerticalAlignment = VerticalAlignment.Center });

            var menuItem = new MenuItem { Header = header, Tag = color.Value };
            var category = item.Category;
            menuItem.Click += async (_, _) =>
            {
                Services.Settings.GrammarColors[category] = (string)menuItem.Tag;
                await Services.SaveSettingsAsync();
                UpdateGrammarCounts();
                // Re-apply coloring to the editor if analysis data exists
                if (ActiveView is not null)
                {
                    await ActiveView.AnalyzeNowAsync();
                }
            };
            menu.Items.Add(menuItem);
        }

        menu.PlacementTarget = element;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
        e.Handled = true;
    }
}
