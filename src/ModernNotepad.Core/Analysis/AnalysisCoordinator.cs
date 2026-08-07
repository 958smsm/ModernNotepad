using ModernNotepad.Core.Models;

namespace ModernNotepad.Core.Analysis;

public sealed class AnalysisCoordinator
{
    private readonly IGrammarAnalyzer _traditionalAnalyzer = new TraditionalGrammarAnalyzer();
    private IGrammarAnalyzer? _aiAnalyzer;
    private readonly TextStatisticsAnalyzer _statisticsAnalyzer = new();
    private readonly DuplicateDetector _duplicateDetector = new();
    private readonly WritingAssistantAnalyzer _writingAssistantAnalyzer = new();

    private IGrammarAnalyzer GetAnalyzer(AppSettings settings)
    {
        if (settings.GrammarAnalysisMode == GrammarAnalysisMode.AI)
        {
            _aiAnalyzer ??= new AIGrammarAnalyzer();
            return _aiAnalyzer;
        }
        return _traditionalAnalyzer;
    }

    public async Task<DocumentAnalysis> AnalyzeAsync(
        string text,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(settings);

        var tokens = await Task.Run(() => TextTokenizer.Tokenize(text, cancellationToken), cancellationToken);
        var sentences = await Task.Run(() => TextSegmentation.GetSentences(text, cancellationToken), cancellationToken);
        var paragraphs = await Task.Run(() => TextSegmentation.GetParagraphs(text, cancellationToken), cancellationToken);
        
        var analyzer = GetAnalyzer(settings);
        var grammar = await analyzer.AnalyzeAsync(text, tokens, sentences, cancellationToken);
        
        var statistics = await Task.Run(() => _statisticsAnalyzer.Analyze(
            text,
            tokens,
            sentences,
            paragraphs,
            grammar.Counts,
            cancellationToken), cancellationToken);

        var findings = new List<TextFinding>();
        var duplicateSpans = Array.Empty<TextSpan>();

        if (settings.DuplicateDetectionEnabled)
        {
            var duplicates = await Task.Run(() => _duplicateDetector.Analyze(
                text,
                settings.DuplicateThreshold,
                settings.StrictDuplicateChecking,
                cancellationToken), cancellationToken);
            findings.AddRange(duplicates.Findings);
            duplicateSpans = duplicates.HighlightSpans.ToArray();
        }

        if (settings.SmartPanelVisible)
        {
            var assistantFindings = await Task.Run(() => _writingAssistantAnalyzer.Analyze(
                text,
                tokens,
                sentences,
                settings.LongSentenceWordThreshold,
                settings.PassiveVoiceDetectionEnabled,
                cancellationToken), cancellationToken);
            findings.AddRange(assistantFindings);
        }

        var filteredFindings = await Task.Run(() => findings
            .Where(finding => !settings.IgnoredWarningIds.Contains(finding.Id))
            .GroupBy(finding => finding.Id)
            .Select(group => group.First())
            .OrderBy(finding => finding.Span?.Start ?? int.MaxValue)
            .Take(500)
            .ToArray(), cancellationToken);

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
