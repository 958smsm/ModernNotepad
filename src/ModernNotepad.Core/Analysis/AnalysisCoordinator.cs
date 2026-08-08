using ModernNotepad.Core.Models;

namespace ModernNotepad.Core.Analysis;

public sealed class AnalysisCoordinator : IDisposable
{
    public const string ProviderFallbackFindingId = "grammar-analysis:provider-fallback";
    public const string AiFallbackFindingId = ProviderFallbackFindingId;
    private const long MaxProviderErrorLogBytes = 1_000_000;
    private static readonly object ProviderErrorLogGate = new();

    private readonly GrammarColorAnalyzer _grammarAnalyzer = new();
    private readonly OpenAiGrammarAnalyzer _openAiGrammarAnalyzer = new();
    private readonly PythonGrammarAnalyzer _spacyGrammarAnalyzer = new(PythonGrammarEngine.Spacy);
    private readonly PythonGrammarAnalyzer _nltkGrammarAnalyzer = new(PythonGrammarEngine.Nltk);
    private readonly GoogleCloudGrammarAnalyzer _googleCloudGrammarAnalyzer = new();
    private readonly TextStatisticsAnalyzer _statisticsAnalyzer = new();
    private readonly DuplicateDetector _duplicateDetector = new();
    private readonly WritingAssistantAnalyzer _writingAssistantAnalyzer = new();
    private bool _disposed;

    public async Task<DocumentAnalysis> AnalyzeAsync(
        string text,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(settings);

        var mode = ResolveConfiguredMode(settings);
        if (mode == GrammarAnalysisMode.Traditional)
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
        var providerName = GetModeDisplayName(mode);
        try
        {
            grammar = mode switch
            {
                GrammarAnalysisMode.OpenAI => await _openAiGrammarAnalyzer.AnalyzeAsync(
                    text,
                    input.Tokens,
                    cancellationToken).ConfigureAwait(false),
                GrammarAnalysisMode.PythonSpacy => await _spacyGrammarAnalyzer.AnalyzeAsync(
                    text,
                    input.Tokens,
                    settings.PythonTransport,
                    cancellationToken).ConfigureAwait(false),
                GrammarAnalysisMode.PythonNltk => await _nltkGrammarAnalyzer.AnalyzeAsync(
                    text,
                    input.Tokens,
                    settings.PythonTransport,
                    cancellationToken).ConfigureAwait(false),
                GrammarAnalysisMode.GoogleCloudNaturalLanguage => await _googleCloudGrammarAnalyzer.AnalyzeAsync(
                    text,
                    input.Tokens,
                    cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException(
                    $"Unsupported grammar analysis mode '{mode}'.")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Keep the editor usable when a provider dependency, IPC channel,
            // credential, connection, model access, or response is unavailable.
            grammar = _grammarAnalyzer.Analyze(
                text,
                input.Tokens,
                input.Sentences,
                cancellationToken);
            var logPath = TryWriteProviderErrorLog(exception);
            providerFinding = CreateProviderFallbackFinding(providerName, exception, logPath);
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
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(settings);

        if (ResolveConfiguredMode(settings) != GrammarAnalysisMode.Traditional)
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

    public static GrammarAnalysisMode ResolveConfiguredMode(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.GrammarMode != GrammarAnalysisMode.Provider)
        {
            return settings.GrammarMode;
        }

        return settings.GrammarProvider switch
        {
            GrammarAnalysisProvider.PythonSpacy => GrammarAnalysisMode.PythonSpacy,
            GrammarAnalysisProvider.PythonNltk => GrammarAnalysisMode.PythonNltk,
            GrammarAnalysisProvider.GoogleCloudNaturalLanguage => GrammarAnalysisMode.GoogleCloudNaturalLanguage,
            _ => GrammarAnalysisMode.PythonSpacy
        };
    }

    public static string GetModeDisplayName(GrammarAnalysisMode mode) => mode switch
    {
        GrammarAnalysisMode.Traditional => "Logic & Traditional NLP",
        GrammarAnalysisMode.OpenAI => "OpenAI",
        GrammarAnalysisMode.PythonSpacy => "Python spaCy",
        GrammarAnalysisMode.PythonNltk => "Python NLTK",
        GrammarAnalysisMode.GoogleCloudNaturalLanguage => GoogleCloudGrammarAnalyzer.DisplayName,
        GrammarAnalysisMode.Provider => "Grammar provider",
        _ => mode.ToString()
    };

    public static string GetProviderDisplayName(GrammarAnalysisProvider provider) => provider switch
    {
        GrammarAnalysisProvider.PythonSpacy => "Python spaCy",
        GrammarAnalysisProvider.PythonNltk => "Python NLTK",
        GrammarAnalysisProvider.GoogleCloudNaturalLanguage => GoogleCloudGrammarAnalyzer.DisplayName,
        _ => provider.ToString()
    };

    private static AnalysisInput Prepare(string text, CancellationToken cancellationToken) => new(
        TextTokenizer.Tokenize(text, cancellationToken),
        TextSegmentation.GetSentences(text, cancellationToken),
        TextSegmentation.GetParagraphs(text, cancellationToken));

    internal static TextFinding CreateAiFallbackFinding(Exception exception, string? logPath) =>
        CreateProviderFallbackFinding("OpenAI", exception, logPath);

    internal static TextFinding CreateProviderFallbackFinding(
        string providerName,
        Exception exception,
        string? logPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentNullException.ThrowIfNull(exception);

        var rootCause = exception.GetBaseException();
        var error = $"{rootCause.GetType().Name}: {rootCause.Message}";
        if (error.Length > 1_200)
        {
            error = error[..1_200] + "…";
        }

        var diagnosticLocation = string.IsNullOrWhiteSpace(logPath)
            ? "The full provider error log could not be written."
            : $"Full provider error log:\n{logPath}";

        return new TextFinding(
            ProviderFallbackFindingId,
            FindingKind.Validation,
            $"{providerName} grammar analysis failed, so Logic & Traditional NLP is being used for this pass." +
            $"\n\nError: {error}\n\n{diagnosticLocation}",
            Severity: FindingSeverity.Warning);
    }

    private static string? TryWriteProviderErrorLog(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ModernNotepad");
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, "grammar-provider-error.log");
            var details = RedactKnownApiKeys(exception.ToString());
            var entry = $"{DateTimeOffset.Now:O}{Environment.NewLine}{details}{Environment.NewLine}{Environment.NewLine}";

            lock (ProviderErrorLogGate)
            {
                if (File.Exists(path) && new FileInfo(path).Length >= MaxProviderErrorLogBytes)
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
        foreach (var name in new[] { "OPENAI_API_KEY", "GOOGLE_CLOUD_NL_API_KEY", "GOOGLE_API_KEY" })
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
                    var key = Environment.GetEnvironmentVariable(name, target);
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
            .Where(finding => finding.Id == ProviderFallbackFindingId
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

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AnalysisCoordinator));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _spacyGrammarAnalyzer.Dispose();
        _nltkGrammarAnalyzer.Dispose();
    }

    private sealed record AnalysisInput(
        IReadOnlyList<TextToken> Tokens,
        IReadOnlyList<TextSpan> Sentences,
        IReadOnlyList<TextSpan> Paragraphs);
}
