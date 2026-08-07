using ModernNotepad.Core.Models;

namespace ModernNotepad.Core.Analysis;

public sealed class AnalysisCoordinator
{
    public const string AiFallbackFindingId = "grammar-analysis:ai-fallback";
    private const long MaxAiErrorLogBytes = 1_000_000;
    private static readonly object AiErrorLogGate = new();

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
        catch (Exception exception)
        {
            // Keep the editor usable if credentials, connectivity, model access, or
            // model output are unavailable. The warning makes the fallback explicit.
            grammar = _grammarAnalyzer.Analyze(
                text,
                input.Tokens,
                input.Sentences,
                cancellationToken);
            var logPath = TryWriteAiErrorLog(exception);
            providerFinding = CreateAiFallbackFinding(exception, logPath);
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
    internal static TextFinding CreateAiFallbackFinding(Exception exception, string? logPath)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var rootCause = exception.GetBaseException();
        var error = $"{rootCause.GetType().Name}: {rootCause.Message}";
        if (error.Length > 1_200)
        {
            error = error[..1_200] + "…";
        }

        var diagnosticLocation = string.IsNullOrWhiteSpace(logPath)
            ? "The full traceback log could not be written."
            : $"Full traceback log:\n{logPath}";

        return new TextFinding(
            AiFallbackFindingId,
            FindingKind.Validation,
            "AI grammar analysis failed, so Logic & Traditional NLP is being used for this pass." +
            $"\n\nError: {error}\n\n{diagnosticLocation}",
            Severity: FindingSeverity.Warning);
    }

    private static string? TryWriteAiErrorLog(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ModernNotepad");
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, "ai-grammar-error.log");
            var details = RedactKnownApiKeys(exception.ToString());
            var entry = $"{DateTimeOffset.Now:O}{Environment.NewLine}{details}{Environment.NewLine}{Environment.NewLine}";

            lock (AiErrorLogGate)
            {
                if (File.Exists(path) && new FileInfo(path).Length >= MaxAiErrorLogBytes)
                {
                    File.WriteAllText(path, string.Empty);
                }

                File.AppendAllText(path, entry);
            }

            return path;
        }
        catch
        {
            // Diagnostics must never prevent the local grammar fallback.
            return null;
        }
    }

    private static string RedactKnownApiKeys(string details)
    {
        foreach (var target in new[]
                 {
                     EnvironmentVariableTarget.Process,
                     EnvironmentVariableTarget.User,
                     EnvironmentVariableTarget.Machine
                 })
        {
            try
            {
                var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY", target);
                if (!string.IsNullOrWhiteSpace(key))
                {
                    details = details.Replace(key, "[REDACTED]", StringComparison.Ordinal);
                }
            }
            catch
            {
                // A restricted environment scope should not block error reporting.
            }
        }

        return details;
    }

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
            .Where(finding => finding.Id == AiFallbackFindingId
                || !settings.IgnoredWarningIds.Contains(finding.Id))
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
