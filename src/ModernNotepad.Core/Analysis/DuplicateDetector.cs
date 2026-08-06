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
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
            findings
                .OrderBy(finding => finding.Span?.Start ?? int.MaxValue)
                .Take(500)
                .ToArray(),
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
        foreach (var sentence in sentences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sentenceTokens = tokens
                .Where(token => token.Span.Start >= sentence.Start && token.Span.End <= sentence.End)
                .Where(token => strict || !CommonWords.Contains(token.Normalized))
                .GroupBy(token => token.Normalized)
                .Where(group => group.Count() > 1);

            foreach (var group in sentenceTokens)
            {
                var occurrences = group.ToArray();
                foreach (var occurrence in occurrences)
                {
                    highlights[(occurrence.Span.Start, occurrence.Span.Length)] = occurrence.Span;
                }

                var sentenceText = text.Substring(sentence.Start, sentence.Length);
                findings.Add(new TextFinding(
                    StableFindingId.Create("repeat-word", $"{group.Key}|{Normalize(sentenceText)}"),
                    FindingKind.RepeatedWord,
                    $"“{occurrences[0].Text}” is repeated {occurrences.Length} times in the same sentence.",
                    occurrences[1].Span,
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
        foreach (var paragraph in paragraphs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frequent = tokens
                .Where(token => token.Span.Start >= paragraph.Start && token.Span.End <= paragraph.End)
                .Where(token => strict || !CommonWords.Contains(token.Normalized))
                .GroupBy(token => token.Normalized)
                .Where(group => group.Count() >= threshold)
                .OrderByDescending(group => group.Count())
                .Take(12);

            foreach (var group in frequent)
            {
                var occurrences = group.ToArray();
                foreach (var occurrence in occurrences)
                {
                    highlights[(occurrence.Span.Start, occurrence.Span.Length)] = occurrence.Span;
                }

                findings.Add(new TextFinding(
                    StableFindingId.Create("paragraph-frequency", $"{paragraph.Start}|{group.Key}"),
                    FindingKind.FrequentWord,
                    $"“{occurrences[0].Text}” appears {occurrences.Length} times in this paragraph.",
                    occurrences[0].Span,
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
        var frequent = tokens
            .Where(token => strict || !CommonWords.Contains(token.Normalized))
            .GroupBy(token => token.Normalized)
            .Where(group => group.Count() >= documentThreshold)
            .OrderByDescending(group => group.Count())
            .Take(15);

        foreach (var group in frequent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var occurrences = group.ToArray();
            foreach (var occurrence in occurrences)
            {
                highlights[(occurrence.Span.Start, occurrence.Span.Length)] = occurrence.Span;
            }

            findings.Add(new TextFinding(
                StableFindingId.Create("document-frequency", group.Key),
                FindingKind.FrequentWord,
                $"“{occurrences[0].Text}” appears {occurrences.Length} times in the document.",
                occurrences[0].Span,
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
        var groups = sentences
            .Select(span => new { Span = span, Normalized = Normalize(text.Substring(span.Start, span.Length)) })
            .Where(item => item.Normalized.Length >= 10)
            .GroupBy(item => item.Normalized)
            .Where(group => group.Count() > 1);

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var duplicates = group.ToArray();
            foreach (var duplicate in duplicates.Skip(1))
            {
                highlights[(duplicate.Span.Start, duplicate.Span.Length)] = duplicate.Span;
                findings.Add(new TextFinding(
                    StableFindingId.Create("duplicate-sentence", group.Key),
                    FindingKind.DuplicateSentence,
                    "This sentence duplicates an earlier sentence.",
                    duplicate.Span,
                    "Remove it or combine the two passages."));
            }
        }
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
