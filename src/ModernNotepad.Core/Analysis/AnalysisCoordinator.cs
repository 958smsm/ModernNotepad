using ModernNotepad.Core.Models;

namespace ModernNotepad.Core.Analysis;

public sealed class AnalysisCoordinator
{
    private const string AiFallbackFindingId = "grammar-analysis:ai-fallback";

    private readonly GrammarColorAnalyzer _grammarAnalyzer = new();
    private readonly OpenAiGrammarAnalyzer _openAiGrammarAnalyzer = new();
    private readonly TextStatisticsAnalyzer _statisticsAnalyzer = new();
    private readonly DuplicateDetector _duplicateDetector = new();
    private readonly WritingAssistantAnalyzer _writingAssistantAnalyzer = new();

    public async Task<DocumentAnalysis> AnalyzeAsync(
        string text,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.GrammarMode == GrammarAnalysisMode.Traditional)
        {
            return await Task.Run(
                () => Analyze(text, settings, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }

        var input = await Task.Run(
            () => Prepare(text, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        GrammarAnalysis grammar;
        TextFinding? providerFinding = null;
        try
        {
            grammar = await _openAiGrammarAnalyzer.AnalyzeAsync(
                text,
                input.Tokens,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Keep the editor usable if credentials, connectivity, model access, or
            // model output are unavailable. The warning makes the fallback explicit.
            grammar = _grammarAnalyzer.Analyze(
                text,
                input.Tokens,
                input.Sentences,
                cancellationToken);
            providerFinding = new TextFinding(
                AiFallbackFindingId,
                FindingKind.Validation,
                "AI grammar analysis was unavailable, so Logic & Traditional NLP was used for this pass. Check OPENAI_API_KEY, network access, and model availability.",
                Severity: FindingSeverity.Warning);
        }

        return await Task.Run(
            () => CompleteAnalysis(text, settings, input, grammar, providerFinding, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    public DocumentAnalysis Analyze(
        string text,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.GrammarMode == GrammarAnalysisMode.AI)
        {
            return AnalyzeAsync(text, settings, cancellationToken).GetAwaiter().GetResult();
        }

        var input = Prepare(text, cancellationToken);
        var grammar = _grammarAnalyzer.Analyze(
            text,
            input.Tokens,
            input.Sentences,
            cancellationToken);
        return CompleteAnalysis(
            text,
            settings,
            input,
            grammar,
            providerFinding: null,
            cancellationToken);
    }

    private static AnalysisInput Prepare(string text, CancellationToken cancellationToken) => new(
        TextTokenizer.Tokenize(text, cancellationToken),
        TextSegmentation.GetSentences(text, cancellationToken),
        TextSegmentation.GetParagraphs(text, cancellationToken));

    private DocumentAnalysis CompleteAnalysis(
        string text,
        AppSettings settings,
        AnalysisInput input,
        GrammarAnalysis grammar,
        TextFinding? providerFinding,
        CancellationToken cancellationToken)
    {
        var statistics = _statisticsAnalyzer.Analyze(
            text,
            input.Tokens,
            input.Sentences,
            input.Paragraphs,
            grammar.Counts,
            cancellationToken);

        var findings = new List<TextFinding>();
        if (providerFinding is not null)
        {
            findings.Add(providerFinding);
        }

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
                input.Tokens,
                input.Sentences,
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

    private sealed record AnalysisInput(
        IReadOnlyList<TextToken> Tokens,
        IReadOnlyList<TextSpan> Sentences,
        IReadOnlyList<TextSpan> Paragraphs);
}
