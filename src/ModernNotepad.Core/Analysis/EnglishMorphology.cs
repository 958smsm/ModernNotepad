using System.Numerics;

namespace ModernNotepad.Core.Analysis;

/// <summary>
/// Allocation-conscious lexical and productive-morphology resolver. The
/// generated lexicon contains attested forms and WordNet exceptions; this
/// resolver additionally maps novel regular inflections back to known lemmas.
/// </summary>
internal static class EnglishMorphology
{
    private static readonly Dictionary<string, string> IrregularLemmas =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["am"] = "be",
            ["is"] = "be",
            ["are"] = "be",
            ["was"] = "be",
            ["were"] = "be",
            ["been"] = "be",
            ["being"] = "be",
            ["has"] = "have",
            ["had"] = "have",
            ["does"] = "do",
            ["did"] = "do",
            ["done"] = "do",
            ["went"] = "go",
            ["gone"] = "go",
            ["came"] = "come",
            ["ran"] = "run",
            ["saw"] = "see",
            ["seen"] = "see",
            ["made"] = "make",
            ["took"] = "take",
            ["taken"] = "take",
            ["gave"] = "give",
            ["given"] = "give",
            ["wrote"] = "write",
            ["written"] = "write",
            ["spoke"] = "speak",
            ["spoken"] = "speak",
            ["drove"] = "drive",
            ["driven"] = "drive",
            ["knew"] = "know",
            ["known"] = "know",
            ["thought"] = "think",
            ["bought"] = "buy",
            ["brought"] = "bring",
            ["caught"] = "catch",
            ["taught"] = "teach",
            ["found"] = "find",
            ["felt"] = "feel",
            ["left"] = "leave",
            ["kept"] = "keep",
            ["held"] = "hold",
            ["heard"] = "hear",
            ["met"] = "meet",
            ["paid"] = "pay",
            ["said"] = "say",
            ["sent"] = "send",
            ["sold"] = "sell",
            ["told"] = "tell",
            ["stood"] = "stand",
            ["understood"] = "understand",
            ["won"] = "win",
            ["lost"] = "lose",
            ["built"] = "build",
            ["broke"] = "break",
            ["broken"] = "break",
            ["chose"] = "choose",
            ["chosen"] = "choose",
            ["drew"] = "draw",
            ["drawn"] = "draw",
            ["drank"] = "drink",
            ["drunk"] = "drink",
            ["ate"] = "eat",
            ["eaten"] = "eat",
            ["fell"] = "fall",
            ["fallen"] = "fall",
            ["flew"] = "fly",
            ["flown"] = "fly",
            ["forgot"] = "forget",
            ["forgotten"] = "forget",
            ["grew"] = "grow",
            ["grown"] = "grow",
            ["rode"] = "ride",
            ["ridden"] = "ride",
            ["rose"] = "rise",
            ["risen"] = "rise",
            ["sang"] = "sing",
            ["sung"] = "sing",
            ["swam"] = "swim",
            ["swum"] = "swim",
            ["threw"] = "throw",
            ["thrown"] = "throw",
            ["wore"] = "wear",
            ["worn"] = "wear",
            ["children"] = "child",
            ["men"] = "man",
            ["women"] = "woman",
            ["people"] = "person",
            ["mice"] = "mouse",
            ["geese"] = "goose",
            ["teeth"] = "tooth",
            ["feet"] = "foot",
            ["oxen"] = "ox",
            ["indices"] = "index",
            ["matrices"] = "matrix",
            ["vertices"] = "vertex",
            ["analyses"] = "analysis",
            ["diagnoses"] = "diagnosis",
            ["theses"] = "thesis",
            ["crises"] = "crisis",
            ["criteria"] = "criterion",
            ["phenomena"] = "phenomenon",
            ["media"] = "medium",
            ["better"] = "good",
            ["best"] = "good",
            ["worse"] = "bad",
            ["worst"] = "bad",
            ["farther"] = "far",
            ["farthest"] = "far",
            ["further"] = "far",
            ["furthest"] = "far"
        };

    private static readonly string[] NounSuffixes =
    {
        "tion", "sion", "ment", "ness", "ity", "ism", "ist", "ship",
        "ance", "ence", "hood", "dom", "age", "ery", "ure", "cy"
    };

    private static readonly string[] AdjectiveSuffixes =
    {
        "ous", "ful", "less", "able", "ible", "ive", "al", "ic",
        "ical", "ary", "ory", "ish", "ent", "ant"
    };

    public static bool TryGetProfile(string surface, out LexiconProfile profile)
    {
        var word = NormalizeApostrophes(surface);
        var builder = new ProfileBuilder();
        builder.Add(word, LexiconTag.None, penalty: 0);

        if (IrregularLemmas.TryGetValue(word, out var irregularLemma))
        {
            builder.Add(irregularLemma, LexiconTag.None, penalty: 1);
        }

        AddPossessiveAndCompoundCandidates(word, ref builder);
        AddInflectionCandidates(word, ref builder);

        if (builder.Tags == LexiconTag.None)
        {
            AddUnknownShape(word, ref builder);
        }

        profile = builder.Build();
        return profile.Tags != LexiconTag.None;
    }

    public static bool HasTag(string surface, LexiconTag tag) =>
        TryGetProfile(surface, out var profile) && (profile.Tags & tag) != 0;

    public static bool IsInflectionOf(string surface, ReadOnlySpan<char> lemma)
    {
        var word = NormalizeApostrophes(surface);
        if (word.AsSpan().Equals(lemma, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (IrregularLemmas.TryGetValue(word, out var irregular)
            && irregular.AsSpan().Equals(lemma, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var span = word.AsSpan();
        if (span.Length > 3 && span.EndsWith("s", StringComparison.OrdinalIgnoreCase)
            && span[..^1].Equals(lemma, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (span.Length > 4 && span.EndsWith("ies", StringComparison.OrdinalIgnoreCase)
            && lemma.EndsWith("y", StringComparison.OrdinalIgnoreCase)
            && span[..^3].Equals(lemma[..^1], StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (span.Length > 4 && span.EndsWith("ed", StringComparison.OrdinalIgnoreCase))
        {
            var stem = span[..^2];
            if (stem.Equals(lemma, StringComparison.OrdinalIgnoreCase)
                || span[..^1].Equals(lemma, StringComparison.OrdinalIgnoreCase)
                || EndsWithDoubledConsonant(stem.ToString())
                   && stem[..^1].Equals(lemma, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        if (span.Length > 5 && span.EndsWith("ing", StringComparison.OrdinalIgnoreCase))
        {
            var stem = span[..^3];
            if (stem.Equals(lemma, StringComparison.OrdinalIgnoreCase)
                || lemma.EndsWith("e", StringComparison.OrdinalIgnoreCase)
                   && stem.Equals(lemma[..^1], StringComparison.OrdinalIgnoreCase)
                || EndsWithDoubledConsonant(stem.ToString())
                   && stem[..^1].Equals(lemma, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public static GrammarCategory? DominantCategory(string surface)
    {
        if (!TryGetProfile(surface, out var profile))
        {
            return null;
        }

        var bestTag = LexiconTag.None;
        var bestWeight = -1;
        foreach (var tag in OrderedTags)
        {
            var weight = profile.GetWeight(tag);
            if (weight > bestWeight)
            {
                bestTag = tag;
                bestWeight = weight;
            }
        }

        return bestTag switch
        {
            LexiconTag.Noun => GrammarCategory.ObjectNoun,
            LexiconTag.Verb => GrammarCategory.Verb,
            LexiconTag.Adjective => GrammarCategory.Adjective,
            LexiconTag.Adverb => GrammarCategory.Adverb,
            LexiconTag.Pronoun => GrammarCategory.Pronoun,
            LexiconTag.Preposition => GrammarCategory.Preposition,
            LexiconTag.Conjunction => GrammarCategory.Conjunction,
            LexiconTag.Determiner => GrammarCategory.Determiner,
            LexiconTag.Particle => GrammarCategory.Particle,
            LexiconTag.Quantifier => GrammarCategory.Quantifier,
            _ => null
        };
    }

    private static ReadOnlySpan<LexiconTag> OrderedTags =>
    [
        LexiconTag.Noun,
        LexiconTag.Verb,
        LexiconTag.Adjective,
        LexiconTag.Adverb,
        LexiconTag.Pronoun,
        LexiconTag.Preposition,
        LexiconTag.Conjunction,
        LexiconTag.Determiner,
        LexiconTag.Particle,
        LexiconTag.Quantifier
    ];

    private static void AddPossessiveAndCompoundCandidates(
        string word,
        ref ProfileBuilder builder)
    {
        if (word.Length > 2 && word.EndsWith("'s", StringComparison.Ordinal))
        {
            builder.Add(word[..^2], LexiconTag.Noun, penalty: 1);
        }
        else if (word.Length > 2 && word.EndsWith("s'", StringComparison.Ordinal))
        {
            builder.Add(word[..^1], LexiconTag.Noun, penalty: 1);
        }

        var hyphen = word.LastIndexOf('-');
        if (hyphen >= 0 && hyphen + 1 < word.Length)
        {
            builder.Add(word[(hyphen + 1)..], LexiconTag.None, penalty: 2);
        }
    }

    private static void AddInflectionCandidates(string word, ref ProfileBuilder builder)
    {
        if (word.Length > 4 && word.EndsWith("ies", StringComparison.Ordinal))
        {
            builder.Add(word[..^3] + "y", LexiconTag.Noun | LexiconTag.Verb, penalty: 1);
        }

        if (word.Length > 4 && word.EndsWith("ves", StringComparison.Ordinal))
        {
            builder.Add(word[..^3] + "f", LexiconTag.Noun, penalty: 2);
            builder.Add(word[..^3] + "fe", LexiconTag.Noun, penalty: 2);
        }

        if (word.Length > 3 && word.EndsWith("es", StringComparison.Ordinal))
        {
            builder.Add(word[..^2], LexiconTag.Noun | LexiconTag.Verb, penalty: 2);
            builder.Add(word[..^1], LexiconTag.Noun | LexiconTag.Verb, penalty: 1);
        }
        else if (word.Length > 3
                 && word.EndsWith('s')
                 && !word.EndsWith("ss", StringComparison.Ordinal)
                 && !word.EndsWith("us", StringComparison.Ordinal)
                 && !word.EndsWith("is", StringComparison.Ordinal))
        {
            builder.Add(word[..^1], LexiconTag.Noun | LexiconTag.Verb, penalty: 1);
        }

        if (word.Length > 4 && word.EndsWith("ied", StringComparison.Ordinal))
        {
            builder.Add(word[..^3] + "y", LexiconTag.Verb | LexiconTag.Adjective, penalty: 1);
        }

        if (word.Length > 4 && word.EndsWith("ed", StringComparison.Ordinal))
        {
            var stem = word[..^2];
            builder.Add(stem, LexiconTag.Verb | LexiconTag.Adjective, penalty: 1);
            builder.Add(word[..^1], LexiconTag.Verb | LexiconTag.Adjective, penalty: 1);
            if (EndsWithDoubledConsonant(stem))
            {
                builder.Add(stem[..^1], LexiconTag.Verb, penalty: 1);
            }
        }

        if (word.Length > 5 && word.EndsWith("ing", StringComparison.Ordinal))
        {
            var stem = word[..^3];
            builder.Add(stem, LexiconTag.Verb | LexiconTag.Noun | LexiconTag.Adjective, penalty: 1);
            builder.Add(stem + "e", LexiconTag.Verb, penalty: 2);
            if (EndsWithDoubledConsonant(stem))
            {
                builder.Add(stem[..^1], LexiconTag.Verb, penalty: 1);
            }
            if (word.EndsWith("ying", StringComparison.Ordinal) && stem.Length > 1)
            {
                builder.Add(stem[..^1] + "ie", LexiconTag.Verb, penalty: 1);
            }
        }

        if (word.Length > 4 && word.EndsWith("er", StringComparison.Ordinal))
        {
            AddComparisonCandidates(word[..^2], ref builder);
        }
        if (word.Length > 5 && word.EndsWith("est", StringComparison.Ordinal))
        {
            AddComparisonCandidates(word[..^3], ref builder);
        }
    }

    private static void AddComparisonCandidates(string stem, ref ProfileBuilder builder)
    {
        builder.Add(stem, LexiconTag.Adjective | LexiconTag.Adverb, penalty: 1);
        builder.Add(stem + "e", LexiconTag.Adjective, penalty: 2);
        if (stem.EndsWith('i'))
        {
            builder.Add(stem[..^1] + "y", LexiconTag.Adjective, penalty: 1);
        }
        if (EndsWithDoubledConsonant(stem))
        {
            builder.Add(stem[..^1], LexiconTag.Adjective, penalty: 1);
        }
    }

    private static void AddUnknownShape(string word, ref ProfileBuilder builder)
    {
        if (word.Length > 4 && word.EndsWith("ly", StringComparison.Ordinal))
        {
            builder.AddSynthetic(LexiconTag.Adverb, 10);
        }
        if (word.Length > 4 && word.EndsWith("ing", StringComparison.Ordinal))
        {
            builder.AddSynthetic(LexiconTag.Verb, 10);
            builder.AddSynthetic(LexiconTag.Noun | LexiconTag.Adjective, 5);
        }
        else if (word.Length > 3
                 && (word.EndsWith("ed", StringComparison.Ordinal)
                     || word.EndsWith("en", StringComparison.Ordinal)))
        {
            builder.AddSynthetic(LexiconTag.Verb, 10);
            builder.AddSynthetic(LexiconTag.Adjective, 5);
        }
        else if (word.EndsWith("ize", StringComparison.Ordinal)
                 || word.EndsWith("ise", StringComparison.Ordinal)
                 || word.EndsWith("ify", StringComparison.Ordinal))
        {
            builder.AddSynthetic(LexiconTag.Verb, 11);
        }

        // An unseen final -s is normally either a plural noun or a third-person
        // singular verb. Keep the noun prior stronger and let sentence context
        // promote the verb reading when there is an overt subject.
        if (word.Length > 3
            && word.EndsWith('s')
            && !word.EndsWith("ss", StringComparison.Ordinal)
            && !word.EndsWith("us", StringComparison.Ordinal)
            && !word.EndsWith("is", StringComparison.Ordinal))
        {
            builder.AddSynthetic(LexiconTag.Noun, 10);
            builder.AddSynthetic(LexiconTag.Verb, 6);
        }

        if (HasSuffix(word, NounSuffixes))
        {
            builder.AddSynthetic(LexiconTag.Noun, 10);
        }
        if (HasSuffix(word, AdjectiveSuffixes))
        {
            builder.AddSynthetic(LexiconTag.Adjective, 10);
        }
    }

    private static bool HasSuffix(string word, IEnumerable<string> suffixes)
    {
        foreach (var suffix in suffixes)
        {
            if (word.Length > suffix.Length + 1
                && word.EndsWith(suffix, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static bool EndsWithDoubledConsonant(string value)
    {
        if (value.Length < 2 || value[^1] != value[^2])
        {
            return false;
        }
        return value[^1] is not ('a' or 'e' or 'i' or 'o' or 'u' or 'y');
    }

    private static string NormalizeApostrophes(string value) =>
        value
            .Replace('’', (char)0x27)
            .Replace('ʼ', (char)0x27)
            .Replace("â€™", "'", StringComparison.Ordinal)
            .ToLowerInvariant();

    private ref struct ProfileBuilder
    {
        private ulong _weights;

        public LexiconTag Tags { get; private set; }

        public void Add(string lemma, LexiconTag allowed, int penalty)
        {
            if (!GrammarLexicon.TryGetProfile(lemma, out var profile))
            {
                return;
            }
            var accepted = allowed == LexiconTag.None ? profile.Tags : profile.Tags & allowed;
            foreach (var tag in OrderedTags)
            {
                if ((accepted & tag) == 0)
                {
                    continue;
                }
                AddSynthetic(tag, Math.Max(1, profile.GetWeight(tag) - penalty));
            }
        }

        public void AddSynthetic(LexiconTag tags, int weight)
        {
            foreach (var tag in OrderedTags)
            {
                if ((tags & tag) == 0)
                {
                    continue;
                }
                Tags |= tag;
                var shift = BitOperations.TrailingZeroCount((uint)tag) * 4;
                var current = (int)((_weights >> shift) & 0xFUL);
                if (weight > current)
                {
                    _weights = (_weights & ~(0xFUL << shift))
                               | ((ulong)Math.Min(15, weight) << shift);
                }
            }
        }

        public readonly LexiconProfile Build() => new(Tags, _weights);
    }
}
