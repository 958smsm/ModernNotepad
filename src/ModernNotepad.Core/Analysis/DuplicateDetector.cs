using System.Text;
using System.Text.RegularExpressions;

namespace ModernNotepad.Core.Analysis;

public sealed class DuplicateDetector
{
    private static readonly HashSet<string> CommonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "but", "is", "are", "was", "were", "be", "been", "being", "to", "of",
        "in", "on", "for", "with", "as", "at", "by", "from", "that", "this", "it", "its", "he", "she", "they",
        "we", "you", "i", "me", "my", "our", "your", "their", "not", "do", "does", "did", "have", "has", "had"
    };

    private static readonly Regex NormalizeSentenceRegex = new(
        @"[^\p{L}\p{M}\p{N}]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public DuplicateAnalysis Analyze(
        string text,
        int repetitionThreshold = 3,
        bool strict = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        repetitionThreshold = Math.Clamp(repetitionThreshold, 2, 100);

        var tokens = TextTokenizer.Tokenize(text, cancellationToken);
        var sentences = TextSegmentation.GetSentences(text, cancellationToken);
        var paragraphs = TextSegmentation.GetParagraphs(text, cancellationToken);
        var findings = new List<TextFinding>();
        var highlights = new Dictionary<(int Start, int Length), TextSpan>();

        AnalyzeSentenceRepetitions(text, tokens, sentences, strict, findings, highlights, cancellationToken);
        AnalyzeParagraphFrequency(tokens, paragraphs, repetitionThreshold, strict, findings, highlights, cancellationToken);
        AnalyzeDocumentFrequency(tokens, repetitionThreshold, strict, findings, highlights, cancellationToken);
        AnalyzeDuplicateSentences(text, sentences, findings, highlights, cancellationToken);

        return new DuplicateAnalysis(
            findings.OrderBy(finding => finding.Span?.Start ?? int.MaxValue).ToArray(),
            highlights.Values.OrderBy(span => span.Start).ToArray());
    }

    private static void AnalyzeSentenceRepetitions(
        string text,
        IReadOnlyList<TextToken> tokens,
        IReadOnlyList<TextSpan> sentences,
        bool strict,
        ICollection<TextFinding> findings,
        IDictionary<(int Start, int Length), TextSpan> highlights,
        CancellationToken cancellationToken)
    {
        var ranges = SpanTokenIndex.Align(tokens, sentences, cancellationToken);
        foreach (var range in ranges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (range.Count < 2)
            {
                continue;
            }

            var occurrences = BuildOccurrenceIndex(tokens, range.Start, range.End, strict, cancellationToken);
            string? normalizedSentence = null;
            foreach (var pair in occurrences)
            {
                if (pair.Value.Count <= 1)
                {
                    continue;
                }

                foreach (var tokenIndex in pair.Value)
                {
                    var token = tokens[tokenIndex];
                    highlights[(token.Span.Start, token.Span.Length)] = token.Span;
                }

                normalizedSentence ??= Normalize(text.Substring(range.Span.Start, range.Span.Length));
                var first = tokens[pair.Value[0]];
                var second = tokens[pair.Value[1]];
                findings.Add(new TextFinding(
                    StableFindingId.Create("repeat-word", $"{pair.Key}|{range.Span.Start}|{normalizedSentence}"),
                    FindingKind.RepeatedWord,
                    $"“{first.Text}” is repeated {pair.Value.Count} times in the same sentence.",
                    second.Span,
                    "Consider removing or replacing one occurrence."));
            }
        }
    }

    private static void AnalyzeParagraphFrequency(
        IReadOnlyList<TextToken> tokens,
        IReadOnlyList<TextSpan> paragraphs,
        int threshold,
        bool strict,
        ICollection<TextFinding> findings,
        IDictionary<(int Start, int Length), TextSpan> highlights,
        CancellationToken cancellationToken)
    {
        var ranges = SpanTokenIndex.Align(tokens, paragraphs, cancellationToken);
        foreach (var range in ranges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var occurrences = BuildOccurrenceIndex(tokens, range.Start, range.End, strict, cancellationToken);
            foreach (var pair in occurrences.OrderByDescending(pair => pair.Value.Count))
            {
                if (pair.Value.Count < threshold)
                {
                    continue;
                }

                foreach (var tokenIndex in pair.Value)
                {
                    var token = tokens[tokenIndex];
                    highlights[(token.Span.Start, token.Span.Length)] = token.Span;
                }

                var first = tokens[pair.Value[0]];
                findings.Add(new TextFinding(
                    StableFindingId.Create("paragraph-frequency", $"{range.Span.Start}|{pair.Key}"),
                    FindingKind.FrequentWord,
                    $"“{first.Text}” appears {pair.Value.Count} times in this paragraph.",
                    first.Span,
                    "Consider varying the wording."));
            }
        }
    }

    private static void AnalyzeDocumentFrequency(
        IReadOnlyList<TextToken> tokens,
        int threshold,
        bool strict,
        ICollection<TextFinding> findings,
        IDictionary<(int Start, int Length), TextSpan> highlights,
        CancellationToken cancellationToken)
    {
        var documentThreshold = Math.Max(threshold * 2, 5);
        var occurrences = BuildOccurrenceIndex(tokens, 0, tokens.Count, strict, cancellationToken);
        foreach (var pair in occurrences.OrderByDescending(pair => pair.Value.Count))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pair.Value.Count < documentThreshold)
            {
                continue;
            }

            foreach (var tokenIndex in pair.Value)
            {
                var token = tokens[tokenIndex];
                highlights[(token.Span.Start, token.Span.Length)] = token.Span;
            }

            var first = tokens[pair.Value[0]];
            findings.Add(new TextFinding(
                StableFindingId.Create("document-frequency", pair.Key),
                FindingKind.FrequentWord,
                $"“{first.Text}” appears {pair.Value.Count} times in the document.",
                first.Span,
                "Review whether a synonym would improve variety.",
                FindingSeverity.Information));
        }
    }

    private static void AnalyzeDuplicateSentences(
        string text,
        IReadOnlyList<TextSpan> sentences,
        ICollection<TextFinding> findings,
        IDictionary<(int Start, int Length), TextSpan> highlights,
        CancellationToken cancellationToken)
    {
        var firstByNormalized = new Dictionary<string, TextSpan>(StringComparer.Ordinal);
        for (var index = 0; index < sentences.Count; index++)
        {
            if ((index & 127) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var span = sentences[index];
            var normalized = Normalize(text.Substring(span.Start, span.Length));
            if (normalized.Length < 10)
            {
                continue;
            }

            if (firstByNormalized.TryAdd(normalized, span))
            {
                continue;
            }

            highlights[(span.Start, span.Length)] = span;
            findings.Add(new TextFinding(
                StableFindingId.Create("duplicate-sentence", $"{normalized}|{span.Start}"),
                FindingKind.DuplicateSentence,
                "This sentence duplicates an earlier sentence.",
                span,
                "Remove it or combine the two passages."));
        }
    }

    private static Dictionary<string, List<int>> BuildOccurrenceIndex(
        IReadOnlyList<TextToken> tokens,
        int start,
        int end,
        bool strict,
        CancellationToken cancellationToken)
    {
        var occurrences = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (var index = start; index < end; index++)
        {
            if ((index & 511) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var token = tokens[index];
            if (!strict && CommonWords.Contains(token.Normalized))
            {
                continue;
            }

            if (!occurrences.TryGetValue(token.Normalized, out var list))
            {
                list = new List<int>(2);
                occurrences.Add(token.Normalized, list);
            }
            list.Add(index);
        }

        return occurrences;
    }

    private static string Normalize(string text)
    {
        var normalized = NormalizeSentenceRegex.Replace(text.ToLowerInvariant(), " ").Trim();
        var output = new StringBuilder(normalized.Length);
        var previousWasSpace = false;
        foreach (var character in normalized)
        {
            if (character == ' ')
            {
                if (!previousWasSpace)
                {
                    output.Append(character);
                }
                previousWasSpace = true;
            }
            else
            {
                output.Append(character);
                previousWasSpace = false;
            }
        }

        return output.ToString();
    }
}
