namespace ModernNotepad.Core.Analysis;

internal static class ProviderGrammarAnalysis
{
    private static readonly GrammarCategory[] Categories = Enum.GetValues<GrammarCategory>();

    public static GrammarAnalysis Create(
        IReadOnlyList<TextToken> tokens,
        IReadOnlyList<GrammarCategory> assignments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(assignments);

        if (assignments.Count != tokens.Count)
        {
            throw new InvalidDataException(
                $"Grammar provider returned {assignments.Count} of {tokens.Count} token classifications.");
        }

        var counts = Categories.ToDictionary(category => category, _ => 0);
        var spans = new List<ColoredSpan>(tokens.Count);
        for (var index = 0; index < tokens.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var category = assignments[index];
            if (!counts.ContainsKey(category))
            {
                throw new InvalidDataException(
                    $"Grammar provider returned invalid category '{category}' for token {index}.");
            }

            counts[category]++;
            if (category != GrammarCategory.Other)
            {
                spans.Add(new ColoredSpan(tokens[index].Span, category));
            }
        }

        return new GrammarAnalysis(spans, counts);
    }

    public static GrammarAnalysis Empty() => new(
        Array.Empty<ColoredSpan>(),
        Categories.ToDictionary(category => category, _ => 0));
}
