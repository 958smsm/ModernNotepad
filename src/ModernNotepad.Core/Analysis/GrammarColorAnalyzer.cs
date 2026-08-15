namespace ModernNotepad.Core.Analysis;

/// <summary>
/// Deterministic, offline English grammar classifier. The analyzer deliberately
/// keeps the public category contract small, but uses sentence/clause context to
/// disambiguate polysemous words and noun roles instead of trusting a single
/// dictionary label or word suffix.
/// </summary>
public sealed class GrammarColorAnalyzer
{
    private static readonly HashSet<string> PersonalPronouns = new(StringComparer.OrdinalIgnoreCase)
    {
        "i", "me", "myself", "you", "yourself", "yourselves", "he", "him", "himself",
        "she", "her", "herself", "it", "itself", "we", "us", "ourselves", "they", "them", "themselves",
        "one", "oneself", "someone", "somebody", "something", "anyone", "anybody", "anything",
        "everyone", "everybody", "everything", "nobody", "nothing", "none", "whoever", "whomever"
    };

    private static readonly HashSet<string> PossessivePronouns = new(StringComparer.OrdinalIgnoreCase)
    {
        "mine", "yours", "his", "hers", "ours", "theirs", "whose"
    };

    private static readonly HashSet<string> PossessiveDeterminers = new(StringComparer.OrdinalIgnoreCase)
    {
        "my", "your", "his", "her", "its", "our", "their", "whose"
    };

