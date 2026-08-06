using System.Text.RegularExpressions;

namespace ModernNotepad.Core.Analysis;

public sealed class WritingAssistantAnalyzer
{
    private static readonly IReadOnlyDictionary<string, string> CommonTypos =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["teh"] = "the",
            ["adn"] = "and",
            ["recieve"] = "receive",
            ["seperate"] = "separate",
            ["definately"] = "definitely",
            ["occured"] = "occurred",
            ["untill"] = "until",
            ["alot"] = "a lot",
            ["wich"] = "which",
            ["thier"] = "their",
            ["wierd"] = "weird",
            ["accomodate"] = "accommodate",
            ["begining"] = "beginning",
            ["beleive"] = "believe",
            ["calender"] = "calendar",
            ["enviroment"] = "environment",
            ["goverment"] = "government",
            ["independant"] = "independent",
            ["neccessary"] = "necessary",
            ["publically"] = "publicly",
            ["succesful"] = "successful"
        };

    private static readonly Regex DoubleSpaceRegex = new(
        @" {2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PassiveVoiceRegex = new(
        @"\b(?:am|is|are|was|were|be|been|being)\s+(?:\w+ly\s+){0,2}\w+(?:ed|en)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex AAnRegex = new(
        @"\b(a|an)\s+([\p{L}\p{M}]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, string> PhraseCorrections =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["could of"] = "could have",
            ["should of"] = "should have",
            ["would of"] = "would have",
            ["irregardless"] = "regardless"
        };

    public IReadOnlyList<TextFinding> Analyze(
        string text,
        IReadOnlyList<TextToken>? tokens,
        IReadOnlyList<TextSpan>? sentences,
        int longSentenceWordThreshold,
        bool detectPassiveVoice,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        tokens ??= TextTokenizer.Tokenize(text, cancellationToken);
        sentences ??= TextSegmentation.GetSentences(text, cancellationToken);
        longSentenceWordThreshold = Math.Clamp(longSentenceWordThreshold, 10, 200);

        var findings = new List<TextFinding>();
        FindCommonTypos(tokens, findings, cancellationToken);
        FindSpacingProblems(text, findings, cancellationToken);
        FindPhraseProblems(text, findings, cancellationToken);
        FindArticleProblems(text, findings, cancellationToken);
        AnalyzeSentences(text, tokens, sentences, longSentenceWordThreshold, detectPassiveVoice, findings, cancellationToken);

        return findings
            .OrderBy(finding => finding.Span?.Start ?? int.MaxValue)
            .Take(500)
            .ToArray();
    }

    private static void FindCommonTypos(
        IReadOnlyList<TextToken> tokens,
        ICollection<TextFinding> findings,
        CancellationToken cancellationToken)
    {
        foreach (var token in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CommonTypos.TryGetValue(token.Normalized, out var suggestion))
            {
                continue;
            }

            findings.Add(new TextFinding(
                StableFindingId.Create("spelling", token.Normalized),
                FindingKind.Spelling,
                $"Possible misspelling: “{token.Text}”.",
                token.Span,
                suggestion));
        }
    }

    private static void FindSpacingProblems(
        string text,
        ICollection<TextFinding> findings,
        CancellationToken cancellationToken)
    {
        foreach (Match match in DoubleSpaceRegex.Matches(text))
        {
            cancellationToken.ThrowIfCancellationRequested();
            findings.Add(new TextFinding(
                StableFindingId.Create("double-space", match.Index.ToString()),
                FindingKind.Grammar,
                "Multiple consecutive spaces found.",
                new TextSpan(match.Index, match.Length),
                "Use a single space.",
                FindingSeverity.Information));
        }
    }

    private static void FindPhraseProblems(
        string text,
        ICollection<TextFinding> findings,
        CancellationToken cancellationToken)
    {
        foreach (var correction in PhraseCorrections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = 0;
            while (start < text.Length)
            {
                var index = text.IndexOf(correction.Key, start, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    break;
                }

                findings.Add(new TextFinding(
                    StableFindingId.Create("phrase", correction.Key),
                    FindingKind.Grammar,
                    $"Consider replacing “{correction.Key}”.",
                    new TextSpan(index, correction.Key.Length),
                    correction.Value));
                start = index + correction.Key.Length;
            }
        }
    }

    private static void FindArticleProblems(
        string text,
        ICollection<TextFinding> findings,
        CancellationToken cancellationToken)
    {
        foreach (Match match in AAnRegex.Matches(text))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var article = match.Groups[1].Value.ToLowerInvariant();
            var nextWord = match.Groups[2].Value;
            if (nextWord.Length == 0)
            {
                continue;
            }

            var beginsWithVowel = "aeiou".Contains(char.ToLowerInvariant(nextWord[0]));
            if ((article == "a" && beginsWithVowel) || (article == "an" && !beginsWithVowel))
            {
                var suggestion = article == "a" ? "an" : "a";
                findings.Add(new TextFinding(
                    StableFindingId.Create("article", $"{article}|{nextWord.ToLowerInvariant()}"),
                    FindingKind.Grammar,
                    $"Check the article before “{nextWord}”.",
                    new TextSpan(match.Groups[1].Index, match.Groups[1].Length),
                    suggestion,
                    FindingSeverity.Information));
            }
        }
    }

    private static void AnalyzeSentences(
        string text,
        IReadOnlyList<TextToken> tokens,
        IReadOnlyList<TextSpan> sentences,
        int threshold,
        bool detectPassiveVoice,
        ICollection<TextFinding> findings,
        CancellationToken cancellationToken)
    {
        foreach (var sentence in sentences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sentenceTokens = tokens
                .Where(token => token.Span.Start >= sentence.Start && token.Span.End <= sentence.End)
                .ToArray();

            if (sentenceTokens.Length > threshold)
            {
                findings.Add(new TextFinding(
                    StableFindingId.Create("long-sentence", NormalizeForId(text, sentence)),
                    FindingKind.LongSentence,
                    $"Long sentence: {sentenceTokens.Length} words.",
                    sentence,
                    $"Consider splitting it into two sentences (target: {threshold} words or fewer).",
                    FindingSeverity.Information));
            }

            var firstLetter = FindFirstLetter(text, sentence);
            if (firstLetter >= 0 && char.IsLower(text[firstLetter]))
            {
                findings.Add(new TextFinding(
                    StableFindingId.Create("lowercase-start", NormalizeForId(text, sentence)),
                    FindingKind.Grammar,
                    "The sentence begins with a lowercase letter.",
                    new TextSpan(firstLetter, 1),
                    char.ToUpperInvariant(text[firstLetter]).ToString(),
                    FindingSeverity.Information));
            }

            if (!detectPassiveVoice)
            {
                continue;
            }

            var sentenceText = text.Substring(sentence.Start, sentence.Length);
            foreach (Match match in PassiveVoiceRegex.Matches(sentenceText))
            {
                findings.Add(new TextFinding(
                    StableFindingId.Create("passive", match.Value.ToLowerInvariant()),
                    FindingKind.PassiveVoice,
                    "Possible passive voice.",
                    new TextSpan(sentence.Start + match.Index, match.Length),
                    "Consider naming the actor and using an active verb.",
                    FindingSeverity.Information));
            }
        }
    }

    private static int FindFirstLetter(string text, TextSpan sentence)
    {
        for (var index = sentence.Start; index < sentence.End; index++)
        {
            if (char.IsLetter(text[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static string NormalizeForId(string text, TextSpan span)
    {
        return string.Join(' ', text.Substring(span.Start, span.Length)
            .ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
