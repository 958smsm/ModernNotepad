namespace ModernNotepad.Core.Analysis;

/// <summary>
/// Aligns sorted, non-overlapping text spans with the already-tokenized document in one forward pass.
/// This avoids rescanning the entire token collection for every sentence or paragraph.
/// </summary>
internal static class SpanTokenIndex
{
    public static IReadOnlyList<TokenRange> Align(
        IReadOnlyList<TextToken> tokens,
        IReadOnlyList<TextSpan> spans,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(spans);

        var ranges = new TokenRange[spans.Count];
        var cursor = 0;

        for (var spanIndex = 0; spanIndex < spans.Count; spanIndex++)
        {
            if ((spanIndex & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var span = spans[spanIndex];
            while (cursor < tokens.Count && tokens[cursor].Span.End <= span.Start)
            {
                cursor++;
            }

            var start = cursor;
            while (cursor < tokens.Count
                   && tokens[cursor].Span.Start < span.End
                   && tokens[cursor].Span.End <= span.End)
            {
                cursor++;
            }

            ranges[spanIndex] = new TokenRange(span, start, cursor - start);
        }

        return ranges;
    }

    internal readonly record struct TokenRange(TextSpan Span, int Start, int Count)
    {
        public int End => Start + Count;
    }
}
