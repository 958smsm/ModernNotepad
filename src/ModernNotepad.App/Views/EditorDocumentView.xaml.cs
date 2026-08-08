using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using ModernNotepad.App.Models;
using ModernNotepad.App.Services;
using ModernNotepad.Core.Analysis;

namespace ModernNotepad.App.Views;

public partial class EditorDocumentView : UserControl
{
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session),
        typeof(DocumentSession),
        typeof(EditorDocumentView),
        new PropertyMetadata(null, OnSessionChanged));

    private readonly DispatcherTimer _analysisTimer;
    private readonly FormattingOverlayManager _smartColorOverlay;
    private readonly FormattingOverlayManager _duplicateOverlay;
    private CancellationTokenSource? _analysisCancellation;
    private DocumentAnalysis _lastAnalysis = DocumentAnalysis.Empty;
    private bool _suppressChanges;
    private bool _isApplyingVisuals;
    private int _lastFindStart = -1;
    private string _lastFindQuery = string.Empty;

    public EditorDocumentView()
    {
        InitializeComponent();

        _analysisTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(650)
        };
        _analysisTimer.Tick += AnalysisTimer_Tick;

        _smartColorOverlay = new FormattingOverlayManager(
            TextElement.ForegroundProperty,
            editor => editor.Foreground);
        _duplicateOverlay = new FormattingOverlayManager(
            TextElement.BackgroundProperty,
            _ => Brushes.Transparent);
    }

    public event EventHandler? StatusChanged;
    public event EventHandler? AnalysisUpdated;

    public DocumentSession? Session
    {
        get => (DocumentSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    private AppServices Services => ((App)Application.Current).Services;

    public RichTextBox TextEditor => Editor;
    public DocumentAnalysis LastAnalysis => _lastAnalysis;

    public void ApplySettings(bool scheduleAnalysis = true)
    {
        var session = Session;
        if (session is null)
        {
            return;
        }

        SetWordWrap(Services.Settings.WordWrap);
        ApplyZoom(session.ZoomPercent);
        _analysisTimer.Interval = ModernNotepad.Core.Analysis.AnalysisCoordinator.ResolveConfiguredMode(Services.Settings)
            == ModernNotepad.Core.Models.GrammarAnalysisMode.Traditional
                ? TimeSpan.FromMilliseconds(650)
                : TimeSpan.FromMilliseconds(1500);

        try
        {
            Editor.Language = XmlLanguage.GetLanguage(Services.Settings.SpellCheckLanguage);
        }
        catch (ArgumentException)
        {
            Editor.Language = XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.IetfLanguageTag);
        }

        if (scheduleAnalysis)
        {
            ScheduleAnalysis(immediate: true);
        }
    }

    public void SetWordWrap(bool enabled)
    {
        if (Session is null)
        {
            return;
        }

        Editor.HorizontalScrollBarVisibility = enabled
            ? ScrollBarVisibility.Hidden
            : ScrollBarVisibility.Auto;
        Session.Document.PageWidth = enabled ? double.NaN : 100000d;
    }

    public void ChangeZoom(int delta)
    {
        if (Session is null)
        {
            return;
        }

        Session.ZoomPercent = Math.Clamp(Session.ZoomPercent + delta, 50, 300);
        ApplyZoom(Session.ZoomPercent);
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyZoom(int percent)
    {
        var scale = Math.Clamp(percent, 50, 300) / 100d;
        Editor.LayoutTransform = new ScaleTransform(scale, scale);
    }

    public DocumentTextSnapshot GetSnapshot() => DocumentTextSnapshot.Create(Editor.Document);

    public string GetPlainText() => GetSnapshot().Text;

    public byte[] GetRtfBytes(bool includeSmartColoring = true)
    {
        _isApplyingVisuals = true;
        try
        {
            _duplicateOverlay.Restore(Editor);
            if (!includeSmartColoring)
            {
                _smartColorOverlay.Restore(Editor);
            }

            return DocumentFactory.ToRtf(Editor.Document);
        }
        finally
        {
            _isApplyingVisuals = false;
            if (Session is not null)
            {
                ApplyAnalysisVisuals(GetSnapshot(), _lastAnalysis);
            }
        }
    }

    public void ClearVisualOverlays()
    {
        _isApplyingVisuals = true;
        try
        {
            _smartColorOverlay.Restore(Editor);
            _duplicateOverlay.Restore(Editor);
        }
        finally
        {
            _isApplyingVisuals = false;
        }
    }

    public void ShowFind(bool showReplace)
    {
        FindBar.Visibility = Visibility.Visible;
        ReplaceRow.Visibility = showReplace ? Visibility.Visible : Visibility.Collapsed;
        FindTextBox.Focus();
        FindTextBox.SelectAll();
    }

    public bool FindNext(bool backwards = false)
    {
        var query = FindTextBox.Text;
        if (string.IsNullOrEmpty(query))
        {
            FindStatusText.Text = "Enter text to find.";
            return false;
        }

        var snapshot = GetSnapshot();
        if (snapshot.Text.Length == 0)
        {
            FindStatusText.Text = "No matches.";
            return false;
        }

        var comparison = MatchCaseCheckBox.IsChecked == true
            ? StringComparison.CurrentCulture
            : StringComparison.CurrentCultureIgnoreCase;

        if (!string.Equals(_lastFindQuery, query, StringComparison.Ordinal))
        {
            _lastFindStart = -1;
            _lastFindQuery = query;
        }

        int index;
        if (backwards)
        {
            var start = _lastFindStart > 0
                ? _lastFindStart - 1
                : Math.Min(GetSelectionOffset(), Math.Max(0, snapshot.Text.Length - 1));
            index = snapshot.Text.LastIndexOf(query, start, comparison);
            if (index < 0 && snapshot.Text.Length > 0)
            {
                index = snapshot.Text.LastIndexOf(query, snapshot.Text.Length - 1, comparison);
            }
        }
        else
        {
            var start = _lastFindStart >= 0
                ? Math.Min(snapshot.Text.Length, _lastFindStart + Math.Max(1, query.Length))
                : Math.Min(snapshot.Text.Length, GetSelectionOffset());
            index = snapshot.Text.IndexOf(query, start, comparison);
            if (index < 0)
            {
                index = snapshot.Text.IndexOf(query, 0, comparison);
            }
        }

        if (index < 0)
        {
            FindStatusText.Text = "No matches.";
            return false;
        }

        _lastFindStart = index;
        var range = snapshot.CreateRange(new TextSpan(index, query.Length));
        if (range is null)
        {
            FindStatusText.Text = "No matches.";
            return false;
        }

        Editor.Selection.Select(range.Start, range.End);
        Editor.CaretPosition = range.End;
        Editor.Focus();
        FindStatusText.Text = $"Match at {index + 1}.";
        return true;
    }

    public bool ReplaceCurrent()
    {
        var query = FindTextBox.Text;
        if (string.IsNullOrEmpty(query))
        {
            return false;
        }

        var comparison = MatchCaseCheckBox.IsChecked == true
            ? StringComparison.CurrentCulture
            : StringComparison.CurrentCultureIgnoreCase;
        var selected = Editor.Selection.Text;

        if (!string.Equals(selected, query, comparison))
        {
            return FindNext();
        }

        ClearVisualOverlays();
        Editor.Selection.Text = ReplaceTextBox.Text;
        _lastFindStart = -1;
        FindNext();
        return true;
    }

    public int ReplaceAll()
    {
        var query = FindTextBox.Text;
        if (string.IsNullOrEmpty(query))
        {
            return 0;
        }

        ClearVisualOverlays();
        var snapshot = GetSnapshot();
        var comparison = MatchCaseCheckBox.IsChecked == true
            ? StringComparison.CurrentCulture
            : StringComparison.CurrentCultureIgnoreCase;
        var matches = new List<int>();
        var cursor = 0;

        while (cursor <= snapshot.Text.Length - query.Length)
        {
            var index = snapshot.Text.IndexOf(query, cursor, comparison);
            if (index < 0)
            {
                break;
            }

            matches.Add(index);
            cursor = index + Math.Max(1, query.Length);
        }

        if (matches.Count == 0)
        {
            FindStatusText.Text = "No matches.";
            return 0;
        }

        Editor.BeginChange();
        try
        {
            for (var index = matches.Count - 1; index >= 0; index--)
            {
                var range = snapshot.CreateRange(new TextSpan(matches[index], query.Length));
                if (range is not null)
                {
                    range.Text = ReplaceTextBox.Text;
                }
            }
        }
        finally
        {
            Editor.EndChange();
        }

        _lastFindStart = -1;
        FindStatusText.Text = $"Replaced {matches.Count} occurrence(s).";
        return matches.Count;
    }

    public void ReplaceAllText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ClearVisualOverlays();

        Editor.BeginChange();
        try
        {
            var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
            range.Text = text;
        }
        finally
        {
            Editor.EndChange();
        }
    }

    public void SelectSpan(TextSpan span)
    {
        var range = GetSnapshot().CreateRange(span);
        if (range is null)
        {
            return;
        }

        Editor.Selection.Select(range.Start, range.End);
        Editor.CaretPosition = range.End;
        Editor.Focus();
    }

    public (int Line, int Column) GetLineAndColumn()
    {
        var beforeCaret = new TextRange(Editor.Document.ContentStart, Editor.CaretPosition).Text;
        var normalized = beforeCaret.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var line = 1;
        var column = 1;

        foreach (var character in normalized)
        {
            if (character == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }

    public async Task AnalyzeNowAsync()
    {
        var session = Session;
        if (session is null)
        {
            return;
        }

        _analysisTimer.Stop();
        _analysisCancellation?.Cancel();
        _analysisCancellation?.Dispose();
        _analysisCancellation = new CancellationTokenSource();
        var cancellationToken = _analysisCancellation.Token;

        DocumentTextSnapshot snapshot;
        _isApplyingVisuals = true;
        try
        {
            _smartColorOverlay.Restore(Editor);
            _duplicateOverlay.Restore(Editor);
            snapshot = GetSnapshot();
        }
        finally
        {
            _isApplyingVisuals = false;
        }

        try
        {
            var analysis = await Services.AnalysisCoordinator.AnalyzeAsync(
                snapshot.Text,
                Services.Settings,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (!ReferenceEquals(session, Session))
            {
                return;
            }

            session.SetAnalysis(analysis);
            _lastAnalysis = analysis;
            ApplyAnalysisVisuals(snapshot, analysis);
            AnalysisUpdated?.Invoke(this, EventArgs.Empty);
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            // A newer edit or tab switch superseded this analysis pass.
        }
    }

    private static void OnSessionChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var control = (EditorDocumentView)dependencyObject;
        control.AttachSession(e.OldValue as DocumentSession, e.NewValue as DocumentSession);
    }

    private void AttachSession(DocumentSession? oldSession, DocumentSession? newSession)
    {
        if (oldSession?.View == this)
        {
            oldSession.View = null;
        }

        if (newSession is null)
        {
            return;
        }

        _suppressChanges = true;
        try
        {
            _lastAnalysis = DocumentAnalysis.Empty;
            Editor.Document = newSession.Document;
            newSession.View = this;
            ApplySettings();
        }
        finally
        {
            _suppressChanges = false;
        }
    }

    private void ApplyAnalysisVisuals(DocumentTextSnapshot snapshot, DocumentAnalysis analysis)
    {
        _isApplyingVisuals = true;
        try
        {
            var smartOverlays = analysis.ColoredSpans.Select(span =>
            {
                var colorText = Services.Settings.GrammarColors.TryGetValue(span.Category, out var configured)
                    ? configured
                    : "#667085";
                return (span.Span, (object)CreateBrush(colorText, Colors.Gray));
            });
            _smartColorOverlay.Apply(Editor, snapshot, smartOverlays);

            var duplicateBrush = CreateBrush(
                Services.Settings.DuplicateHighlightColor,
                Color.FromRgb(255, 243, 163));
            _duplicateOverlay.Apply(
                Editor,
                snapshot,
                analysis.DuplicateSpans.Select(span => (span, (object)duplicateBrush)));
        }
        finally
        {
            _isApplyingVisuals = false;
        }
    }

    private void ScheduleAnalysis(bool immediate = false)
    {
        if (!IsLoaded || Session is null)
        {
            return;
        }

        _analysisTimer.Stop();
        if (immediate)
        {
            _ = AnalyzeNowAsync();
        }
        else
        {
            _analysisTimer.Start();
        }
    }

    private int GetSelectionOffset()
    {
        var text = new TextRange(Editor.Document.ContentStart, Editor.Selection.End).Text;
        return Math.Max(0, text.Length);
    }

    private static SolidColorBrush CreateBrush(string? colorText, Color fallback)
    {
        var color = ThemeManager.ParseColor(colorText, fallback);
        var brush = new SolidColorBrush(color);
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }

    private async void AnalysisTimer_Tick(object? sender, EventArgs e)
    {
        _analysisTimer.Stop();
        await AnalyzeNowAsync();
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressChanges || _isApplyingVisuals || Session is null)
        {
            return;
        }

        Session.MarkDirty();
        _analysisCancellation?.Cancel();
        ScheduleAnalysis();
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Editor_SelectionChanged(object sender, RoutedEventArgs e) =>
        StatusChanged?.Invoke(this, EventArgs.Empty);

    private void Editor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        ChangeZoom(e.Delta > 0 ? 10 : -10);
        e.Handled = true;
    }

    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && FindBar.Visibility == Visibility.Visible)
        {
            FindBar.Visibility = Visibility.Collapsed;
            Editor.Focus();
            e.Handled = true;
        }
    }

    private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _lastFindStart = -1;
        _lastFindQuery = FindTextBox.Text;
        FindStatusText.Text = string.Empty;
    }

    private void FindTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FindNext((Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            FindBar.Visibility = Visibility.Collapsed;
            Editor.Focus();
            e.Handled = true;
        }
    }

    private void ReplaceTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ReplaceCurrent();
            e.Handled = true;
        }
    }

    private void FindPrevious_Click(object sender, RoutedEventArgs e) => FindNext(backwards: true);
    private void FindNext_Click(object sender, RoutedEventArgs e) => FindNext();
    private void Replace_Click(object sender, RoutedEventArgs e) => ReplaceCurrent();
    private void ReplaceAll_Click(object sender, RoutedEventArgs e) => ReplaceAll();

    private void CloseFindBar_Click(object sender, RoutedEventArgs e)
    {
        FindBar.Visibility = Visibility.Collapsed;
        Editor.Focus();
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        ApplySettings();
    }

    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        _analysisTimer.Stop();
        _analysisCancellation?.Cancel();
    }
}
