using System.Text.RegularExpressions;

namespace ModernNotepad.Core.Analysis;

public static class TextTokenizer
{
    private static readonly Regex WordRegex = new(
        @"\b[\p{L}\p{M}]+(?:['’\-][\p{L}\p{M}]+)*\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<TextToken> Tokenize(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var tokens = new List<TextToken>();
        var index = 0;
        foreach (Match match in WordRegex.Matches(text))
        {
            if ((index++ & 127) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            tokens.Add(new TextToken(
                match.Value,
                match.Value.ToLowerInvariant(),
                new TextSpan(match.Index, match.Length)));
        }

        return tokens;
    }
}
