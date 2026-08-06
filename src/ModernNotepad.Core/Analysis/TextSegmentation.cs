namespace ModernNotepad.Core.Analysis;

public static class TextSegmentation
{
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

            var character = text[index];
            var isBoundary = character is '.' or '!' or '?';
            if (!isBoundary)
            {
                continue;
            }

            var next = index + 1;
            while (next < text.Length && text[next] is '"' or '\'' or '”' or '’' or ')' or ']')
            {
                next++;
            }

            if (next < text.Length && !char.IsWhiteSpace(text[next]))
            {
                continue;
            }

            var span = Trim(text, start, next - start);
            if (!span.IsEmpty)
            {
                sentences.Add(span);
            }

            start = next;
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
            while (probe < text.Length && (text[probe] == ' ' || text[probe] == '\t'))
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
