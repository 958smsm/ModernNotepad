namespace ModernNotepad.Core.Analysis;

public static class TextSegmentation
{
    private static readonly HashSet<string> NonTerminalAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "mr", "mrs", "ms", "dr", "prof", "rev", "hon", "pres", "gov", "sen", "rep", "gen", "sgt", "lt", "col",
        "capt", "cmdr", "sr", "jr", "st", "mt", "ft", "dept", "est", "fig", "eq", "ref", "refs", "vol", "pp",
        "jan", "feb", "mar", "apr", "jun", "jul", "aug", "sep", "sept", "oct", "nov", "dec"
    };

    private static readonly HashSet<string> ContextualAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "etc", "vs", "approx", "misc", "incl", "inc", "ltd", "corp", "co", "no", "nos", "ed", "eds", "trans"
    };

    private static readonly HashSet<string> LikelySentenceStarters = new(StringComparer.OrdinalIgnoreCase)
    {
        "i", "he", "she", "it", "we", "they", "you", "the", "this", "that", "these", "those", "there",
        "however", "therefore", "meanwhile", "nevertheless", "nonetheless", "but", "then", "next", "finally"
    };

    public static IReadOnlyList<TextSpan> GetSentences(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var sentences = new List<TextSpan>();
        var start = 0;

        for (var index = 0; index < text.Length; index++)
        {
            if ((index & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!IsSentenceBoundary(text, index))
            {
                continue;
            }

            var boundaryEnd = ConsumeTerminalRun(text, index);
            var next = ConsumeClosingPunctuation(text, boundaryEnd);
            var span = Trim(text, start, next - start);
            if (!span.IsEmpty)
            {
                sentences.Add(span);
            }

            start = next;
            index = Math.Max(index, next - 1);
        }

        var finalSpan = Trim(text, start, text.Length - start);
        if (!finalSpan.IsEmpty)
        {
            sentences.Add(finalSpan);
        }

        return sentences;
    }

    public static IReadOnlyList<TextSpan> GetParagraphs(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var paragraphs = new List<TextSpan>();
        var start = 0;
        var index = 0;

        while (index < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (text[index] is not ('\r' or '\n'))
            {
                index++;
                continue;
            }

            var firstBreakEnd = ConsumeLineBreak(text, index);
            var probe = firstBreakEnd;
            while (probe < text.Length && text[probe] is ' ' or '\t')
            {
                probe++;
            }

            if (probe >= text.Length || text[probe] is not ('\r' or '\n'))
            {
                index = firstBreakEnd;
                continue;
            }

            var span = Trim(text, start, index - start);
            if (!span.IsEmpty)
            {
                paragraphs.Add(span);
            }

            index = ConsumeLineBreak(text, probe);
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            start = index;
        }

        var finalSpan = Trim(text, start, text.Length - start);
        if (!finalSpan.IsEmpty)
        {
            paragraphs.Add(finalSpan);
        }

        return paragraphs;
    }

    private static bool IsSentenceBoundary(string text, int index)
    {
        var character = text[index];
        if (character is '!' or '?')
        {
            // Treat a run such as "?!" or "!!!" as one boundary at its final mark.
            return index + 1 >= text.Length || text[index + 1] is not ('!' or '?');
        }

        if (character != '.')
        {
            return false;
        }

        if (index > 0 && index + 1 < text.Length
            && char.IsDigit(text[index - 1]) && char.IsDigit(text[index + 1]))
        {
            return false; // decimal/version number: 3.14, 1.2
        }

        if (index + 1 < text.Length && text[index + 1] == '.')
        {
            return false; // wait for the end of an ellipsis
        }

        if (index > 0 && text[index - 1] == '.')
        {
            // Final dot of an ellipsis is a sentence boundary only when the following
            // non-space token looks like a new sentence or the document ends.
            var ellipsisNext = FindNextNonWhitespace(text, index + 1);
            return ellipsisNext < 0 || char.IsUpper(text[ellipsisNext]) || IsOpeningQuote(text[ellipsisNext]);
        }

        var next = ConsumeClosingPunctuation(text, index + 1);
        if (next < text.Length && !char.IsWhiteSpace(text[next]))
        {
            return false; // domain names, filenames, abbreviations such as e.g., etc.
        }

        var wordStart = index;
        while (wordStart > 0 && char.IsLetter(text[wordStart - 1]))
        {
            wordStart--;
        }

        var precedingWord = text.AsSpan(wordStart, index - wordStart);
        if (!precedingWord.IsEmpty)
        {
            var abbreviation = precedingWord.ToString();
            if (NonTerminalAbbreviations.Contains(abbreviation))
            {
                return false;
            }

            var nextNonWhitespace = FindNextNonWhitespace(text, next);
            if (ContextualAbbreviations.Contains(abbreviation)
                && nextNonWhitespace >= 0
                && char.IsLower(text[nextNonWhitespace]))
            {
                return false;
            }

            // A single capital followed by a surname is normally an initial, not an
            // end of sentence: "A. Smith". The same rule also handles acronym pieces.
            var isInitialismPiece = wordStart > 0 && text[wordStart - 1] == '.';
            if (!isInitialismPiece
                && precedingWord.Length == 1
                && char.IsUpper(precedingWord[0])
                && nextNonWhitespace >= 0
                && char.IsUpper(text[nextNonWhitespace]))
            {
                return false;
            }
        }

        // Handle multi-period initialisms such as U.S. or Ph.D. when followed by a
        // continuation token. This deliberately stays conservative at paragraph/end.
        if (LooksLikeInitialismEndingAt(text, index))
        {
            var nextNonWhitespace = FindNextNonWhitespace(text, next);
            if (nextNonWhitespace >= 0 && !StartsLikelyNewSentenceAfterInitialism(text, nextNonWhitespace))
            {
                return false;
            }
        }

        return true;
    }

    private static int ConsumeTerminalRun(string text, int index)
    {
        var end = index + 1;
        while (end < text.Length && text[end] is '!' or '?')
        {
            end++;
        }

        return end;
    }

    private static int ConsumeClosingPunctuation(string text, int index)
    {
        while (index < text.Length && text[index] is '"' or '\'' or '”' or '’' or ')' or ']' or '}')
        {
            index++;
        }

        return index;
    }

    private static int FindNextNonWhitespace(string text, int start)
    {
        for (var index = start; index < text.Length; index++)
        {
            if (!char.IsWhiteSpace(text[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsOpeningQuote(char character) => character is '"' or '\'' or '“' or '‘';

    private static bool LooksLikeInitialismEndingAt(string text, int periodIndex)
    {
        var dots = 0;
        var letters = 0;
        var cursor = periodIndex;
        while (cursor >= 1)
        {
            if (text[cursor] != '.' || !char.IsLetter(text[cursor - 1]))
            {
                break;
            }

            dots++;
            letters++;
            cursor -= 2;
            if (cursor < 0 || text[cursor] != '.')
            {
                break;
            }
        }

        return dots >= 2 && letters >= 2;
    }

    private static bool StartsLikelyNewSentenceAfterInitialism(string text, int index)
    {
        for (var cursor = index - 1; cursor >= 0 && char.IsWhiteSpace(text[cursor]); cursor--)
        {
            if (text[cursor] is '\r' or '\n')
            {
                return true;
            }
        }

        while (index < text.Length && IsOpeningQuote(text[index]))
        {
            index++;
        }

        if (index >= text.Length || !char.IsUpper(text[index]))
        {
            return false;
        }

        var end = index + 1;
        while (end < text.Length && char.IsLetter(text[end]))
        {
            end++;
        }

        return LikelySentenceStarters.Contains(text.Substring(index, end - index));
    }

    private static int ConsumeLineBreak(string text, int index)
    {
        if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
        {
            return index + 2;
        }

        return index + 1;
    }

    private static TextSpan Trim(string text, int start, int length)
    {
        var end = start + length;
        while (start < end && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        return new TextSpan(start, end - start);
    }
}
