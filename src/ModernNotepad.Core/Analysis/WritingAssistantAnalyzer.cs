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
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly Regex PassiveVoiceRegex = new(
        @"\b(?:am|is|are|was|were|be|been|being)\s+(?:[\p{L}\p{M}]+ly\s+){0,2}(?:[\p{L}\p{M}]+(?:ed|en)|built|bought|brought|caught|done|drawn|driven|felt|found|given|grown|held|kept|known|left|lost|made|paid|read|said|seen|sent|shown|sold|spent|taken|taught|told|thought|understood|won|written)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly Regex AAnRegex = new(
        @"\b(a|an)\s+([\p{L}\p{M}]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly IReadOnlyDictionary<string, string> PhraseCorrections =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["could of"] = "could have",
            ["should of"] = "should have",
            ["would of"] = "would have",
            ["irregardless"] = "regardless"
        };

    private static readonly HashSet<string> SilentHWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "heir", "heiress", "honest", "honestly", "honor", "honour", "honorable", "honourable", "hour", "hourly"
    };

    private static readonly string[] ConsonantSoundVowelPrefixes =
    {
        "uni", "use", "user", "usual", "utility", "utensil", "euro", "ewe", "eul", "eup", "one", "once"
    };

    private static readonly HashSet<string> AmbiguousInitialisms = new(StringComparer.OrdinalIgnoreCase)
    {
        "SQL", "URL", "FAQ"
    };

    private static readonly IReadOnlyDictionary<string, (string Wrong, string Suggestion)> SingularPronounAgreement =
        new Dictionary<string, (string Wrong, string Suggestion)>(StringComparer.OrdinalIgnoreCase)
        {
            ["are"] = ("are", "is"),
            ["were"] = ("were", "was"),
            ["have"] = ("have", "has"),
            ["do"] = ("do", "does")
        };

    private static readonly IReadOnlyDictionary<string, (string Wrong, string Suggestion)> PluralPronounAgreement =
        new Dictionary<string, (string Wrong, string Suggestion)>(StringComparer.OrdinalIgnoreCase)
        {
            ["is"] = ("is", "are"),
            ["was"] = ("was", "were"),
            ["has"] = ("has", "have"),
            ["does"] = ("does", "do")
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
        FindHighConfidenceAgreementProblems(text, tokens, findings, cancellationToken);
        AnalyzeSentences(text, tokens, sentences, longSentenceWordThreshold, detectPassiveVoice, findings, cancellationToken);

        return findings
            .OrderBy(finding => finding.Span?.Start ?? int.MaxValue)
            .ThenBy(finding => finding.Kind)
            .ToArray();
    }

    private static void FindCommonTypos(
        IReadOnlyList<TextToken> tokens,
        ICollection<TextFinding> findings,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < tokens.Count; index++)
        {
            if ((index & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var token = tokens[index];
            if (!CommonTypos.TryGetValue(token.Normalized, out var suggestion))
            {
                continue;
            }

            findings.Add(new TextFinding(
                StableFindingId.Create("spelling", $"{token.Normalized}|{token.Span.Start}"),
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

                if (IsWordBounded(text, index, correction.Key.Length))
                {
                    findings.Add(new TextFinding(
                        StableFindingId.Create("phrase", $"{correction.Key}|{index}"),
                        FindingKind.Grammar,
                        $"Consider replacing “{correction.Key}”.",
                        new TextSpan(index, correction.Key.Length),
                        correction.Value));
                }

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
            if (nextWord.Length == 0 || AmbiguousInitialisms.Contains(nextWord))
            {
                continue;
            }

            var shouldUseAn = BeginsWithVowelSound(nextWord);
            if ((article == "a" && shouldUseAn) || (article == "an" && !shouldUseAn))
            {
                var suggestion = shouldUseAn ? "an" : "a";
                findings.Add(new TextFinding(
                    StableFindingId.Create("article", $"{article}|{nextWord.ToLowerInvariant()}|{match.Index}"),
                    FindingKind.Grammar,
                    $"Check the article before “{nextWord}”.",
                    new TextSpan(match.Groups[1].Index, match.Groups[1].Length),
                    suggestion,
                    FindingSeverity.Information));
            }
        }
    }

    private static bool BeginsWithVowelSound(string word)
    {
        if (SilentHWords.Contains(word))
        {
            return true;
        }

        var lower = word.ToLowerInvariant();
        foreach (var prefix in ConsonantSoundVowelPrefixes)
        {
            if (lower.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (LooksLikeInitialism(word))
        {
            // Letter names beginning with a vowel sound: A, E, F, H, I, L, M, N, O, R, S, X.
            return "AEFHILMNORSX".Contains(char.ToUpperInvariant(word[0]));
        }

        return "aeiou".Contains(char.ToLowerInvariant(word[0]));
    }

    private static bool LooksLikeInitialism(string word)
    {
        if (word.Length == 1)
        {
            return char.IsUpper(word[0]);
        }

        var uppercase = 0;
        for (var index = 0; index < word.Length; index++)
        {
            if (char.IsUpper(word[index]))
            {
                uppercase++;
            }
        }

        return uppercase == word.Length || (word.Length <= 5 && uppercase >= 2 && char.IsUpper(word[0]));
    }

    private static void FindHighConfidenceAgreementProblems(
        string text,
        IReadOnlyList<TextToken> tokens,
        ICollection<TextFinding> findings,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index + 1 < tokens.Count; index++)
        {
            if ((index & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var subject = tokens[index];
            var nextIndex = index + 1;
            while (nextIndex < tokens.Count && IsSimpleInterveningAdverb(tokens[nextIndex].Normalized) && nextIndex - index <= 2)
            {
                nextIndex++;
            }

            if (nextIndex >= tokens.Count || HasStrongBoundary(text, subject.Span.End, tokens[nextIndex].Span.Start))
            {
                continue;
            }

            var verb = tokens[nextIndex];
            string? suggestion = null;
            if (subject.Normalized is "he" or "she" or "it")
            {
                if (SingularPronounAgreement.TryGetValue(verb.Normalized, out var rule))
                {
                    suggestion = PreserveCase(verb.Text, rule.Suggestion);
                }
            }
            else if (subject.Normalized is "you" or "we" or "they")
            {
                if (PluralPronounAgreement.TryGetValue(verb.Normalized, out var rule))
                {
                    suggestion = PreserveCase(verb.Text, rule.Suggestion);
                }
            }
            else if (subject.Normalized == "i" && verb.Normalized is ("is" or "are" or "has" or "does"))
            {
                suggestion = verb.Normalized switch
                {
                    "is" or "are" => PreserveCase(verb.Text, "am"),
                    "has" => PreserveCase(verb.Text, "have"),
                    "does" => PreserveCase(verb.Text, "do"),
                    _ => null
                };
            }

            if (suggestion is null)
            {
                continue;
            }

            findings.Add(new TextFinding(
                StableFindingId.Create("agreement", $"{subject.Span.Start}|{verb.Span.Start}"),
                FindingKind.Grammar,
                $"Possible subject–verb agreement issue: “{subject.Text} {verb.Text}”.",
                verb.Span,
                suggestion,
                FindingSeverity.Warning));
        }
    }

    private static bool IsSimpleInterveningAdverb(string word) =>
        word is "not" or "never" or "always" or "usually" or "often" or "sometimes" or "still" or "already" or "also" or "really" or "probably";

    private static void AnalyzeSentences(
        string text,
        IReadOnlyList<TextToken> tokens,
        IReadOnlyList<TextSpan> sentences,
        int threshold,
        bool detectPassiveVoice,
        ICollection<TextFinding> findings,
        CancellationToken cancellationToken)
    {
        var ranges = SpanTokenIndex.Align(tokens, sentences, cancellationToken);
        foreach (var range in ranges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sentence = range.Span;

            if (range.Count > threshold)
            {
                findings.Add(new TextFinding(
                    StableFindingId.Create("long-sentence", $"{sentence.Start}|{NormalizeForId(text, sentence)}"),
                    FindingKind.LongSentence,
                    $"Long sentence: {range.Count} words.",
                    sentence,
                    $"Consider splitting it into two sentences (target: {threshold} words or fewer).",
                    FindingSeverity.Information));
            }

            var firstLetter = FindFirstLetter(text, sentence);
            if (firstLetter >= 0 && char.IsLower(text[firstLetter]) && LooksLikeProseSentenceStart(text, sentence, firstLetter))
            {
                findings.Add(new TextFinding(
                    StableFindingId.Create("lowercase-start", $"{sentence.Start}|{NormalizeForId(text, sentence)}"),
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
                    StableFindingId.Create("passive", $"{sentence.Start + match.Index}|{match.Value.ToLowerInvariant()}"),
                    FindingKind.PassiveVoice,
                    "Possible passive voice.",
                    new TextSpan(sentence.Start + match.Index, match.Length),
                    "Consider naming the actor and using an active verb.",
                    FindingSeverity.Information));
            }
        }
    }

    private static bool LooksLikeProseSentenceStart(string text, TextSpan sentence, int firstLetter)
    {
        // Avoid flagging common code/markup fragments as prose capitalization errors.
        for (var index = sentence.Start; index < firstLetter; index++)
        {
            if (text[index] is '<' or '>' or '{' or '}' or '[' or ']' or '#' or '@' or '`')
            {
                return false;
            }
        }
        return true;
    }

    private static bool HasStrongBoundary(string text, int start, int end)
    {
        for (var index = Math.Max(0, start); index < Math.Min(text.Length, end); index++)
        {
            if (text[index] is '.' or '!' or '?' or ';' or ':' or '\n' or '\r')
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsWordBounded(string text, int start, int length)
    {
        var before = start == 0 || !char.IsLetterOrDigit(text[start - 1]);
        var end = start + length;
        var after = end >= text.Length || !char.IsLetterOrDigit(text[end]);
        return before && after;
    }

    private static string PreserveCase(string original, string replacement)
    {
        if (original.Length > 0 && char.IsUpper(original[0]))
        {
            return char.ToUpperInvariant(replacement[0]) + replacement[1..];
        }
        return replacement;
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