    private static readonly HashSet<string> Determiners = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "this", "these", "those", "some", "any", "every", "each", "all", "both",
        "neither", "either", "no", "much", "many", "most", "several", "enough", "few", "fewer", "fewest",
        "little", "less", "least", "more", "another", "other", "such", "what", "whatever", "which", "whichever"
    };

    // Keep the broad determiner set above for noun-phrase detection, but expose
    // semantically quantifying determiners separately in the public taxonomy.
    private static readonly HashSet<string> QuantifierWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "some", "any", "every", "each", "all", "both", "neither", "either", "no", "much", "many",
        "most", "several", "enough", "few", "fewer", "fewest", "little", "less", "least", "more"
    };

    private static readonly HashSet<string> NumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen",
        "eighteen", "nineteen", "twenty", "thirty", "forty", "fifty", "sixty", "seventy",
        "eighty", "ninety", "hundred", "thousand", "million", "billion", "trillion",
        "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth",
        "ninth", "tenth", "eleventh", "twelfth", "thirteenth", "fourteenth", "fifteenth",
        "sixteenth", "seventeenth", "eighteenth", "nineteenth", "twentieth",
        "dozen", "score", "half", "quarter"
    };

    private static readonly HashSet<string> ProperNounTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "mr", "mrs", "ms", "miss", "dr", "prof", "professor", "president", "governor",
        "senator", "representative", "judge", "justice", "captain", "general", "saint", "st"
    };

    private static readonly HashSet<string> ComplementTakingNouns = new(StringComparer.OrdinalIgnoreCase)
    {
        "fact", "idea", "belief", "claim", "possibility", "evidence", "assumption", "conclusion", "hope",
        "news", "report", "statement", "suggestion", "proof", "indication", "likelihood", "chance", "notion",
        "argument", "proposal", "rumor", "rumour", "view", "theory"
    };

    private static readonly HashSet<string> GerundTakingVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "admit", "admits", "admitted", "avoid", "avoids", "avoided", "consider", "considers", "considered",
        "deny", "denies", "denied", "enjoy", "enjoys", "enjoyed", "finish", "finishes", "finished",
        "imagine", "imagines", "imagined", "mind", "minds", "minded", "postpone", "postpones", "postponed",
        "practice", "practices", "practiced", "practise", "practises", "practised", "quit", "quits", "risk",
        "risks", "risked", "suggest", "suggests", "suggested", "recommend", "recommends", "recommended"
    };

    private static readonly HashSet<string> StronglyTransitiveVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "build", "builds", "built", "buy", "buys", "bought", "call", "calls", "called", "choose", "chooses",
        "chose", "chosen", "create", "creates", "created", "detect", "detects", "detected", "find", "finds",
        "found", "identify", "identifies", "identified", "make", "makes", "made", "produce", "produces", "produced",
        "read", "reads", "review", "reviews", "reviewed", "select", "selects", "selected", "take", "takes", "took",
        "taken", "test", "tests", "tested", "use", "uses", "used", "write", "writes", "wrote", "written"
    };

    private static readonly HashSet<string> PhrasalVerbParticlePairs = new(StringComparer.OrdinalIgnoreCase)
    {
        "back up", "break down", "bring in", "bring up", "call off", "carry on", "carry out", "check out",
        "come across", "come back", "come in", "come out", "cut off", "end up", "figure out", "fill out",
        "find out", "give up", "go on", "go out", "go through", "grow up", "hand over", "hold on", "keep on",
        "leave out", "log in", "look up", "make out", "make up", "move on", "opt out", "pick up", "point out",
        "put off", "put on", "put out", "rule out", "set up", "shut down", "sign in", "take off", "take over",
        "take up", "turn down", "turn off", "turn on", "turn out", "turn up", "work out", "write down"
    };

    private static readonly HashSet<string> ProductiveParticleWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "away", "back", "down", "in", "off", "on", "out", "over", "through", "up"
    };

    private static readonly HashSet<string> AdverbialPrepositionWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "above", "across", "after", "around", "before", "behind", "below",
        "beneath", "besides", "beyond", "down", "inside", "near", "off", "on", "out",
        "outside", "over", "past", "round", "since", "through", "underneath", "up"
    };

    private static readonly HashSet<string> IrregularPastParticiples = new(StringComparer.OrdinalIgnoreCase)
    {
        "been", "begun", "broken", "brought", "built", "bought", "caught", "chosen", "come", "done", "drawn",
        "driven", "eaten", "felt", "found", "given", "gone", "grown", "held", "kept", "known", "left", "lost",
        "made", "paid", "read", "run", "said", "seen", "sent", "shown", "sold", "spoken", "spent", "taken",
        "taught", "told", "thought", "understood", "won", "written"
    };

    private static readonly HashSet<string> StativeParticipleAdjectives = new(StringComparer.OrdinalIgnoreCase)
    {
        "broken", "closed", "concerned", "confused", "damaged", "done", "finished", "interested", "known",
        "lost", "married", "prepared", "related", "satisfied", "seated", "tired", "worried"
    };

    private static readonly HashSet<string> AmbiguousBareInfinitiveVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "answer", "cook", "dance", "drive", "drink", "exercise", "fish", "garden", "phone", "plan", "record",
        "shop", "sleep", "smoke", "study", "travel", "visit", "walk"
    };

    // The tokenizer deliberately keeps common contractions as one surface token.
    // Preserve the dominant syntactic role of that token instead of falling back
    // to a noun classification simply because the apostrophe form is absent from
    // the generated lexicon. Apostrophes are normalized in the helper methods so
    // typographic and ASCII forms behave identically.
    private static readonly HashSet<string> ContractedPronounAuxiliaries = new(StringComparer.OrdinalIgnoreCase)
    {
        "i'm", "i've", "i'll", "i'd",
        "you're", "you've", "you'll", "you'd",
        "he's", "he'll", "he'd", "she's", "she'll", "she'd", "it's", "it'll", "it'd",
        "we're", "we've", "we'll", "we'd", "they're", "they've", "they'll", "they'd"
    };

    private static readonly HashSet<string> ContractedAuxiliaryVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "isn't", "aren't", "wasn't", "weren't", "hasn't", "haven't", "hadn't",
        "don't", "doesn't", "didn't", "can't", "couldn't", "won't", "wouldn't",
        "shan't", "shouldn't", "mustn't", "mightn't", "needn't", "ain't"
    };

    private static readonly HashSet<string> ContractedBeAuxiliaries = new(StringComparer.OrdinalIgnoreCase)
    {
        "i'm", "you're", "he's", "she's", "it's", "we're", "they're",
        "isn't", "aren't", "wasn't", "weren't"
    };

    private static readonly HashSet<string> InterrogativeWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "who", "whom", "what", "which", "whose", "where", "when", "why", "how"
    };

    private static readonly HashSet<string> CoordinatingConjunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "but", "or", "nor", "for", "yet", "so"
    };

    private static readonly HashSet<string> SubordinatingConjunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "although", "though", "because", "unless", "since", "while", "whereas", "if", "when", "whenever",
        "where", "wherever", "whether", "before", "after", "once", "until", "than", "as", "lest", "provided"
    };

    private static readonly HashSet<string> Prepositions = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "above", "across", "after", "against", "along", "amid", "among", "around", "at", "before",
        "behind", "below", "beneath", "beside", "besides", "between", "beyond", "by", "concerning", "despite",
        "down", "during", "except", "for", "from", "in", "inside", "into", "like", "near", "of", "off", "on",
        "onto", "opposite", "out", "outside", "over", "past", "per", "regarding", "round", "since", "through",
        "throughout", "toward", "towards", "under", "underneath", "unlike", "until", "up", "upon", "via", "with",
        "within", "without"
    };

    private static readonly HashSet<string> ModalVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "can", "could", "may", "might", "must", "shall", "should", "will", "would"
    };

    private static readonly HashSet<string> AuxiliaryVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "am", "is", "are", "was", "were", "be", "being", "been",
        "have", "has", "had", "having", "do", "does", "did", "doing"
    };

    private static readonly HashSet<string> CopularVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "am", "is", "are", "was", "were", "be", "being", "been",
        "become", "becomes", "became", "seem", "seems", "seemed", "appear", "appears", "appeared",
        "remain", "remains", "remained", "feel", "feels", "felt", "look", "looks", "looked", "sound", "sounds",
        "sounded", "smell", "smells", "smelled", "taste", "tastes", "tasted", "grow", "grows", "grew", "grown"
    };

    private static readonly HashSet<string> VerbComplementLicensers = new(StringComparer.OrdinalIgnoreCase)
    {
        "start", "starts", "started", "begin", "begins", "began", "continue", "continues", "continued",
        "keep", "keeps", "kept", "stop", "stops", "stopped", "try", "tries", "tried", "finish", "finishes",
        "finished", "avoid", "avoids", "avoided", "consider", "considers", "considered", "enjoy", "enjoys", "enjoyed"
    };

    private static readonly string[] ObjectControlVerbLemmas =
    {
        "allow", "ask", "cause", "encourage", "enable", "expect", "force", "invite",
        "order", "permit", "persuade", "require", "teach", "tell", "want"
    };

    private static readonly string[] FiniteClauseComplementVerbLemmas =
    {
        "believe", "claim", "discover", "expect", "feel", "find", "hear", "know",
        "mean", "notice", "prove", "realize", "remember", "report", "say", "see",
        "show", "suppose", "think", "understand"
    };

    private static readonly string[] PerceptionVerbLemmas =
    {
        "catch", "feel", "find", "hear", "notice", "observe", "see", "watch"
    };

    private static readonly HashSet<string> CommonVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "go", "goes", "went", "gone", "make", "makes", "made", "say", "says", "said", "see", "sees", "saw", "seen",
        "know", "knows", "knew", "known", "think", "thinks", "thought", "take", "takes", "took", "taken", "come", "comes",
        "came", "want", "wants", "wanted", "use", "uses", "used", "find", "finds", "found", "give", "gives", "gave", "given",
        "tell", "tells", "told", "work", "works", "worked", "call", "calls", "called", "try", "tries", "tried", "ask", "asks",
        "asked", "need", "needs", "needed", "feel", "feels", "felt", "become", "becomes", "became", "leave", "leaves", "left",
        "put", "puts", "keep", "keeps", "kept", "let", "lets", "begin", "begins", "began", "begun", "start", "starts", "started",
        "show", "shows", "showed", "shown", "hear", "hears", "heard", "play", "plays", "played", "run", "runs", "ran", "move",
        "moves", "moved", "live", "lives", "lived", "believe", "believes", "believed", "bring", "brings", "brought", "write", "writes",
        "wrote", "written", "provide", "provides", "provided", "sit", "sits", "sat", "stand", "stands", "stood", "lose", "loses", "lost",
        "pay", "pays", "paid", "meet", "meets", "met", "include", "includes", "included", "continue", "continues", "continued", "set",
        "sets", "learn", "learns", "learned", "change", "changes", "changed", "lead", "leads", "led", "understand", "understands",
        "understood", "watch", "watches", "watched", "follow", "follows", "followed", "stop", "stops", "stopped", "create", "creates",
        "created", "speak", "speaks", "spoke", "spoken", "read", "reads", "allow", "allows", "allowed", "add", "adds", "added",
        "separate", "separates", "separated", "update", "updates", "updated", "delete", "deletes", "deleted", "remove", "removes", "removed",
        "save", "saves", "saved", "open", "opens", "opened", "close", "closes", "closed", "format", "formats", "formatted", "check", "checks",
        "checked", "select", "selects", "selected", "copy", "copies", "copied", "paste", "pastes", "pasted", "cut", "cuts", "replace",
        "replaces", "replaced", "insert", "inserts", "inserted", "view", "views", "viewed", "help", "helps", "helped", "build", "builds",
        "built", "compile", "compiles", "compiled", "execute", "executes", "executed", "test", "tests", "tested", "debug", "debugs", "debugged",
        "deploy", "deploys", "deployed", "generate", "generates", "generated", "analyze", "analyzes", "analyzed", "parse", "parses", "parsed",
        "process", "processes", "processed", "evaluate", "evaluates", "evaluated", "configure", "configures", "configured", "ingest", "ingests",
        "ingested", "handle", "handles", "handled", "share", "shares", "shared", "maintain", "maintains", "maintained", "store", "stores", "stored",
        "deliver", "delivers", "delivered", "produce", "produces", "produced", "convert", "converts", "converted", "measure", "measures", "measured",
        "mean", "means", "meant", "miss", "misses", "missed", "identify", "identifies", "identified", "detect", "detects", "detected", "classify",
        "classifies", "classified", "require", "requires", "required", "support", "supports", "supported", "pass", "passes", "passed", "review",
        "reviews", "reviewed", "improve", "improves", "improved", "fail", "fails", "failed"
    };

    private static readonly HashSet<string> CommonNouns = new(StringComparer.OrdinalIgnoreCase)
    {
        "processing", "tracking", "understanding", "monitoring", "versioning", "scaling", "building", "testing", "meeting", "training",
        "planning", "setting", "meaning", "marketing", "engineering", "coding", "programming", "learning", "operating", "beheading",
        "drawing", "painting", "feeling",
        "finding", "recording", "showing", "warning", "opening", "closing", "configuration", "isolation", "detection", "logic", "recall",
        "precision", "accuracy", "model", "models", "case", "cases", "instance", "instances", "condition", "conditions", "object", "objects",
        "thanks"
    };

    private static readonly HashSet<string> NominalSetModifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "character", "data", "development", "feature", "instruction", "record",
        "result", "test", "tool", "training", "validation", "value"
    };

    private static readonly HashSet<string> CommonAdjectives = new(StringComparer.OrdinalIgnoreCase)
    {
        "good", "new", "first", "last", "long", "great", "little", "own", "other", "old", "right", "big", "high", "different", "small",
        "large", "next", "early", "young", "important", "few", "public", "bad", "same", "able", "clear", "simple", "strong", "possible",
        "available", "local", "recent", "modern", "fast", "lightweight", "current", "previous", "true", "false", "valid", "invalid", "empty",
        "full", "static", "dynamic", "private", "specific", "basic", "active", "inactive", "visible", "hidden", "raw", "scalable", "central",
        "semantic", "actual", "positive", "real", "useful", "costly", "accurate", "existing"
    };

    private static readonly HashSet<string> CommonAdverbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "very", "quite", "rather", "often", "always", "never", "soon", "well", "too", "then", "now", "instead", "not", "also", "only",
        "especially", "correctly", "already", "still", "just", "almost", "nearly", "perhaps", "maybe", "however", "therefore", "thus"
    };

    private static readonly HashSet<string> AdjectiveLyExceptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "friendly", "lovely", "lively", "lonely", "likely", "unlikely", "elderly", "costly", "orderly", "daily", "weekly", "monthly", "yearly"
    };

    private static readonly string[] NounSuffixes =
    {
        "tion", "sion", "ment", "ness", "ity", "ism", "ist", "ship", "ance", "ence", "hood", "dom", "age", "ery", "ry"
    };

    private static readonly string[] AdjectiveSuffixes =
    {
        "ous", "ful", "less", "able", "ible", "ive", "al", "ic", "ical", "ary", "ory", "ish", "ent", "ant"
    };

    public GrammarAnalysis Analyze(
        string text,
        IReadOnlyList<TextToken>? tokens = null,
        IReadOnlyList<TextSpan>? sentences = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        tokens ??= TextTokenizer.Tokenize(text, cancellationToken);
        sentences ??= TextSegmentation.GetSentences(text, cancellationToken);

        var spans = new List<ColoredSpan>(tokens.Count);
        var counts = Enum.GetValues<GrammarCategory>().ToDictionary(category => category, _ => 0);
        var processed = new bool[tokens.Count];
        var sentenceRanges = SpanTokenIndex.Align(tokens, sentences, cancellationToken);

        foreach (var range in sentenceRanges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (range.Count == 0)
            {
                continue;
            }

            AnalyzeSentence(text, tokens, range, spans, counts, processed, cancellationToken);
        }

        // Defensive fallback for tokens outside a supplied/custom sentence span list.
        for (var index = 0; index < tokens.Count; index++)
        {
            if ((index & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (processed[index])
            {
                continue;
            }

            var category = ClassifyStandalone(tokens[index]);
            AddResult(tokens[index], category, spans, counts);
        }

        return new GrammarAnalysis(spans, counts);
    }

    private static void AnalyzeSentence(
        string text,
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        ICollection<ColoredSpan> spans,
        IDictionary<GrammarCategory, int> counts,
        bool[] processed,
        CancellationToken cancellationToken)
    {
        var kinds = new LexicalKind[range.Count];
        var isQuestion = IsQuestionSentence(text, range.Span);

        for (var local = 0; local < range.Count; local++)
        {
            if ((local & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            kinds[local] = ClassifyLexical(text, tokens, range, local, isQuestion);
        }

        var clauseIds = BuildClauseIds(text, tokens, range, kinds);
        var subjectNouns = new bool[range.Count];
        MarkSubjectNouns(text, tokens, range, kinds, clauseIds, subjectNouns);

        for (var local = 0; local < range.Count; local++)
        {
            var global = range.Start + local;
            processed[global] = true;
            var category = ToGrammarCategory(kinds[local], subjectNouns[local]);
            AddResult(tokens[global], category, spans, counts);
        }
    }

    private static LexicalKind ClassifyLexical(
        string text,
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local,
        bool isQuestion)
    {
        var token = tokens[range.Start + local];
        var word = token.Normalized;

        if (word is "e.g." or "i.e.")
        {
            return LexicalKind.Adverb;
        }

        if (IsNumericToken(token.Text))
        {
            return LexicalKind.Quantifier;
        }

        if (word == "one"
            && (IsAnaphoricOne(tokens, range, local)
                || IsImpersonalOne(tokens, range, local)))
        {
            return LexicalKind.Pronoun;
        }

        if (NumberWords.Contains(word))
        {
            return LexicalKind.Quantifier;
        }

        if (TryClassifyContraction(word, isQuestion, out var contractionKind))
        {
            return contractionKind;
        }

        if (IsPossessiveSurface(word))
        {
            return LexicalKind.Noun;
        }

        if (IsContextualProperNoun(token, tokens, range, local))
        {
            return LexicalKind.Noun;
        }

        if (word == "to")
        {
            return LooksLikeInfinitive(tokens, range, local) ? LexicalKind.Particle : LexicalKind.Preposition;
        }

        if (word == "that")
        {
            return ClassifyThat(tokens, range, local, isQuestion);
        }

        if (InterrogativeWords.Contains(word))
        {
            if (IsRelativeWh(tokens, range, local))
            {
                if (word == "whose" && HasFollowingNominal(tokens, range, local))
                {
                    return LexicalKind.Determiner;
                }

                return word is "who" or "whom" or "whose" or "which" or "what"
                    ? LexicalKind.Pronoun
                    : LexicalKind.Conjunction;
            }

            // A question mark does not make every wh-form interrogative. In
            // "When the rain stops, will we leave?", when introduces an adverbial
            // clause and is a subordinating conjunction.
            if (IsAdverbialSubordinatorUse(tokens, range, local))
            {
                return LexicalKind.Conjunction;
            }

            if (isQuestion
                || IsEmbeddedQuestion(tokens, range, local)
                || IsWhQuestionSyntax(tokens, range, local))
            {
                return LexicalKind.Interrogative;
            }

            if (word is "who" or "whom" or "whose" or "what" or "which")
            {
                return LexicalKind.Pronoun;
            }

            return LexicalKind.Conjunction;
        }

        if (PossessiveDeterminers.Contains(word))
        {
            return HasFollowingNominal(tokens, range, local) ? LexicalKind.Determiner : LexicalKind.Pronoun;
        }

        if (Determiners.Contains(word))
        {
            // Demonstratives and wh-determiners can stand alone as pronouns.
            if (word is "this" or "these" or "those" or "what" or "which" or "whatever" or "whichever"
                && !HasFollowingNominal(tokens, range, local))
            {
                return LexicalKind.Pronoun;
            }

            return QuantifierWords.Contains(word) ? LexicalKind.Quantifier : LexicalKind.Determiner;
        }

        if (PersonalPronouns.Contains(word) || PossessivePronouns.Contains(word))
        {
            return LexicalKind.Pronoun;
        }

        if (IsLexicalizedCompoundHead(tokens, range, local))
        {
            return LexicalKind.Noun;
        }

        if (IsDeterminerAnchoredAttributive(tokens, range, local))
        {
            return LexicalKind.Adjective;
        }

        if (IsAuxiliaryChainAdverb(tokens, range, local))
        {
            return LexicalKind.Adverb;
        }

        if (IsCoordinatedVerb(tokens, range, local))
        {
            return LexicalKind.Verb;
        }

        if (IsInfinitiveTarget(tokens, range, local))
        {
            return LexicalKind.Verb;
        }

        if (IsSyntacticallyForcedVerb(tokens, range, local))
        {
            return LexicalKind.Verb;
        }

        if (CoordinatingConjunctions.Contains(word))
        {
            if (word == "for" && !IsCoordinatingFor(text, tokens, range, local))
            {
                return LexicalKind.Preposition;
            }
            if (word == "so" && IsAdverbialSo(text, tokens, range, local))
            {
                return LexicalKind.Adverb;
            }
            if (word == "yet"
                && (local + 1 >= range.Count
                    || CoordinatingConjunctions.Contains(
                        tokens[range.Start + local + 1].Normalized)
                    || !HasFollowingFiniteVerb(tokens, range, local, 5)))
            {
                return LexicalKind.Adverb;
            }
            return LexicalKind.Conjunction;
        }

        if (SubordinatingConjunctions.Contains(word) && !IsPrepositionalUse(text, tokens, range, local))
        {
            return LexicalKind.Conjunction;
        }

        if (IsPhrasalVerbParticle(tokens, range, local))
        {
            return LexicalKind.Particle;
        }

        if (Prepositions.Contains(word))
        {
            return IsAdverbialPrepositionUse(tokens, range, local)
                ? LexicalKind.Adverb
                : LexicalKind.Preposition;
        }

        if (IsNominalCompoundModifier(tokens, range, local))
        {
            return LexicalKind.Noun;
        }

        // Resolve the noun following an -ing/-ed form before treating the form
        // as a clause-level gerund: "running water" is attributive, whereas
        // "running is healthy" is nominal.
        if (IsAttributiveParticiple(tokens, range, local))
        {
            return LexicalKind.Adjective;
        }

        if (IsGerundNominal(tokens, range, local))
        {
            return LexicalKind.Noun;
        }

        if (IsPassiveParticipleContext(tokens, range, local))
        {
            return LexicalKind.Verb;
        }

        if (IsProgressiveParticipleContext(tokens, range, local))
        {
            return LexicalKind.Verb;
        }

        if (IsPredicativeStativeParticiple(tokens, range, local))
        {
            return LexicalKind.Adjective;
        }

        if (word.EndsWith("ing", StringComparison.OrdinalIgnoreCase)
            && local + 1 < range.Count
            && tokens[range.Start + local + 1].Normalized == "to")
        {
            return LexicalKind.Verb;
        }

        if (CommonAdverbs.Contains(word) || IsAdverbByLexiconOrMorphology(word))
        {
            return LexicalKind.Adverb;
        }

        if (IsContextualAdjective(tokens, range, local)
            || CommonAdjectives.Contains(word)
            || IsAdjectiveByLexiconOrMorphology(word))
        {
            return LexicalKind.Adjective;
        }

        if (IsStrongNominalContext(tokens, range, local))
        {
            return LexicalKind.Noun;
        }

        if (CommonNouns.Contains(word))
        {
            return LexicalKind.Noun;
        }

        if (LooksLikeVerb(tokens, range, local))
        {
            return LexicalKind.Verb;
        }

        if (word.EndsWith("ing", StringComparison.OrdinalIgnoreCase)
            && HasFollowingFiniteVerb(tokens, range, local, 2))
        {
            // Sentence-initial gerunds such as "Running is healthy" function nominally.
            return LexicalKind.Noun;
        }

        if (CommonNouns.Contains(word) || LexiconCategory(word) is GrammarCategory.ObjectNoun or GrammarCategory.SubjectNoun)
        {
            return LexicalKind.Noun;
        }

        if (LooksLikeNounByMorphology(word) || IsLikelyProperNoun(token, range, local))
        {
            return LexicalKind.Noun;
        }

        // Content-word fallback. Treating an unknown word as a noun is safer than
        // treating it as a verb solely because of a suffix; contextual verb scoring
        // above already captures productive verb forms.
        return LexicalKind.Noun;
    }

    private static LexicalKind ClassifyThat(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local,
        bool isQuestion)
    {
        if (isQuestion && local == 0)
        {
            return LexicalKind.Pronoun;
        }

        if (isQuestion
            && local > 0
            && IsAuxiliary(tokens[range.Start + local - 1].Normalized))
        {
            return LexicalKind.Pronoun;
        }

        if (HasFollowingNominal(tokens, range, local))
        {
            var previous = local > 0 ? tokens[range.Start + local - 1].Normalized : string.Empty;
            var next = local + 1 < range.Count ? tokens[range.Start + local + 1].Normalized : string.Empty;

            if (PreviousLooksNominal(tokens, range, local))
            {
                // Content nouns such as fact/claim/evidence can license a finite
                // content clause ("the fact that it works"). Other nominal
                // antecedents normally introduce a relative clause.
                if (ComplementTakingNouns.Contains(previous)
                    && HasFollowingFiniteVerb(tokens, range, local, 6)
                    && !LooksLikeObjectGapRelativeClause(tokens, range, local))
                {
                    return LexicalKind.Conjunction;
                }

                return LexicalKind.Pronoun;
            }

            if (local > 0
                && IsVerbWord(previous)
                && ((Determiners.Contains(next)
                     || PossessiveDeterminers.Contains(next)
                     || PersonalPronouns.Contains(next)
                     || next.EndsWith("'s", StringComparison.Ordinal))
                    || HasFollowingFiniteVerb(tokens, range, local, 8)))
            {
                // "I know that the model works" -> complementizer.
                return LexicalKind.Conjunction;
            }

            // "that model" / "use that model" -> demonstrative determiner.
            return LexicalKind.Determiner;
        }

        if (PreviousLooksNominal(tokens, range, local))
        {
            return LexicalKind.Pronoun;
        }

        return LexicalKind.Pronoun;
    }


    private static bool IsContextualAdjective(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local)
    {
        var word = tokens[range.Start + local].Normalized;
        if (ProductiveParticleWords.Contains(word)
            && IsPhrasalVerbParticle(tokens, range, local))
        {
            return false;
        }
        if (!EnglishMorphology.TryGetProfile(word, out var profile)
            || (profile.Tags & LexiconTag.Adjective) == 0)
        {
            return false;
        }

        var adjectiveWeight = profile.GetWeight(LexiconTag.Adjective);
        var nounWeight = profile.GetWeight(LexiconTag.Noun);
        var verbWeight = profile.GetWeight(LexiconTag.Verb);

        // Lexicon entries such as "open" can also be verbs. Prefer the adjective
        // reading only when syntax supplies an adjective-shaped environment.
        var previousContentWord = PreviousContentWordSkippingAdverbs(tokens, range, local, maxSkips: 3);
        if (previousContentWord is not null && IsCopularSurface(previousContentWord))
        {
            return !word.EndsWith("ing", StringComparison.OrdinalIgnoreCase)
                   && (adjectiveWeight >= verbWeight
                       || StativeParticipleAdjectives.Contains(word));
        }

        if (local > 0
            && local + 1 < range.Count
            && (RawLooksNominal(tokens[range.Start + local + 1].Normalized)
                || EnglishMorphology.HasTag(
                    tokens[range.Start + local + 1].Normalized,
                    LexiconTag.Noun)))
        {
            var attributivePrevious = tokens[range.Start + local - 1].Normalized;
            if (Determiners.Contains(attributivePrevious)
                || PossessiveDeterminers.Contains(attributivePrevious)
                || QuantifierWords.Contains(attributivePrevious))
            {
                return adjectiveWeight > nounWeight
                       && adjectiveWeight >= verbWeight;
            }
        }

        if (local + 1 < range.Count
            && ShallowFiniteVerbCandidate(tokens, range, local + 1))
        {
            return false;
        }

        if (local + 1 >= range.Count || !RawLooksNominal(tokens[range.Start + local + 1].Normalized))
        {
            return !CommonNouns.Contains(word)
                   && !CommonVerbs.Contains(word)
                   && adjectiveWeight >= Math.Max(nounWeight, verbWeight);
        }

        if (local == 0)
        {
            // "Open files are ..." is likely attributive; "Open the file" is
            // excluded because a determiner is not nominal here.
            return true;
        }

        var previousIndex = local - 1;
        var skipped = 0;
        while (previousIndex > 0 && skipped < 3)
        {
            var candidate = tokens[range.Start + previousIndex].Normalized;
            if (!CommonAdverbs.Contains(candidate)
                && !IsAdverbByLexiconOrMorphology(candidate))
            {
                break;
            }
            previousIndex--;
            skipped++;
        }
        var previous = tokens[range.Start + previousIndex].Normalized;
        var attributiveContext = Determiners.Contains(previous)
                                 || PossessiveDeterminers.Contains(previous)
                                 || CommonAdjectives.Contains(previous)
                                 || IsAdjectiveByLexiconOrMorphology(previous)
                                 || Prepositions.Contains(previous);
        return attributiveContext
               && adjectiveWeight > nounWeight
               && adjectiveWeight >= verbWeight;
    }

    private static bool LooksLikeVerb(IReadOnlyList<TextToken> tokens, SpanTokenIndex.TokenRange range, int local)
    {
        var word = tokens[range.Start + local].Normalized;
        if (ModalVerbs.Contains(word) || AuxiliaryVerbs.Contains(word))
        {
            return true;
        }

        var score = 0;
        if (CommonVerbs.Contains(word))
        {
            score += 9;
        }

        if (EnglishMorphology.TryGetProfile(word, out var profile))
        {
            var verbWeight = profile.GetWeight(LexiconTag.Verb);
            var nounWeight = profile.GetWeight(LexiconTag.Noun);
            var adjectiveWeight = profile.GetWeight(LexiconTag.Adjective);
            var adverbWeight = profile.GetWeight(LexiconTag.Adverb);
            var strongestAlternative = Math.Max(
                Math.Max(nounWeight, adjectiveWeight),
                adverbWeight);
            if (verbWeight > 0)
            {
                score += 2 + (verbWeight / 3);
                if (verbWeight >= strongestAlternative)
                {
                    score += 3;
                }
                else
                {
                    score -= Math.Min(3, (strongestAlternative - verbWeight) / 3);
                }
            }
            else if (nounWeight > 0)
            {
                score -= 3;
            }
        }

        if (word.Length > 3 && (word.EndsWith("ed", StringComparison.OrdinalIgnoreCase)
                                || word.EndsWith("en", StringComparison.OrdinalIgnoreCase)
                                || word.EndsWith("ing", StringComparison.OrdinalIgnoreCase)))
        {
            score += 3;
        }
        else if (word.Length > 3 && (word.EndsWith("ize", StringComparison.OrdinalIgnoreCase)
                                     || word.EndsWith("ise", StringComparison.OrdinalIgnoreCase)
                                     || word.EndsWith("ify", StringComparison.OrdinalIgnoreCase)))
        {
            score += 5;
        }
        else if (word.Length > 2 && (word.EndsWith("s", StringComparison.OrdinalIgnoreCase)
                                     || word.EndsWith("es", StringComparison.OrdinalIgnoreCase)))
        {
            score += 1;
        }

        if (local > 0)
        {
            var previous = tokens[range.Start + local - 1].Normalized;
            if (ModalVerbs.Contains(previous) || AuxiliaryVerbs.Contains(previous)
                || IsContractedAuxiliaryVerb(previous) || IsContractedPronounAuxiliary(previous))
            {
                score += 10;
            }
            else if (previous == "to")
            {
                score += LooksLikeInfinitive(tokens, range, local - 1) ? 10 : -12;
            }
            else if (PersonalPronouns.Contains(previous))
            {
                score += 6;
            }
            else if (previous is "whatever" or "whoever" or "whomever" or "what" or "who" or "which")
            {
                score += 6;
            }
            else if (Determiners.Contains(previous) || PossessiveDeterminers.Contains(previous) || Prepositions.Contains(previous))
            {
                score -= 12;
            }
            else if (CommonAdjectives.Contains(previous) || IsAdjectiveByLexiconOrMorphology(previous))
            {
                score -= 10;
            }
            else if (char.IsUpper(tokens[range.Start + local - 1].Text[0])
                     && IsContextualProperNoun(
                         tokens[range.Start + local - 1],
                         tokens,
                         range,
                         local - 1))
            {
                score += 4;
            }
            else if (IsInfinitiveTarget(tokens, range, local - 1)
                     || IsLikelyFinitePredicateAt(tokens, range, local - 1))
            {
                score += HasPrecedingClauseMarker(tokens, range, local) ? 5 : -10;
            }
            else if (IsVerbWord(previous))
            {
                score += VerbComplementLicensers.Contains(previous)
                         && word.EndsWith("ing", StringComparison.OrdinalIgnoreCase)
                    ? 4
                    : HasPrecedingClauseMarker(tokens, range, local) ? 5 : -10;
            }
            else if (RawLooksNominal(previous))
            {
                var previousPrevious = local > 1
                    ? tokens[range.Start + local - 2].Normalized
                    : string.Empty;
                var compoundContext = previous.EndsWith("'s", StringComparison.Ordinal)
                                      || Determiners.Contains(previousPrevious)
                                      || PossessiveDeterminers.Contains(previousPrevious)
                                      || CommonAdjectives.Contains(previousPrevious)
                                      || IsAdjectiveByLexiconOrMorphology(previousPrevious)
                                      || RawLooksNominal(previousPrevious);
                var finiteInflection = word.EndsWith("s", StringComparison.OrdinalIgnoreCase)
                                       || word.EndsWith("ed", StringComparison.OrdinalIgnoreCase)
                                       || IrregularPastParticiples.Contains(word);
                score += compoundContext && !finiteInflection ? -6 : 4;
            }
        }

        if (local + 1 < range.Count)
        {
            var next = tokens[range.Start + local + 1].Normalized;
            if (Determiners.Contains(next) || PossessiveDeterminers.Contains(next))
            {
                score += 5;
            }
            else if (PersonalPronouns.Contains(next))
            {
                score += 3;
            }
            else if (next == "to")
            {
                score += 4;
            }
        }
        else if (local == 0
                 && EnglishMorphology.HasTag(word, LexiconTag.Verb))
        {
            score += 6;
        }

        if (local == 0
            && local + 1 < range.Count
            && EnglishMorphology.HasTag(word, LexiconTag.Verb))
        {
            var next = tokens[range.Start + local + 1].Normalized;
            if (CommonAdverbs.Contains(next)
                || IsAdverbByLexiconOrMorphology(next)
                || Prepositions.Contains(next)
                || Determiners.Contains(next)
                || PersonalPronouns.Contains(next))
            {
                score += 6;
            }
        }

        if (IsAttributiveParticiple(tokens, range, local))
        {
            score -= 12;
        }

        if (local + 1 < range.Count && ShallowFiniteVerbCandidate(tokens, range, local + 1))
        {
            // "High recall means ..." / "The record shows ...": the content word
            // immediately before a likely finite verb is normally nominal.
            score -= 7;
        }

        return score >= 6;
    }

    private static bool IsStrongNominalContext(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local)
    {
        if (local <= 0)
        {
            return false;
        }
        var word = tokens[range.Start + local].Normalized;
        if (ModalVerbs.Contains(word)
            || AuxiliaryVerbs.Contains(word))
        {
            return false;
        }
        if (!EnglishMorphology.HasTag(word, LexiconTag.Noun))
        {
            return false;
        }
        var previous = tokens[range.Start + local - 1].Normalized;
        if (Determiners.Contains(previous)
            || PossessiveDeterminers.Contains(previous)
            || QuantifierWords.Contains(previous)
            || NumberWords.Contains(previous))
        {
            return true;
        }

        if (CommonVerbs.Contains(word)
            && local + 1 < range.Count
            && RawLooksNominal(tokens[range.Start + local + 1].Normalized))
        {
            return false;
        }
        if ((word.EndsWith("ing", StringComparison.OrdinalIgnoreCase)
             || word.EndsWith("ed", StringComparison.OrdinalIgnoreCase))
            && EnglishMorphology.HasTag(word, LexiconTag.Verb))
        {
            return false;
        }
        if (!GrammarLexicon.TryGetProfile(word, out var rawProfile)
            || (rawProfile.Tags & LexiconTag.Noun) == 0
            || word != "set"
               && rawProfile.GetWeight(LexiconTag.Verb)
                  > rawProfile.GetWeight(LexiconTag.Noun))
        {
            return false;
        }

        var start = Math.Max(0, local - 5);
        for (var cursor = local - 1; cursor >= start; cursor--)
        {
            var candidate = tokens[range.Start + cursor].Normalized;
            if (Determiners.Contains(candidate)
                || PossessiveDeterminers.Contains(candidate)
                || QuantifierWords.Contains(candidate)
                || NumberWords.Contains(candidate))
            {
                return true;
            }
            if (IsLikelyFinitePredicateAt(tokens, range, cursor))
            {
                break;
            }
            if (RawLooksNominal(candidate)
                || EnglishMorphology.HasTag(candidate, LexiconTag.Noun)
                || CommonAdjectives.Contains(candidate)
                || IsAdjectiveByLexiconOrMorphology(candidate))
            {
                continue;
            }
            break;
        }
        return false;
    }

    private static bool IsAuxiliaryChainAdverb(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local)
    {
        if (local <= 0 || local + 1 >= range.Count)
        {
            return false;
        }
        var word = tokens[range.Start + local].Normalized;
        if (!EnglishMorphology.HasTag(word, LexiconTag.Adverb))
        {
            return false;
        }
        var previous = tokens[range.Start + local - 1].Normalized;
        var next = tokens[range.Start + local + 1].Normalized;
        return (IsAuxiliary(previous) || IsContractedAuxiliaryVerb(previous)
                                      || IsContractedPronounAuxiliary(previous))
               && (IsAuxiliary(next) || ModalVerbs.Contains(next));
    }

    private static bool IsDeterminerAnchoredAttributive(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local)
    {
        if (local <= 0 || local + 1 >= range.Count)
        {
            return false;
        }

        var previous = tokens[range.Start + local - 1].Normalized;
        if (!Determiners.Contains(previous)
            && !PossessiveDeterminers.Contains(previous)
            && !QuantifierWords.Contains(previous))
        {
            return false;
        }

        var word = tokens[range.Start + local].Normalized;
        var next = tokens[range.Start + local + 1].Normalized;
        if (!EnglishMorphology.TryGetProfile(word, out var profile)
            || !EnglishMorphology.HasTag(next, LexiconTag.Noun))
        {
            return false;
        }
        var adjectiveWeight = profile.GetWeight(LexiconTag.Adjective);
        return adjectiveWeight > profile.GetWeight(LexiconTag.Noun)
               && adjectiveWeight >= profile.GetWeight(LexiconTag.Verb);
    }

    private static bool IsLexicalizedCompoundHead(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local)
    {
        if (local <= 1
            || tokens[range.Start + local].Normalized != "set"
            || !NominalSetModifiers.Contains(
                tokens[range.Start + local - 1].Normalized))
        {
            return false;
        }

        var anchor = tokens[range.Start + local - 2].Normalized;
        return Determiners.Contains(anchor)
               || PossessiveDeterminers.Contains(anchor)
               || QuantifierWords.Contains(anchor)
               || RawLooksNominal(anchor)
               || CommonAdjectives.Contains(anchor)
               || IsAdjectiveByLexiconOrMorphology(anchor);
    }

    private static bool IsSyntacticallyForcedVerb(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local)
    {
        if (local <= 0)
        {
            return false;
        }

        var word = tokens[range.Start + local].Normalized;
        if (ProductiveParticleWords.Contains(word)
            && IsPhrasalVerbParticle(tokens, range, local))
        {
            return false;
        }
        if (!EnglishMorphology.TryGetProfile(word, out var profile)
            || (profile.Tags & LexiconTag.Verb) == 0)
        {
            return false;
        }

        var previousIndex = local - 1;
        var skipped = 0;
        while (previousIndex > 0 && skipped < 3)
        {
            var candidate = tokens[range.Start + previousIndex].Normalized;
            if (candidate == "to"
                || !CommonAdverbs.Contains(candidate)
                && !IsAdverbByLexiconOrMorphology(candidate))
            {
                break;
            }
            previousIndex--;
            skipped++;
        }
        var previous = tokens[range.Start + previousIndex].Normalized;
        if (ModalVerbs.Contains(previous))
        {
            return true;
        }
        if (previous == "to")
        {
            return LooksLikeInfinitive(tokens, range, previousIndex);
        }
        if (IsContractedAuxiliaryVerb(previous)
            || IsContractedPronounAuxiliary(previous)
            || AuxiliaryVerbs.Contains(previous))
        {
            if (IsCopularSurface(previous))
            {
                if (!word.EndsWith("ing", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                return profile.GetWeight(LexiconTag.Verb)
                       >= profile.GetWeight(LexiconTag.Adjective);
            }
            return true;
        }
        if (PersonalPronouns.Contains(previous))
        {
            return profile.GetWeight(LexiconTag.Verb) > 0;
        }
        if (RawLooksNominal(previous)
            && IsFiniteVerbSurface(word)
            && HasSubjectAnchor(tokens, range, previousIndex)
            && (local + 1 >= range.Count
                || !ShallowFiniteVerbCandidate(tokens, range, local + 1)))
        {
            return true;
        }
        return false;
    }

    private static bool IsCoordinatedVerb(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local)
    {
        if (local <= 1)
        {
            return false;
        }

        var word = tokens[range.Start + local].Normalized;
        if (!EnglishMorphology.HasTag(word, LexiconTag.Verb)
            || !word.EndsWith("ing", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var coordinator = local - 1;
        var skippedAdverbs = 0;
        while (coordinator > 0 && skippedAdverbs < 2)
        {
            var candidate = tokens[range.Start + coordinator].Normalized;
            if (!CommonAdverbs.Contains(candidate)
                && !IsAdverbByLexiconOrMorphology(candidate))
            {
                break;
            }
            coordinator--;
            skippedAdverbs++;
        }

        if (tokens[range.Start + coordinator].Normalized
            is not ("and" or "or" or "nor"))
        {
            return false;
        }

        var start = Math.Max(0, coordinator - 12);
        for (var cursor = coordinator - 1; cursor >= start; cursor--)
        {
            var candidate = tokens[range.Start + cursor].Normalized;
            if ((candidate.EndsWith("ing", StringComparison.OrdinalIgnoreCase)
                 || candidate.EndsWith("ed", StringComparison.OrdinalIgnoreCase)
                 || candidate.EndsWith("en", StringComparison.OrdinalIgnoreCase))
                && EnglishMorphology.HasTag(candidate, LexiconTag.Verb))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsInfinitiveTarget(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local)
    {
        if (local <= 0
            || !EnglishMorphology.HasTag(
                tokens[range.Start + local].Normalized,
                LexiconTag.Verb))
        {
            return false;
        }

        var marker = local - 1;
        var skipped = 0;
        while (marker >= 0 && skipped < 3)
        {
            var candidate = tokens[range.Start + marker].Normalized;
            if (candidate == "to"
                || !CommonAdverbs.Contains(candidate)
                && !IsAdverbByLexiconOrMorphology(candidate))
            {
                break;
            }
            marker--;
            skipped++;
        }

        return marker >= 0
               && tokens[range.Start + marker].Normalized == "to"
               && LooksLikeInfinitive(tokens, range, marker);
    }

    private static bool HasSubjectAnchor(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int subject)
    {
        if (subject == 0)
        {
            return true;
        }

        var previous = tokens[range.Start + subject - 1].Normalized;
        return Determiners.Contains(previous)
               || PossessiveDeterminers.Contains(previous)
               || QuantifierWords.Contains(previous)
               || NumberWords.Contains(previous)
               || CommonAdjectives.Contains(previous)
               || IsAdjectiveByLexiconOrMorphology(previous);
    }

    private static bool IsFiniteVerbSurface(string word) =>
        word.Length > 3
        && (word.EndsWith("s", StringComparison.OrdinalIgnoreCase)
            || word.EndsWith("ed", StringComparison.OrdinalIgnoreCase)
            || word.EndsWith("en", StringComparison.OrdinalIgnoreCase)
            || IrregularPastParticiples.Contains(word));

    private static bool IsLikelyFinitePredicateAt(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local)
    {
        if (local <= 0)
        {
            return false;
        }

        var word = tokens[range.Start + local].Normalized;
        if (!IsFiniteVerbSurface(word)
            || !EnglishMorphology.HasTag(word, LexiconTag.Verb))
        {
            return false;
        }

        var subject = tokens[range.Start + local - 1].Normalized;
        return PersonalPronouns.Contains(subject)
               || RawLooksNominal(subject)
               && HasSubjectAnchor(tokens, range, local - 1);
    }

    private static bool ShallowFiniteVerbCandidate(IReadOnlyList<TextToken> tokens, SpanTokenIndex.TokenRange range, int local)
    {
        if (local < 0 || local >= range.Count)
        {
            return false;
        }

        var word = tokens[range.Start + local].Normalized;
        if (ModalVerbs.Contains(word) || AuxiliaryVerbs.Contains(word) || CommonVerbs.Contains(word))
        {
            return true;
        }

        if (EnglishMorphology.TryGetProfile(word, out var profile))
        {
            var verbWeight = profile.GetWeight(LexiconTag.Verb);
            var nominalWeight = Math.Max(
                profile.GetWeight(LexiconTag.Noun),
                profile.GetWeight(LexiconTag.Adjective));
            if (verbWeight > 0
                && (verbWeight >= nominalWeight
                    || word.EndsWith("ed", StringComparison.OrdinalIgnoreCase)
                    || IrregularPastParticiples.Contains(word)))
            {
                return true;
            }

            if (verbWeight > 0
                && word.EndsWith("s", StringComparison.OrdinalIgnoreCase)
                && local > 0)
            {
                var previous = tokens[range.Start + local - 1].Normalized;
                if (PersonalPronouns.Contains(previous)
                    || previous is "whatever" or "whoever" or "whomever"
                        or "who" or "what" or "which" or "that"
                    || (verbWeight + 2 >= nominalWeight && RawLooksNominal(previous)))
                {
                    return true;
                }
            }
        }

        if (local + 1 < range.Count
            && word.Length > 2
            && word.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            var next = tokens[range.Start + local + 1].Normalized;
            return Determiners.Contains(next) || PossessiveDeterminers.Contains(next);
        }

        return false;
    }

    private static bool IsAttributiveParticiple(IReadOnlyList<TextToken> tokens, SpanTokenIndex.TokenRange range, int local)
    {
        var word = tokens[range.Start + local].Normalized;
        if (word.Length <= 3 || !(word.EndsWith("ing", StringComparison.OrdinalIgnoreCase)
                                 || word.EndsWith("ed", StringComparison.OrdinalIgnoreCase)
                                 || word.EndsWith("en", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (local + 1 >= range.Count || !RawLooksNominal(tokens[range.Start + local + 1].Normalized))
        {
            return false;
        }

        if (local > 0)
        {
            var previous = tokens[range.Start + local - 1].Normalized;
            if (AuxiliaryVerbs.Contains(previous) || ModalVerbs.Contains(previous)
                || IsContractedAuxiliaryVerb(previous) || IsContractedPronounAuxiliary(previous) || previous == "to")
            {
                return false;
            }

            if (Determiners.Contains(previous) || PossessiveDeterminers.Contains(previous) || Prepositions.Contains(previous)
                || CommonAdjectives.Contains(previous) || IsAdjectiveByLexiconOrMorphology(previous))
            {
                return true;
            }
        }

        return local == 0;
    }

    private static bool IsNominalCompoundModifier(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local)
    {
        var word = tokens[range.Start + local].Normalized;
        if (!CommonNouns.Contains(word)
            || local + 1 >= range.Count
            || !RawLooksNominal(tokens[range.Start + local + 1].Normalized))
        {
            return false;
        }
        if (local == 0)
        {
            return true;
        }
        var previous = tokens[range.Start + local - 1].Normalized;
        return !AuxiliaryVerbs.Contains(previous)
               && !ModalVerbs.Contains(previous)
               && !IsContractedAuxiliaryVerb(previous)
               && !IsContractedPronounAuxiliary(previous);
    }

    private static int[] BuildClauseIds(
        string text,
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        IReadOnlyList<LexicalKind> kinds)
    {
        var ids = new int[range.Count];
        var clause = 0;
        var hasVerb = kinds[0] == LexicalKind.Verb;
        var startsWithPreposition = kinds[0] == LexicalKind.Preposition;

        for (var local = 1; local < range.Count; local++)
        {
            var previousToken = tokens[range.Start + local - 1];
            var token = tokens[range.Start + local];
            var gap = text.AsSpan(previousToken.Span.End, Math.Max(0, token.Span.Start - previousToken.Span.End));
            var word = token.Normalized;

            var hardBoundary = ContainsHardClausePunctuation(gap);
            var hasComma = gap.IndexOf(',') >= 0;
            var markerBoundary = IsClauseIntroducer(word, kinds[local], tokens, range, local);
            var previousKind = kinds[local - 1];
            var coordinatorBoundary = CoordinatingConjunctions.Contains(word)
                                      && hasVerb
                                      && previousKind is not (LexicalKind.Noun or LexicalKind.Adjective or LexicalKind.Determiner or LexicalKind.Quantifier)
                                      && HasVerbAhead(kinds, local + 1, Math.Min(range.Count, local + 8));
            var commaClauseStarter = kinds[local] is LexicalKind.Pronoun
                or LexicalKind.Interrogative
                or LexicalKind.Conjunction
                || kinds[local] == LexicalKind.Noun
                && local + 1 < range.Count
                && ShallowFiniteVerbCandidate(tokens, range, local + 1);
            var commaBoundary = hasComma
                                && (markerBoundary
                                    || startsWithPreposition
                                    || hasVerb && commaClauseStarter);

            if (hardBoundary || markerBoundary || coordinatorBoundary || commaBoundary)
            {
                clause++;
                hasVerb = false;
                startsWithPreposition = kinds[local] == LexicalKind.Preposition;
            }

            ids[local] = clause;
            if (kinds[local] == LexicalKind.Verb)
            {
                hasVerb = true;
            }
        }

        return ids;
    }

    private static void MarkSubjectNouns(
        string text,
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        IReadOnlyList<LexicalKind> kinds,
        IReadOnlyList<int> clauseIds,
        bool[] subjects)
    {
        MarkAntecedentSubjectsAcrossRelativeClauses(text, tokens, range, kinds, clauseIds, subjects);

        var clauseStart = 0;
        while (clauseStart < range.Count)
        {
            var clauseId = clauseIds[clauseStart];
            var clauseEnd = clauseStart + 1;
            while (clauseEnd < range.Count && clauseIds[clauseEnd] == clauseId)
            {
                clauseEnd++;
            }

            MarkSubjectsInClause(text, tokens, range, kinds, clauseStart, clauseEnd, subjects);
            clauseStart = clauseEnd;
        }
    }

    private static void MarkAntecedentSubjectsAcrossRelativeClauses(
        string text,
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        IReadOnlyList<LexicalKind> kinds,
        IReadOnlyList<int> clauseIds,
        bool[] subjects)
    {
        // Precompute reusable sentence context once. Relative-clause-heavy or
        // malformed run-on text must not trigger repeated whole-sentence scans.
        var verbPrefix = new int[range.Count + 1];
        var verbSuffix = new int[range.Count + 1];
        var clauseStartByToken = new int[range.Count];
        var nearestNonModifier = new int[range.Count];
        var currentClauseStart = 0;
        var lastNonModifier = -1;

        for (var local = 0; local < range.Count; local++)
        {
            verbPrefix[local + 1] = verbPrefix[local] + (kinds[local] == LexicalKind.Verb ? 1 : 0);
            if (local == 0 || clauseIds[local] != clauseIds[local - 1])
            {
                currentClauseStart = local;
            }
            clauseStartByToken[local] = currentClauseStart;

            if (kinds[local] is not (LexicalKind.Adjective or LexicalKind.Adverb))
            {
                lastNonModifier = local;
            }
            nearestNonModifier[local] = lastNonModifier;
        }

        for (var local = range.Count - 1; local >= 0; local--)
        {
            verbSuffix[local] = verbSuffix[local + 1] + (kinds[local] == LexicalKind.Verb ? 1 : 0);
        }

        for (var marker = 1; marker < range.Count - 1; marker++)
        {
            var markerWord = tokens[range.Start + marker].Normalized;
            var isRelativeMarker = kinds[marker] == LexicalKind.Pronoun
                                   && markerWord is ("that" or "which" or "who" or "whom" or "whose");
            var isContentClauseMarker = kinds[marker] == LexicalKind.Conjunction
                                        && markerWord == "that"
                                        && ComplementTakingNouns.Contains(tokens[range.Start + marker - 1].Normalized);
            if (!isRelativeMarker && !isContentClauseMarker)
            {
                continue;
            }

            var antecedent = nearestNonModifier[marker - 1];
            if (antecedent < 0 || kinds[antecedent] != LexicalKind.Noun)
            {
                continue;
            }

            var clauseStart = clauseStartByToken[antecedent];
            var verbBeforeAntecedent = verbPrefix[antecedent] - verbPrefix[clauseStart] > 0;
            if (verbBeforeAntecedent
                || IsGovernedByPreposition(tokens, range, kinds, clauseStart, antecedent))
            {
                continue;
            }

            // A subject NP interrupted by a relative clause resumes at the main
            // predicate: "The report that the team wrote passed." Requiring at
            // least two following verbs prevents an isolated relative-clause NP
            // fragment from being promoted to a sentence subject.
            if (verbSuffix[marker + 1] >= 2)
            {
                subjects[antecedent] = true;
                MarkSimpleCoordinatedSubject(text, tokens, range, kinds, clauseStart, antecedent, subjects);
            }
        }
    }

    private static void MarkSubjectsInClause(
        string text,
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        IReadOnlyList<LexicalKind> kinds,
        int start,
        int end,
        bool[] subjects)
    {
        var verbs = new List<int>(4);
        for (var local = start; local < end; local++)
        {
            if (kinds[local] == LexicalKind.Verb)
            {
                verbs.Add(local);
            }
        }

        if (verbs.Count == 0)
        {
            return;
        }

        var firstVerb = verbs[0];
        var subject = FindSubjectBeforeVerb(tokens, range, kinds, start, firstVerb);
        if (subject >= 0)
        {
            subjects[subject] = true;
            MarkSimpleCoordinatedSubject(text, tokens, range, kinds, start, subject, subjects);
        }
        else if (IsAuxiliary(tokens[range.Start + firstVerb].Normalized))
        {
            // Subject-auxiliary inversion: "Are the results accurate?" / "Did Alice leave?"
            var inverted = FindSubjectAfterAuxiliary(tokens, range, kinds, firstVerb + 1, end);
            if (inverted >= 0)
            {
                subjects[inverted] = true;
                MarkSimpleCoordinatedSubject(text, tokens, range, kinds, firstVerb + 1, inverted, subjects);
            }
        }

        if (subject < 0 && firstVerb > start
            && tokens[range.Start + firstVerb - 1].Normalized == "there"
            && CopularVerbs.Contains(tokens[range.Start + firstVerb].Normalized))
        {
            var existential = FindFirstNoun(kinds, firstVerb + 1, end);
            if (existential >= 0)
            {
                subjects[existential] = true;
            }
        }

        // Detect overt subjects of embedded/non-finite clauses that have no explicit
        // complementizer: "recall means the model misses...", "expect John to leave".
        for (var verbIndex = 1; verbIndex < verbs.Count; verbIndex++)
        {
            var previousVerb = verbs[verbIndex - 1];
            var currentVerb = verbs[verbIndex];
            if (ContainsCoordinator(tokens, range, previousVerb + 1, currentVerb))
            {
                continue;
            }
            if (!CanHaveOvertEmbeddedSubject(
                    tokens,
                    range,
                    previousVerb,
                    currentVerb))
            {
                continue;
            }

            var embeddedSubject = FindSubjectBetweenVerbs(tokens, range, kinds, previousVerb + 1, currentVerb);
            if (embeddedSubject >= 0)
            {
                subjects[embeddedSubject] = true;
                MarkSimpleCoordinatedSubject(text, tokens, range, kinds, previousVerb + 1, embeddedSubject, subjects);
            }
        }

        // Predicate nominatives have no dedicated public category. SubjectNoun is
        // the closest traditional-grammar representation because the complement
        // renames/identifies the subject rather than functioning as an object.
        if (CopularVerbs.Contains(tokens[range.Start + firstVerb].Normalized))
        {
            var predicateNoun = FindPredicateNoun(tokens, range, kinds, firstVerb + 1, end);
            if (predicateNoun >= 0)
            {
                subjects[predicateNoun] = true;
            }
        }
    }

    private static bool CanHaveOvertEmbeddedSubject(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int previousVerb,
        int currentVerb)
    {
        var previous = tokens[range.Start + previousVerb].Normalized;
        var current = tokens[range.Start + currentVerb].Normalized;
        var sawFor = false;
        for (var cursor = previousVerb + 1; cursor < currentVerb; cursor++)
        {
            var marker = tokens[range.Start + cursor].Normalized;
            sawFor |= marker == "for";
            if (marker == "to")
            {
                return sawFor
                       || MatchesAnyLemma(previous, ObjectControlVerbLemmas);
            }
        }

        if (current.EndsWith("ed", StringComparison.OrdinalIgnoreCase)
            || current.EndsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return MatchesAnyLemma(previous, FiniteClauseComplementVerbLemmas);
        }
        if (current.EndsWith("ing", StringComparison.OrdinalIgnoreCase))
        {
            return MatchesAnyLemma(previous, PerceptionVerbLemmas);
        }
        return true;
    }

    private static bool MatchesAnyLemma(string surface, IEnumerable<string> lemmas)
    {
        foreach (var lemma in lemmas)
        {
            if (EnglishMorphology.IsInflectionOf(surface, lemma))
            {
                return true;
            }
        }
        return false;
    }

    private static int FindSubjectBeforeVerb(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        IReadOnlyList<LexicalKind> kinds,
        int start,
        int verb)
    {
        for (var local = verb - 1; local >= start; local--)
        {
            if (kinds[local] == LexicalKind.Pronoun)
            {
                if (!IsGovernedByPreposition(tokens, range, kinds, start, local))
                {
                    return -1; // grammatical subject exists, but it is a Pronoun category
                }
            }

            if (kinds[local] != LexicalKind.Noun)
            {
                continue;
            }

            if (!IsGovernedByPreposition(tokens, range, kinds, start, local))
            {
                return local;
            }
        }

        return -1;
    }

    private static int FindSubjectAfterAuxiliary(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        IReadOnlyList<LexicalKind> kinds,
        int start,
        int end)
    {
        for (var local = start; local < end; local++)
        {
            if (kinds[local] == LexicalKind.Verb && local > start)
            {
                break;
            }

            if (kinds[local] == LexicalKind.Pronoun)
            {
                return -1;
            }

            if (kinds[local] == LexicalKind.Noun
                && !IsGovernedByPreposition(tokens, range, kinds, start, local))
            {
                return local;
            }
        }

        return -1;
    }

    private static int FindSubjectBetweenVerbs(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        IReadOnlyList<LexicalKind> kinds,
        int start,
        int end)
    {
        var sawSubjectSignal = false;
        for (var local = start; local < end; local++)
        {
            if (kinds[local] is LexicalKind.Determiner or LexicalKind.Quantifier or LexicalKind.Pronoun)
            {
                sawSubjectSignal = true;
            }
        }

        for (var local = end - 1; local >= start; local--)
        {
            if (kinds[local] == LexicalKind.Pronoun)
            {
                return -1;
            }

            if (kinds[local] == LexicalKind.Noun
                && !IsGovernedByPreposition(tokens, range, kinds, start, local))
            {
                if (sawSubjectSignal || local == start || local == end - 1)
                {
                    return local;
                }
            }
        }

        return -1;
    }

    private static int FindPredicateNoun(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        IReadOnlyList<LexicalKind> kinds,
        int start,
        int end)
    {
        for (var local = start; local < end; local++)
        {
            if (kinds[local] == LexicalKind.Verb)
            {
                break;
            }

            if (kinds[local] == LexicalKind.Noun
                && !IsGovernedByPreposition(tokens, range, kinds, start, local))
            {
                return local;
            }
        }

        return -1;
    }

    private static bool IsGovernedByPreposition(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        IReadOnlyList<LexicalKind> kinds,
        int start,
        int noun)
    {
        for (var local = noun - 1; local >= start; local--)
        {
            if (kinds[local] is LexicalKind.Adjective or LexicalKind.Determiner or LexicalKind.Quantifier or LexicalKind.Noun)
            {
                continue;
            }

            if (kinds[local] == LexicalKind.Preposition)
            {
                return true;
            }

            if (kinds[local] is LexicalKind.Conjunction or LexicalKind.Verb or LexicalKind.Pronoun)
            {
                return false;
            }

            var word = tokens[range.Start + local].Normalized;
            if (word == "to")
            {
                return false;
            }
        }

        return false;
    }

    private static void MarkSimpleCoordinatedSubject(
        string text,
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        IReadOnlyList<LexicalKind> kinds,
        int start,
        int subject,
        bool[] subjects)
    {
        var conjunction = -1;
        for (var local = subject - 1; local >= start; local--)
        {
            if (kinds[local] == LexicalKind.Noun)
            {
                // An adjacent noun immediately before the subject head is usually a
                // compound modifier ("development teams"), not another subject.
                if (conjunction < 0)
                {
                    return;
                }
                break;
            }

            if (CoordinatingConjunctions.Contains(tokens[range.Start + local].Normalized))
            {
                conjunction = local;
                break;
            }

            if (kinds[local] is not (LexicalKind.Adjective or LexicalKind.Determiner or LexicalKind.Quantifier or LexicalKind.Adverb))
            {
                break;
            }
        }

        if (conjunction < 0)
        {
            return;
        }

        var previousHead = -1;
        for (var local = conjunction - 1; local >= start; local--)
        {
            if (kinds[local] == LexicalKind.Noun
                && !IsGovernedByPreposition(tokens, range, kinds, start, local))
            {
                subjects[local] = true;
                if (previousHead < 0)
                {
                    previousHead = local;
                    continue;
                }

                // Continue through a serial subject list only when punctuation
                // between noun heads contains a comma: "Alice, Bob, and Carol".
                var left = tokens[range.Start + local];
                var right = tokens[range.Start + previousHead];
                var gapLength = Math.Max(0, right.Span.Start - left.Span.End);
                if (gapLength == 0 || text.AsSpan(left.Span.End, gapLength).IndexOf(',') < 0)
                {
                    return;
                }

                previousHead = local;
                continue;
            }

            if (kinds[local] is LexicalKind.Verb or LexicalKind.Preposition
                || (kinds[local] == LexicalKind.Conjunction
                    && !CoordinatingConjunctions.Contains(tokens[range.Start + local].Normalized)))
            {
                return;
            }
        }
    }

    private static GrammarCategory ToGrammarCategory(LexicalKind kind, bool subjectNoun) => kind switch
    {
        LexicalKind.Noun => subjectNoun ? GrammarCategory.SubjectNoun : GrammarCategory.ObjectNoun,
        LexicalKind.Verb => GrammarCategory.Verb,
        LexicalKind.Adjective => GrammarCategory.Adjective,
        LexicalKind.Adverb => GrammarCategory.Adverb,
        LexicalKind.Pronoun => GrammarCategory.Pronoun,
        LexicalKind.Preposition => GrammarCategory.Preposition,
        LexicalKind.Conjunction => GrammarCategory.Conjunction,
        LexicalKind.Interrogative => GrammarCategory.Interrogative,
        LexicalKind.Quantifier => GrammarCategory.Quantifier,
        LexicalKind.Determiner => GrammarCategory.Determiner,
        LexicalKind.Particle => GrammarCategory.Particle,
        _ => GrammarCategory.Other
    };

    private static GrammarCategory ClassifyStandalone(TextToken token)
    {
        var word = token.Normalized;
        if (IsNumericToken(token.Text)) return GrammarCategory.Quantifier;
        if (NumberWords.Contains(word)) return GrammarCategory.Quantifier;
        if (TryClassifyContraction(word, isQuestion: false, out var contractionKind))
        {
            return ToGrammarCategory(contractionKind, subjectNoun: false);
        }
        if (token.Text.Length > 1 && token.Text.All(char.IsUpper)) return GrammarCategory.ObjectNoun;
        if (word == "to") return GrammarCategory.Particle;
        if (PossessiveDeterminers.Contains(word)) return GrammarCategory.Determiner;
        if (Determiners.Contains(word)) return QuantifierWords.Contains(word) ? GrammarCategory.Quantifier : GrammarCategory.Determiner;
        if (InterrogativeWords.Contains(word)) return GrammarCategory.Interrogative;
        if (PersonalPronouns.Contains(word) || PossessivePronouns.Contains(word)) return GrammarCategory.Pronoun;
        if (CoordinatingConjunctions.Contains(word) || SubordinatingConjunctions.Contains(word)) return GrammarCategory.Conjunction;
        if (Prepositions.Contains(word)) return GrammarCategory.Preposition;
        if (CommonAdverbs.Contains(word) || IsAdverbByLexiconOrMorphology(word)) return GrammarCategory.Adverb;
        if (CommonAdjectives.Contains(word) || IsAdjectiveByLexiconOrMorphology(word)) return GrammarCategory.Adjective;
        if (IsVerbWord(word)) return GrammarCategory.Verb;
        return GrammarCategory.ObjectNoun;
    }

    private static void AddResult(
        TextToken token,
        GrammarCategory category,
        ICollection<ColoredSpan> spans,
        IDictionary<GrammarCategory, int> counts)
    {
        counts[category]++;
        if (category != GrammarCategory.Other)
        {
            spans.Add(new ColoredSpan(token.Span, category));
        }
    }

    private static bool LooksLikeObjectGapRelativeClause(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int marker)
    {
        var firstVerb = -1;
        var searchEnd = Math.Min(range.Count, marker + 25);
        for (var local = marker + 1; local < searchEnd; local++)
        {
            if (ShallowFiniteVerbCandidate(tokens, range, local))
            {
                firstVerb = local;
                break;
            }
        }

        if (firstVerb < 0 || !StronglyTransitiveVerbs.Contains(tokens[range.Start + firstVerb].Normalized))
        {
            return false;
        }

        // A transitive relative-clause verb with no overt post-verbal object before
        // the next finite predicate strongly suggests that the antecedent fills the
        // missing object slot: "the report that the team wrote passed".
        for (var local = firstVerb + 1; local < searchEnd; local++)
        {
            if (ShallowFiniteVerbCandidate(tokens, range, local))
            {
                return true;
            }

            var word = tokens[range.Start + local].Normalized;
            if (RawLooksNominal(word) || PersonalPronouns.Contains(word))
            {
                return false;
            }

            if (CoordinatingConjunctions.Contains(word) || SubordinatingConjunctions.Contains(word))
            {
                break;
            }
        }

        return false;
    }

    private static bool IsGerundNominal(IReadOnlyList<TextToken> tokens, SpanTokenIndex.TokenRange range, int local)
    {
        var word = tokens[range.Start + local].Normalized;
        if (word.Length <= 4 || !word.EndsWith("ing", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (local > 0)
        {
            var previous = tokens[range.Start + local - 1].Normalized;
            if (GerundTakingVerbs.Contains(previous) || Prepositions.Contains(previous))
            {
                return true;
            }
        }

        // A clause-initial -ing form followed by a finite predicate is usually a
        // gerund phrase functioning nominally: "Running is healthy."
        return local == 0 && HasFollowingFiniteVerb(tokens, range, local, 3);
    }

    private static bool IsPassiveParticipleContext(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local)
    {
        if (local <= 0)
        {
            return false;
        }

        var previous = PreviousContentWordSkippingAdverbs(tokens, range, local, maxSkips: 3);
        if (previous is null || !IsPassiveAuxiliarySurface(previous))
        {
            return false;
        }

        var word = tokens[range.Start + local].Normalized;
        var participleShape = word.EndsWith("ed", StringComparison.OrdinalIgnoreCase)
                              || word.EndsWith("en", StringComparison.OrdinalIgnoreCase)
                              || IrregularPastParticiples.Contains(word);
        if (!participleShape)
        {
            return false;
        }

        // A following by-phrase is strong evidence for verbal passive use and
        // resolves adjective/participle ambiguity ("was carefully reviewed by...").
        return HasFollowingByPhrase(tokens, range, local, maxSkips: 3);
    }

    private static bool IsProgressiveParticipleContext(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local)
    {
        var word = tokens[range.Start + local].Normalized;
        if (local <= 0
            || word.Length <= 4
            || !word.EndsWith("ing", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var previous = PreviousContentWordSkippingAdverbs(tokens, range, local, maxSkips: 3);
        if (previous is null || !IsCopularSurface(previous))
        {
            return false;
        }
        return !EnglishMorphology.TryGetProfile(word, out var profile)
               || profile.GetWeight(LexiconTag.Verb)
                   >= profile.GetWeight(LexiconTag.Adjective);
    }

    private static bool IsPredicativeStativeParticiple(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local)
    {
        if (local <= 0)
        {
            return false;
        }

        var word = tokens[range.Start + local].Normalized;
        if (!StativeParticipleAdjectives.Contains(word))
        {
            return false;
        }

        var previous = PreviousContentWordSkippingAdverbs(tokens, range, local, maxSkips: 3);
        return previous is not null
               && IsCopularSurface(previous)
               && !HasFollowingByPhrase(tokens, range, local, maxSkips: 3);
    }

    private static bool IsPhrasalVerbParticle(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local)
    {
        if (local <= 0)
        {
            return false;
        }

        var word = tokens[range.Start + local].Normalized;
        if (!Prepositions.Contains(word))
        {
            return false;
        }

        var previous = tokens[range.Start + local - 1].Normalized;
        if (PhrasalVerbParticlePairs.Contains($"{previous} {word}"))
        {
            return true;
        }

        if (!ProductiveParticleWords.Contains(word))
        {
            return false;
        }

        var start = Math.Max(0, local - 3);
        for (var cursor = local - 1; cursor >= start; cursor--)
        {
            var candidate = tokens[range.Start + cursor].Normalized;
            foreach (var pair in PhrasalVerbParticlePairs)
            {
                var separator = pair.LastIndexOf(' ');
                if (separator <= 0
                    || !pair.AsSpan(separator + 1).Equals(
                        word,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (EnglishMorphology.IsInflectionOf(candidate, pair.AsSpan(0, separator)))
                {
                    return true;
                }
            }
            if (CoordinatingConjunctions.Contains(candidate)
                || SubordinatingConjunctions.Contains(candidate)
                || Prepositions.Contains(candidate))
            {
                break;
            }
        }
        return false;
    }

    private static bool IsAdverbialSubordinatorUse(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local)
    {
        var word = tokens[range.Start + local].Normalized;
        if (word is not ("when" or "whenever" or "where" or "wherever"))
        {
            return false;
        }

        if (IsEmbeddedQuestion(tokens, range, local))
        {
            return false;
        }

        if (local + 1 >= range.Count)
        {
            return false;
        }

        var next = tokens[range.Start + local + 1].Normalized;
        var beginsSubject = Determiners.Contains(next)
                            || PossessiveDeterminers.Contains(next)
                            || PersonalPronouns.Contains(next)
                            || RawLooksNominal(next);
        return beginsSubject && HasFollowingFiniteVerb(tokens, range, local, 6);
    }

    private static bool LooksLikeInfinitive(IReadOnlyList<TextToken> tokens, SpanTokenIndex.TokenRange range, int local)
    {
        if (local + 1 >= range.Count)
        {
            return false;
        }

        var candidateIndex = local + 1;
        var skippedAdverbs = 0;
        while (candidateIndex < range.Count && skippedAdverbs < 3)
        {
            var modifier = tokens[range.Start + candidateIndex].Normalized;
            if (!CommonAdverbs.Contains(modifier)
                && !IsAdverbByLexiconOrMorphology(modifier))
            {
                break;
            }
            candidateIndex++;
            skippedAdverbs++;
        }
        if (candidateIndex >= range.Count)
        {
            return false;
        }

        var next = tokens[range.Start + candidateIndex].Normalized;
        if (next.EndsWith("ing", StringComparison.OrdinalIgnoreCase)
            || next.EndsWith("ed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ModalVerbs.Contains(next) || AuxiliaryVerbs.Contains(next) || CommonVerbs.Contains(next)
            || AmbiguousBareInfinitiveVerbs.Contains(next))
        {
            return true;
        }

        if (next.Length > 3 && (next.EndsWith("ize", StringComparison.OrdinalIgnoreCase)
                               || next.EndsWith("ise", StringComparison.OrdinalIgnoreCase)
                               || next.EndsWith("ify", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!EnglishMorphology.TryGetProfile(next, out var profile)
            || (profile.Tags & LexiconTag.Verb) == 0)
        {
            return false;
        }
        var verbWeight = profile.GetWeight(LexiconTag.Verb);
        var nounWeight = profile.GetWeight(LexiconTag.Noun);
        if (verbWeight >= nounWeight)
        {
            return true;
        }

        if (candidateIndex + 1 >= range.Count)
        {
            return false;
        }
        var following = tokens[range.Start + candidateIndex + 1].Normalized;
        if (Determiners.Contains(following)
            || PossessiveDeterminers.Contains(following)
            || PersonalPronouns.Contains(following)
            || NumberWords.Contains(following))
        {
            return true;
        }
        return verbWeight >= 6
               && (Prepositions.Contains(following)
                   || ProductiveParticleWords.Contains(following)
                   || RawLooksNominal(following)
                   || CommonAdjectives.Contains(following)
                   || IsAdjectiveByLexiconOrMorphology(following));
    }

    private static bool IsRelativeWh(IReadOnlyList<TextToken> tokens, SpanTokenIndex.TokenRange range, int local)
    {
        return local > 0 && PreviousLooksNominal(tokens, range, local);
    }

    private static bool IsEmbeddedQuestion(IReadOnlyList<TextToken> tokens, SpanTokenIndex.TokenRange range, int local)
    {
        if (local == 0)
        {
            return false;
        }

        var previous = tokens[range.Start + local - 1].Normalized;
        return previous is "ask" or "asks" or "asked" or "wonder" or "wonders" or "wondered"
            or "know" or "knows" or "knew" or "explain" or "explains" or "explained";
    }

    private static bool IsWhQuestionSyntax(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local)
    {
        if (local != 0)
        {
            return false;
        }

        var end = Math.Min(range.Count, local + 4);
        for (var cursor = local + 1; cursor < end; cursor++)
        {
            var word = tokens[range.Start + cursor].Normalized;
            if (ModalVerbs.Contains(word)
                || AuxiliaryVerbs.Contains(word)
                || IsContractedAuxiliaryVerb(word)
                || IsContractedPronounAuxiliary(word))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsPrepositionalUse(
        string text,
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local)
    {
        var word = tokens[range.Start + local].Normalized;
        if (!Prepositions.Contains(word))
        {
            return false;
        }

        if (local + 1 >= range.Count)
        {
            return true;
        }

        var next = tokens[range.Start + local + 1].Normalized;
        if (CoordinatingConjunctions.Contains(next))
        {
            return true;
        }
        if (next.EndsWith("ing", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (word == "as"
            && local > 0
            && tokens[range.Start + local - 1].Normalized == "such")
        {
            return true;
        }
        if (word == "as"
            && (next == "to"
                || next == "for"
                || local > 0
                   && tokens[range.Start + local - 1].Normalized
                       is "regarded" or "regard" or "regards" or "named" or "known"
                           or "served" or "serve" or "serves" or "used" or "use"
                           or "uses" or "refer" or "referred" or "described"
                           or "joined" or "join" or "joins" or "viewed" or "treated"))
        {
            return true;
        }
        if (word == "as" && local > 0)
        {
            var previous = tokens[range.Start + local - 1].Normalized;
            if (RawLooksNominal(previous) && !IsVerbWord(previous))
            {
                return true;
            }
        }
        if (word == "than"
            && !PersonalPronouns.Contains(next)
            && !ContractedPronounAuxiliaries.Contains(NormalizeApostrophe(next)))
        {
            return true;
        }

        // before/after/since/until + noun phrase is prepositional; + finite clause is subordinating.
        return !HasFollowingClausePredicate(text, tokens, range, local, 8);
    }

    private static bool IsCoordinatingFor(
        string text,
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local)
    {
        if (local <= 0 || local + 1 >= range.Count)
        {
            return false;
        }
        var previous = tokens[range.Start + local - 1];
        var current = tokens[range.Start + local];
        var gapLength = Math.Max(0, current.Span.Start - previous.Span.End);
        if (gapLength == 0)
        {
            return false;
        }
        var gap = text.AsSpan(previous.Span.End, gapLength);
        var hasBoundary = gap.IndexOf(',') >= 0
                          || gap.IndexOf(';') >= 0
                          || gap.IndexOf(':') >= 0
                          || gap.IndexOf('—') >= 0;
        return hasBoundary
               && HasFollowingClausePredicate(text, tokens, range, local, 8);
    }

    private static bool IsAdverbialSo(
        string text,
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local)
    {
        if (local + 1 >= range.Count)
        {
            return true;
        }
        var nextToken = tokens[range.Start + local + 1];
        var next = nextToken.Normalized;
        if (QuantifierWords.Contains(next)
            || CommonAdjectives.Contains(next)
            || CommonAdverbs.Contains(next)
            || IsAdjectiveByLexiconOrMorphology(next)
            || IsAdverbByLexiconOrMorphology(next))
        {
            return true;
        }
        var token = tokens[range.Start + local];
        var gapLength = Math.Max(0, nextToken.Span.Start - token.Span.End);
        return gapLength > 0
               && text.AsSpan(token.Span.End, gapLength).IndexOf(',') >= 0;
    }

    private static bool HasFollowingClausePredicate(
        string text,
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local,
        int lookahead)
    {
        var end = Math.Min(range.Count, local + 1 + lookahead);
        var sawSubjectSignal = false;
        var previous = tokens[range.Start + local];
        for (var cursor = local + 1; cursor < end; cursor++)
        {
            var token = tokens[range.Start + cursor];
            var gapLength = Math.Max(0, token.Span.Start - previous.Span.End);
            if (gapLength > 0)
            {
                var gap = text.AsSpan(previous.Span.End, gapLength);
                if (gap.IndexOf(',') >= 0
                    || gap.IndexOf(';') >= 0
                    || gap.IndexOf(':') >= 0)
                {
                    return false;
                }
            }

            var word = token.Normalized;
            if (word is "who" or "whom" or "whose" or "which" or "where")
            {
                return false;
            }
            if (ShallowFiniteVerbCandidate(tokens, range, cursor))
            {
                return sawSubjectSignal;
            }
            if (PersonalPronouns.Contains(word)
                || Determiners.Contains(word)
                || PossessiveDeterminers.Contains(word)
                || RawLooksNominal(word))
            {
                sawSubjectSignal = true;
            }
            previous = token;
        }
        return false;
    }

    private static bool IsAdverbialPrepositionUse(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local)
    {
        var word = tokens[range.Start + local].Normalized;
        if (!AdverbialPrepositionWords.Contains(word))
        {
            return false;
        }
        if (local + 1 >= range.Count)
        {
            return EnglishMorphology.HasTag(word, LexiconTag.Adverb);
        }

        var previous = local > 0
            ? tokens[range.Start + local - 1].Normalized
            : string.Empty;
        if (word == "about" && previous is "just" or "only" or "roughly")
        {
            return true;
        }

        var next = tokens[range.Start + local + 1].Normalized;
        if (Determiners.Contains(next)
            || PossessiveDeterminers.Contains(next)
            || PersonalPronouns.Contains(next)
            || RawLooksNominal(next))
        {
            return false;
        }
        return EnglishMorphology.HasTag(word, LexiconTag.Adverb);
    }

    private static bool HasFollowingNominal(IReadOnlyList<TextToken> tokens, SpanTokenIndex.TokenRange range, int local)
    {
        if (local + 1 >= range.Count)
        {
            return false;
        }

        var next = tokens[range.Start + local + 1].Normalized;
        if (Determiners.Contains(next) || PossessiveDeterminers.Contains(next) || PersonalPronouns.Contains(next))
        {
            return true;
        }

        if (ShallowFiniteVerbCandidate(tokens, range, local + 1))
        {
            return false;
        }

        return RawLooksNominal(next) || CommonAdjectives.Contains(next) || IsAdjectiveByLexiconOrMorphology(next);
    }

    private static bool HasFollowingFiniteVerb(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local,
        int lookahead)
    {
        var end = Math.Min(range.Count, local + 1 + lookahead);
        for (var cursor = local + 1; cursor < end; cursor++)
        {
            if (ShallowFiniteVerbCandidate(tokens, range, cursor))
            {
                return true;
            }
        }
        return false;
    }

    private static bool PreviousLooksNominal(IReadOnlyList<TextToken> tokens, SpanTokenIndex.TokenRange range, int local)
    {
        if (local <= 0)
        {
            return false;
        }

        var previous = tokens[range.Start + local - 1].Normalized;
        return RawLooksNominal(previous) || PersonalPronouns.Contains(previous) || PossessivePronouns.Contains(previous);
    }

    private static bool RawLooksNominal(string word)
    {
        if (string.IsNullOrWhiteSpace(word) || word == "to")
        {
            return false;
        }
        if (IsPossessiveSurface(word))
        {
            return true;
        }

        if (CommonNouns.Contains(word))
        {
            return true;
        }

        if (EnglishMorphology.TryGetProfile(word, out var profile))
        {
            var nounWeight = profile.GetWeight(LexiconTag.Noun);
            var verbWeight = profile.GetWeight(LexiconTag.Verb);
            var adjectiveWeight = profile.GetWeight(LexiconTag.Adjective);
            if (nounWeight > 0 && nounWeight >= Math.Max(verbWeight, adjectiveWeight))
            {
                return true;
            }
        }

        return LooksLikeNounByMorphology(word)
               || (!IsVerbWord(word)
                   && !CommonAdjectives.Contains(word)
                   && !IsAdjectiveByLexiconOrMorphology(word)
                   && !CommonAdverbs.Contains(word)
                   && !IsAdverbByLexiconOrMorphology(word)
                   && !Determiners.Contains(word)
                   && !PossessiveDeterminers.Contains(word)
                   && !NumberWords.Contains(word)
                   && !PersonalPronouns.Contains(word)
                   && !Prepositions.Contains(word)
                   && !CoordinatingConjunctions.Contains(word)
                   && !SubordinatingConjunctions.Contains(word));
    }

    private static bool IsVerbWord(string word)
    {
        if (IsPossessiveSurface(word))
        {
            return false;
        }
        if (ModalVerbs.Contains(word) || AuxiliaryVerbs.Contains(word) || CommonVerbs.Contains(word))
        {
            return true;
        }

        if (!EnglishMorphology.TryGetProfile(word, out var profile))
        {
            return false;
        }

        var verbWeight = profile.GetWeight(LexiconTag.Verb);
        return verbWeight > 0
               && verbWeight >= Math.Max(
                   profile.GetWeight(LexiconTag.Noun),
                   profile.GetWeight(LexiconTag.Adjective));
    }

    private static bool IsAdverbByLexiconOrMorphology(string word)
    {
        if (EnglishMorphology.TryGetProfile(word, out var profile))
        {
            var adverbWeight = profile.GetWeight(LexiconTag.Adverb);
            if (adverbWeight > 0
                && adverbWeight >= Math.Max(
                    profile.GetWeight(LexiconTag.Adjective),
                    profile.GetWeight(LexiconTag.Noun)))
            {
                return true;
            }
        }

        return word.Length > 4
               && word.EndsWith("ly", StringComparison.OrdinalIgnoreCase)
               && !AdjectiveLyExceptions.Contains(word);
    }

    private static bool IsAdjectiveByLexiconOrMorphology(string word)
    {
        if (CommonNouns.Contains(word) || CommonVerbs.Contains(word))
        {
            return false;
        }

        if (EnglishMorphology.TryGetProfile(word, out var profile))
        {
            var adjectiveWeight = profile.GetWeight(LexiconTag.Adjective);
            return adjectiveWeight > 0
                   && adjectiveWeight >= Math.Max(
                       profile.GetWeight(LexiconTag.Noun),
                       profile.GetWeight(LexiconTag.Adverb))
                   && adjectiveWeight > profile.GetWeight(LexiconTag.Verb);
        }

        foreach (var suffix in AdjectiveSuffixes)
        {
            if (word.Length > suffix.Length + 1 && word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeNounByMorphology(string word)
    {
        foreach (var suffix in NounSuffixes)
        {
            if (word.Length > suffix.Length + 1 && word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsLikelyProperNoun(TextToken token, SpanTokenIndex.TokenRange range, int local)
    {
        if (string.IsNullOrEmpty(token.Text) || !char.IsUpper(token.Text[0]))
        {
            return false;
        }

        return local > 0 || range.Span.Start == token.Span.Start;
    }

    private static string? PreviousContentWordSkippingAdverbs(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local,
        int maxSkips)
    {
        var skipped = 0;
        for (var cursor = local - 1; cursor >= 0; cursor--)
        {
            var word = tokens[range.Start + cursor].Normalized;
            if ((CommonAdverbs.Contains(word) || IsAdverbByLexiconOrMorphology(word)) && skipped < maxSkips)
            {
                skipped++;
                continue;
            }

            return word;
        }

        return null;
    }

    private static bool HasFollowingByPhrase(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local,
        int maxSkips)
    {
        var skipped = 0;
        for (var cursor = local + 1; cursor < range.Count; cursor++)
        {
            var word = tokens[range.Start + cursor].Normalized;
            if (word == "by")
            {
                return true;
            }

            if ((CommonAdverbs.Contains(word) || IsAdverbByLexiconOrMorphology(word)) && skipped < maxSkips)
            {
                skipped++;
                continue;
            }

            return false;
        }

        return false;
    }

    private static bool IsCopularSurface(string word)
        => CopularVerbs.Contains(word) || ContractedBeAuxiliaries.Contains(NormalizeApostrophe(word));

    private static bool IsPassiveAuxiliarySurface(string word)
    {
        var normalized = NormalizeApostrophe(word);
        return normalized is "am" or "is" or "are" or "was" or "were" or "be" or "been" or "being"
               or "get" or "gets" or "got"
               || ContractedBeAuxiliaries.Contains(normalized);
    }

    private static bool IsContractedPronounAuxiliary(string word)
        => ContractedPronounAuxiliaries.Contains(NormalizeApostrophe(word));

    private static bool IsContractedAuxiliaryVerb(string word)
        => ContractedAuxiliaryVerbs.Contains(NormalizeApostrophe(word));

    private static bool TryClassifyContraction(
        string word,
        bool isQuestion,
        out LexicalKind kind)
    {
        var normalized = NormalizeApostrophe(word);
        if (ContractedPronounAuxiliaries.Contains(normalized))
        {
            kind = LexicalKind.Pronoun;
            return true;
        }
        if (ContractedAuxiliaryVerbs.Contains(normalized)
            || normalized.EndsWith("n't", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("n't've", StringComparison.OrdinalIgnoreCase)
            || normalized is "should've" or "could've" or "would've" or "might've"
                or "must've" or "may've" or "ought've" or "needn't've"
                or "let's")
        {
            kind = LexicalKind.Verb;
            return true;
        }

        var apostrophe = normalized.IndexOf((char)0x27);
        if (apostrophe <= 0)
        {
            kind = LexicalKind.Other;
            return false;
        }

        var head = normalized[..apostrophe];
        var suffix = normalized[apostrophe..];
        if (head is "who" or "what" or "where" or "when" or "why" or "how")
        {
            kind = isQuestion ? LexicalKind.Interrogative : LexicalKind.Pronoun;
            return suffix is "'s" or "'d" or "'ll" or "'ve" or "'re";
        }
        if (head is "that" or "this" or "there" or "here")
        {
            kind = LexicalKind.Pronoun;
            return suffix is "'s" or "'d" or "'ll" or "'ve" or "'re";
        }

        kind = LexicalKind.Other;
        return false;
    }

    private static bool IsPossessiveSurface(string word)
    {
        var normalized = NormalizeApostrophe(word);
        return normalized.Length > 2
               && (normalized.EndsWith("'s", StringComparison.Ordinal)
                   || normalized.EndsWith("s'", StringComparison.Ordinal))
               && !ContractedPronounAuxiliaries.Contains(normalized)
               && !ContractedBeAuxiliaries.Contains(normalized);
    }

    private static bool IsAnaphoricOne(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local)
    {
        if (local <= 0)
        {
            return false;
        }
        var previous = tokens[range.Start + local - 1].Normalized;
        var next = local + 1 < range.Count
            ? tokens[range.Start + local + 1].Normalized
            : string.Empty;
        return next != "of"
               && (Determiners.Contains(previous)
                   || CommonAdjectives.Contains(previous)
                   || IsAdjectiveByLexiconOrMorphology(previous)
                   || RawLooksNominal(previous));
    }

    private static bool IsImpersonalOne(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local)
    {
        if (local + 1 >= range.Count)
        {
            return false;
        }
        var next = tokens[range.Start + local + 1].Normalized;
        return ModalVerbs.Contains(next)
               || AuxiliaryVerbs.Contains(next)
               || IsContractedAuxiliaryVerb(next);
    }

    private static bool IsContextualProperNoun(
        TextToken token,
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local)
    {
        if (token.Text.Length <= 1 || !char.IsUpper(token.Text[0]))
        {
            return false;
        }

        var word = token.Normalized;
        if (NumberWords.Contains(word)
            || Determiners.Contains(word)
            || CommonAdjectives.Contains(word)
            || PersonalPronouns.Contains(word) && !token.Text.All(char.IsUpper)
            || InterrogativeWords.Contains(word)
            || Prepositions.Contains(word)
            || CoordinatingConjunctions.Contains(word)
            || SubordinatingConjunctions.Contains(word))
        {
            return false;
        }

        if (token.Text.All(character => !char.IsLetter(character) || char.IsUpper(character))
            || token.Text.Skip(1).Any(char.IsUpper)
            || token.Text.Any(char.IsDigit))
        {
            return true;
        }

        var inflectedVerbShape = word.EndsWith("ed", StringComparison.OrdinalIgnoreCase)
                                 || word.EndsWith("ing", StringComparison.OrdinalIgnoreCase);
        EnglishMorphology.TryGetProfile(word, out var profile);
        var nounWeight = profile.GetWeight(LexiconTag.Noun);
        var adjectiveWeight = profile.GetWeight(LexiconTag.Adjective);

        if (adjectiveWeight > 0
            && adjectiveWeight >= nounWeight
            && local + 1 < range.Count
            && RawLooksNominal(tokens[range.Start + local + 1].Normalized)
            && !ShallowFiniteVerbCandidate(tokens, range, local + 1))
        {
            return false;
        }

        if (local > 0)
        {
            var previousToken = tokens[range.Start + local - 1];
            if (ProperNounTitles.Contains(previousToken.Normalized))
            {
                return true;
            }
            if (!inflectedVerbShape
                && char.IsUpper(previousToken.Text[0])
                && !Determiners.Contains(previousToken.Normalized)
                && !InterrogativeWords.Contains(previousToken.Normalized)
                && (nounWeight > 0 || adjectiveWeight == 0))
            {
                return true;
            }
            if (nounWeight > 0
                && nounWeight >= adjectiveWeight
                && !inflectedVerbShape)
            {
                return true;
            }
            if (!inflectedVerbShape
                && local + 1 < range.Count
                && ShallowFiniteVerbCandidate(tokens, range, local + 1))
            {
                return true;
            }
            return false;
        }

        if (local + 1 < range.Count)
        {
            var nextToken = tokens[range.Start + local + 1];
            if (!inflectedVerbShape
                && char.IsUpper(nextToken.Text[0])
                && !Determiners.Contains(nextToken.Normalized))
            {
                return true;
            }
            if (nounWeight > 0
                && ShallowFiniteVerbCandidate(tokens, range, local + 1))
            {
                return true;
            }
        }
        return false;
    }

    private static string NormalizeApostrophe(string word)
        => word
            .Replace('\u2019', (char)0x27)
            .Replace('\u02BC', (char)0x27);

    private static bool IsNumericToken(string text)
    {
        if (string.IsNullOrEmpty(text) || !char.IsDigit(text[0]))
        {
            return false;
        }

        foreach (var character in text)
        {
            if (char.IsDigit(character) || character is '.' or ',' or ':' or '/' or '-' or '%')
            {
                continue;
            }

            var lower = char.ToLowerInvariant(character);
            if (lower is 's' or 't' or 'n' or 'd' or 'r' or 'h')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static GrammarCategory? LexiconCategory(string word) =>
        EnglishMorphology.DominantCategory(word);

    private static bool IsQuestionSentence(string text, TextSpan span)
    {
        for (var index = span.End - 1; index >= span.Start; index--)
        {
            if (char.IsWhiteSpace(text[index]) || text[index] is '"' or '\'' or '”' or '’' or ')' or ']')
            {
                continue;
            }
            return text[index] == '?';
        }
        return false;
    }

    private static bool ContainsHardClausePunctuation(ReadOnlySpan<char> gap) =>
        gap.IndexOf(';') >= 0 || gap.IndexOf(':') >= 0 || gap.IndexOf('—') >= 0 || gap.IndexOf('\n') >= 0 || gap.IndexOf('\r') >= 0;

    private static bool IsClauseIntroducer(
        string word,
        LexicalKind kind,
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local)
    {
        if (SubordinatingConjunctions.Contains(word))
        {
            return true;
        }

        if (word == "that" && kind is (LexicalKind.Pronoun or LexicalKind.Conjunction))
        {
            return true;
        }

        return word is "who" or "whom" or "whose" or "which" or "where" or "wherever"
               && local > 0
               && PreviousLooksNominal(tokens, range, local);
    }

    private static bool HasVerbAhead(IReadOnlyList<LexicalKind> kinds, int start, int end)
    {
        for (var index = start; index < end; index++)
        {
            if (kinds[index] == LexicalKind.Verb)
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsAuxiliary(string word) => AuxiliaryVerbs.Contains(word) || ModalVerbs.Contains(word);

    private static int FindFirstNoun(IReadOnlyList<LexicalKind> kinds, int start, int end)
    {
        for (var index = start; index < end; index++)
        {
            if (kinds[index] == LexicalKind.Noun)
            {
                return index;
            }
        }
        return -1;
    }

    private static bool ContainsCoordinator(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int start,
        int end)
    {
        for (var local = start; local < end; local++)
        {
            if (CoordinatingConjunctions.Contains(tokens[range.Start + local].Normalized))
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasPrecedingClauseMarker(
        IReadOnlyList<TextToken> tokens,
        SpanTokenIndex.TokenRange range,
        int local)
    {
        var start = Math.Max(0, local - 16);
        var sawEmbeddedPredicate = false;
        for (var cursor = local - 1; cursor >= start; cursor--)
        {
            var word = tokens[range.Start + cursor].Normalized;
            if (word is "that" or "who" or "whom" or "whose" or "which")
            {
                return sawEmbeddedPredicate;
            }
            if (CoordinatingConjunctions.Contains(word))
            {
                return false;
            }
            if (ShallowFiniteVerbCandidate(tokens, range, cursor))
            {
                sawEmbeddedPredicate = true;
            }
        }
        return false;
    }


    private enum LexicalKind
    {
        Other,
        Noun,
        Verb,
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
}
