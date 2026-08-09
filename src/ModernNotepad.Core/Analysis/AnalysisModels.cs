namespace ModernNotepad.Core.Analysis;

public enum GrammarCategory
{
    Other,
    SubjectNoun,
    Verb,
    ObjectNoun,
    Adjective,
    Adverb,
    Pronoun,
    Preposition,
    Conjunction,
    Interrogative,
    Quantifier,
    Determiner,
    Particle
}

public enum FindingKind
{
    RepeatedWord,
    FrequentWord,
    DuplicateSentence,
    Spelling,
    Grammar,
    LongSentence,
    PassiveVoice,
    Validation
}

public enum FindingSeverity
{
    Information,
    Warning,
    Error
}

public readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;
    public bool IsEmpty => Length <= 0;
}

public sealed record ColoredSpan(TextSpan Span, GrammarCategory Category);

public sealed record TextFinding(
    string Id,
    FindingKind Kind,
    string Message,
    TextSpan? Span = null,
    string? Suggestion = null,
    FindingSeverity Severity = FindingSeverity.Warning);

public sealed record TextStatistics(
    int WordCount,
    int CharacterCount,
    int CharacterCountWithoutWhitespace,
    int SentenceCount,
    int ParagraphCount,
    double ReadingTimeMinutes,
    double ReadabilityScore,
    IReadOnlyDictionary<GrammarCategory, int> GrammarCategoryCounts)
{
    public static TextStatistics Empty { get; } = new(
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        new Dictionary<GrammarCategory, int>());
}

public sealed record DocumentAnalysis(
    TextStatistics Statistics,
    IReadOnlyList<TextFinding> Findings,
    IReadOnlyList<ColoredSpan> ColoredSpans,
    IReadOnlyList<TextSpan> DuplicateSpans)
{
    public static DocumentAnalysis Empty { get; } = new(
        TextStatistics.Empty,
        Array.Empty<TextFinding>(),
        Array.Empty<ColoredSpan>(),
        Array.Empty<TextSpan>());
}

public sealed record TextToken(string Text, string Normalized, TextSpan Span);

public sealed record GrammarAnalysis(
    IReadOnlyList<ColoredSpan> Spans,
    IReadOnlyDictionary<GrammarCategory, int> Counts);

public sealed record DuplicateAnalysis(
    IReadOnlyList<TextFinding> Findings,
    IReadOnlyList<TextSpan> HighlightSpans);
