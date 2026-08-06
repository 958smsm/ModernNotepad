using System.Text;

namespace ModernNotepad.Core.Models;

public enum LineEnding
{
    CrLf,
    Lf,
    Cr
}

public sealed record LineEndingProfile(
    LineEnding Preferred,
    IReadOnlyList<LineEnding> Sequence,
    int CrLfCount,
    int LfCount,
    int CrCount)
{
    public static LineEndingProfile WindowsDefault { get; } =
        new(LineEnding.CrLf, Array.Empty<LineEnding>(), 0, 0, 0);

    public bool HasMixedLineEndings =>
        new[] { CrLfCount, LfCount, CrCount }.Count(value => value > 0) > 1;

    public string PreferredText => Preferred switch
    {
        LineEnding.Lf => "LF",
        LineEnding.Cr => "CR",
        _ => "CRLF"
    };

    public static LineEndingProfile Detect(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var sequence = new List<LineEnding>();
        var crlf = 0;
        var lf = 0;
        var cr = 0;

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    crlf++;
                    sequence.Add(LineEnding.CrLf);
                    index++;
                }
                else
                {
                    cr++;
                    sequence.Add(LineEnding.Cr);
                }
            }
            else if (text[index] == '\n')
            {
                lf++;
                sequence.Add(LineEnding.Lf);
            }
        }

        var preferred = LineEnding.CrLf;
        if (lf > crlf && lf >= cr)
        {
            preferred = LineEnding.Lf;
        }
        else if (cr > crlf && cr > lf)
        {
            preferred = LineEnding.Cr;
        }

        return new LineEndingProfile(preferred, sequence, crlf, lf, cr);
    }

    public string ApplyTo(string editorText)
    {
        ArgumentNullException.ThrowIfNull(editorText);

        var normalized = NormalizeToLf(editorText);
        if (!normalized.Contains('\n'))
        {
            return normalized;
        }

        var output = new StringBuilder(normalized.Length + 32);
        var breakIndex = 0;

        foreach (var character in normalized)
        {
            if (character != '\n')
            {
                output.Append(character);
                continue;
            }

            var lineEnding = breakIndex < Sequence.Count ? Sequence[breakIndex] : Preferred;
            output.Append(ToText(lineEnding));
            breakIndex++;
        }

        return output.ToString();
    }

    public static string NormalizeToLf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    public static string ToText(LineEnding ending) => ending switch
    {
        LineEnding.Lf => "\n",
        LineEnding.Cr => "\r",
        _ => "\r\n"
    };
}
