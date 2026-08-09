namespace ModernNotepad.Core.Analysis;

public sealed class TextStatisticsAnalyzer
{
    public TextStatistics Analyze(
        string text,
        IReadOnlyList<TextToken>? tokens = null,
        IReadOnlyList<TextSpan>? sentences = null,
        IReadOnlyList<TextSpan>? paragraphs = null,
        IReadOnlyDictionary<GrammarCategory, int>? categoryCounts = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        tokens ??= TextTokenizer.Tokenize(text, cancellationToken);
        sentences ??= TextSegmentation.GetSentences(text, cancellationToken);
        paragraphs ??= TextSegmentation.GetParagraphs(text, cancellationToken);
        categoryCounts ??= new Dictionary<GrammarCategory, int>();

        var nonWhitespace = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if ((index & 2047) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!char.IsWhiteSpace(text[index]))
            {
                nonWhitespace++;
            }
        }

        var wordCount = tokens.Count;
        var sentenceCount = sentences.Count;
        var readingTime = wordCount == 0 ? 0 : wordCount / 200d;
        var syllables = 0;
        for (var index = 0; index < tokens.Count; index++)
        {
            if ((index & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            syllables += EstimateSyllables(tokens[index].Normalized);
        }
        var readability = CalculateFleschReadingEase(wordCount, sentenceCount, syllables);

        return new TextStatistics(
            wordCount,
            text.Length,
            nonWhitespace,
            sentenceCount,
            paragraphs.Count,
            readingTime,
            readability,
            new Dictionary<GrammarCategory, int>(categoryCounts));
    }

    private static double CalculateFleschReadingEase(int words, int sentences, int syllables)
    {
        if (words == 0 || sentences == 0)
        {
            return 0;
        }

        var score = 206.835 - (1.015 * words / sentences) - (84.6 * syllables / words);
        return Math.Round(Math.Clamp(score, 0, 100), 1);
    }

    private static int EstimateSyllables(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return 0;
        }

        var count = 0;
        var previousWasVowel = false;
        for (var index = 0; index < word.Length; index++)
        {
            var isVowel = "aeiouy".Contains(char.ToLowerInvariant(word[index]));
            if (isVowel && !previousWasVowel)
            {
                count++;
            }

            previousWasVowel = isVowel;
        }

        if (word.Length > 2 && word.EndsWith('e') && !word.EndsWith("le", StringComparison.OrdinalIgnoreCase))
        {
            count--;
        }

        return Math.Max(1, count);
    }
}
