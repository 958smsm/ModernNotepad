using ModernNotepad.Core.Models;

namespace ModernNotepad.Core.Analysis;

public sealed class AnalysisCoordinator
{
    private readonly GrammarColorAnalyzer _grammarAnalyzer = new();
    private readonly TextStatisticsAnalyzer _statisticsAnalyzer = new();
    private readonly DuplicateDetector _duplicateDetector = new();
    private readonly WritingAssistantAnalyzer _writingAssistantAnalyzer = new();

    public Task<DocumentAnalysis> AnalyzeAsync(
        string text,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(settings);

        return Task.Run(() => Analyze(text, settings, cancellationToken), cancellationToken);
    }

    public DocumentAnalysis Analyze(
        string text,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        var tokens = TextTokenizer.Tokenize(text, cancellationToken);
        var sentences = TextSegmentation.GetSentences(text, cancellationToken);
        var paragraphs = TextSegmentation.GetParagraphs(text, cancellationToken);
        var grammar = _grammarAnalyzer.Analyze(text, tokens, sentences, cancellationToken);
        var statistics = _statisticsAnalyzer.Analyze(
            text,
            tokens,
            sentences,
            paragraphs,
            grammar.Counts,
            cancellationToken);

        var findings = new List<TextFinding>();
        var duplicateSpans = Array.Empty<TextSpan>();

        if (settings.DuplicateDetectionEnabled)
        {
            var duplicates = _duplicateDetector.Analyze(
                text,
                settings.DuplicateThreshold,
                settings.StrictDuplicateChecking,
                cancellationToken);
            findings.AddRange(duplicates.Findings);
            duplicateSpans = duplicates.HighlightSpans.ToArray();
        }

        if (settings.SmartPanelVisible)
        {
            findings.AddRange(_writingAssistantAnalyzer.Analyze(
                text,
                tokens,
                sentences,
                settings.LongSentenceWordThreshold,
                settings.PassiveVoiceDetectionEnabled,
                cancellationToken));
        }

        var filteredFindings = findings
            .Where(finding => !settings.IgnoredWarningIds.Contains(finding.Id))
            .GroupBy(finding => finding.Id)
            .Select(group => group.First())
            .OrderBy(finding => finding.Span?.Start ?? int.MaxValue)
            .Take(500)
            .ToArray();

        var coloredSpans = settings.SmartColoringEnabled
            ? grammar.Spans.Take(settings.MaxVisualAnalysisSpans).ToArray()
            : Array.Empty<ColoredSpan>();

        return new DocumentAnalysis(
            statistics,
            filteredFindings,
            coloredSpans,
            duplicateSpans.Take(settings.MaxVisualAnalysisSpans).ToArray());
    }
}
