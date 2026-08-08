namespace ModernNotepad.Core.Analysis;

public sealed class GrammarColorAnalyzer
{
    private static readonly HashSet<string> Interrogatives = new(StringComparer.OrdinalIgnoreCase)
    {
        "who", "whom", "what", "which", "whose", "where", "when", "why", "how"
    };

    private static readonly HashSet<string> Pronouns = new(StringComparer.OrdinalIgnoreCase)
    {
        "i", "me", "my", "mine", "myself", "you", "your", "yours", "yourself",
        "he", "him", "his", "himself", "she", "her", "hers", "herself", "it", "its", "itself",
        "we", "us", "our", "ours", "ourselves", "they", "them", "their", "theirs", "themselves",
        "that", "this", "these", "those"
    };

    private static readonly HashSet<string> Prepositions = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "above", "across", "after", "against", "along", "among", "around", "at", "before",
        "behind", "below", "beneath", "beside", "between", "beyond", "by", "despite", "during", "for",
        "from", "in", "inside", "into", "like", "near", "of", "off", "on", "onto", "out", "outside",
        "over", "past", "since", "through", "throughout", "to", "toward", "under", "until", "up", "upon",
        "with", "within", "without"
    };

    private static readonly HashSet<string> Conjunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "but", "or", "nor", "for", "yet", "so", "although", "because", "since", "unless",
        "while", "whereas", "if", "when", "whenever", "where", "wherever", "whether", "than", "though"
    };

    private static readonly HashSet<string> Determiners = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "some", "any", "each", "every", "either", "neither", "many", "much", "few",
        "little", "several", "all", "both", "no", "another"
    };

    private static readonly HashSet<string> CommonNouns = new(StringComparer.OrdinalIgnoreCase)
    {
        "processing", "tracking", "understanding", "monitoring", "versioning", "scaling",
        "building", "testing", "meeting", "training", "planning", "setting", "meaning",
        "marketing", "engineering", "coding", "programming", "learning", "drawing", "painting",
        "feeling", "finding", "recording", "showing", "warning", "opening", "closing",
        "configuration", "isolation", "detection", "logic"
    };

    private static readonly HashSet<string> CommonVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "am", "is", "are", "was", "were", "be", "being", "been", "have", "has", "had", "do", "does", "did",
        "can", "could", "shall", "should", "will", "would", "may", "might", "must", "go", "goes", "went", "gone",
        "make", "makes", "made", "say", "says", "said", "see", "sees", "saw", "seen", "know", "knows", "knew",
        "known", "think", "thinks", "thought", "take", "takes", "took", "taken", "come", "comes", "came", "want",
        "wants", "use", "uses", "used", "find", "finds", "found", "give", "gives", "gave", "given", "tell", "tells",
        "told", "work", "works", "worked", "call", "calls", "called", "try", "tries", "tried", "ask", "asks", "asked",
        "need", "needs", "needed", "feel", "feels", "felt", "become", "becomes", "became", "leave", "leaves", "left",
        "put", "keep", "keeps", "kept", "let", "begin", "begins", "began", "started", "start", "starts", "show",
        "shows", "showed", "shown", "hear", "hears", "heard", "play", "plays", "played", "run", "runs", "ran",
        "move", "moves", "moved", "live", "lives", "lived", "believe", "believes", "believed", "bring", "brings",
        "brought", "write", "writes", "wrote", "written", "provide", "provides", "provided", "sit", "sits", "sat",
        "stand", "stands", "stood", "lose", "loses", "lost", "pay", "pays", "paid", "meet", "meets", "met",
        "include", "includes", "included", "continue", "continues", "continued", "set", "learn", "learns", "learned",
        "change", "changes", "changed", "lead", "leads", "led", "understand", "understands", "understood", "watch",
        "watches", "watched", "follow", "follows", "followed", "stop", "stops", "stopped", "create", "creates",
        "created", "speak", "speaks", "spoke", "spoken", "read", "allow", "allows", "allowed", "add", "adds", "added",
        "separate", "separates", "separated", "update", "updates", "updated", "delete", "deletes", "deleted",
        "remove", "removes", "removed", "save", "saves", "saved", "open", "opens", "opened", "close", "closes", "closed",
        "format", "formats", "formatted", "check", "checks", "checked", "select", "selects", "selected",
        "copy", "copies", "copied", "paste", "pastes", "pasted", "cut", "cuts", "replace", "replaces", "replaced",
        "insert", "inserts", "inserted", "view", "views", "viewed", "help", "helps", "helped",
        "build", "builds", "built", "compile", "compiles", "compiled", "execute", "executes", "executed",
        "test", "tests", "tested", "debug", "debugs", "debugged", "deploy", "deploys", "deployed",
        "generate", "generates", "generated", "analyze", "analyzes", "analyzed", "parse", "parses", "parsed",
        "process", "processes", "processed", "evaluate", "evaluates", "evaluated", "configure", "configures", "configured",
        "ingest", "ingests", "ingested", "handle", "handles", "handled", "share", "shares", "shared", 
        "maintain", "maintains", "maintained", "store", "stores", "stored", "deliver", "delivers", "delivered",
        "produce", "produces", "produced", "convert", "converts", "converted"
    };

    private static readonly HashSet<string> CommonAdjectives = new(StringComparer.OrdinalIgnoreCase)
    {
        "good", "new", "first", "last", "long", "great", "little", "own", "other", "old", "right", "big", "high",
        "different", "small", "large", "next", "early", "young", "important", "few", "public", "bad", "same", "able",
        "clear", "simple", "strong", "possible", "available", "local", "recent", "modern", "fast", "lightweight",
        "current", "previous", "true", "false", "valid", "invalid", "empty", "full", "static", "dynamic", "private",
        "specific", "basic", "active", "inactive", "visible", "hidden", "raw", "scalable", "stream-processing",
        "customer-specific", "production-ready", "central", "semantic"
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
        var processedStarts = new HashSet<int>();
        var counts = Enum.GetValues<GrammarCategory>()
            .ToDictionary(category => category, _ => 0);

        foreach (var sentence in sentences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sentenceTokens = tokens
                .Where(token => token.Span.Start >= sentence.Start && token.Span.End <= sentence.End)
                .ToArray();
            
            if (sentenceTokens.Length == 0)
            {
                continue;
            }

            for (int i = 0; i < sentenceTokens.Length; i++)
            {
                var token = sentenceTokens[i];
                processedStarts.Add(token.Span.Start);
                var category = ClassifyToken(i, sentenceTokens);
                counts[category]++;
                if (category != GrammarCategory.Other)
                {
                    spans.Add(new ColoredSpan(token.Span, category));
                }
            }
        }

        foreach (var token in tokens)
        {
            if (processedStarts.Contains(token.Span.Start))
            {
                continue;
            }

            var category = ClassifyTokenSimple(token.Normalized);
            counts[category]++;
            if (category != GrammarCategory.Other)
            {
                spans.Add(new ColoredSpan(token.Span, category));
            }
        }

        return new GrammarAnalysis(spans, counts);
    }

    private static GrammarCategory ClassifyToken(int index, TextToken[] sentenceTokens)
    {
        var word = sentenceTokens[index].Normalized;

        if (Determiners.Contains(word)) return GrammarCategory.Quantifier;
        if (Interrogatives.Contains(word)) return GrammarCategory.Interrogative;
        if (Pronouns.Contains(word)) return GrammarCategory.Pronoun;
        if (Conjunctions.Contains(word)) return GrammarCategory.Conjunction;
        if (Prepositions.Contains(word)) return GrammarCategory.Preposition;
        if (IsAdverb(word)) return GrammarCategory.Adverb;
        if (IsVerb(word)) return GrammarCategory.Verb;
        if (IsAdjective(word)) return GrammarCategory.Adjective;

        return ClassifyNounInContext(index, sentenceTokens);
    }

    private static GrammarCategory ClassifyTokenSimple(string word)
    {
        if (Determiners.Contains(word)) return GrammarCategory.Quantifier;
        if (Interrogatives.Contains(word)) return GrammarCategory.Interrogative;
        if (Pronouns.Contains(word)) return GrammarCategory.Pronoun;
        if (Conjunctions.Contains(word)) return GrammarCategory.Conjunction;
        if (Prepositions.Contains(word)) return GrammarCategory.Preposition;
        if (IsAdverb(word)) return GrammarCategory.Adverb;
        if (IsVerb(word)) return GrammarCategory.Verb;
        if (IsAdjective(word)) return GrammarCategory.Adjective;
        return GrammarCategory.ObjectNoun;
    }

    private static GrammarCategory ClassifyNounInContext(int index, TextToken[] sentenceTokens)
    {
        var verbs = new List<int>();
        for (int i = 0; i < sentenceTokens.Length; i++)
        {
            if (IsVerb(sentenceTokens[i].Normalized)) verbs.Add(i);
        }

        if (verbs.Count == 0) return GrammarCategory.ObjectNoun;

        int ownerVerbIndex = -1;
        int minDistance = int.MaxValue;

        foreach (var vIndex in verbs)
        {
            int dist = 0;
            int start = Math.Min(index, vIndex) + 1;
            int end = Math.Max(index, vIndex);

            for (int i = start; i < end; i++)
            {
                dist++;
                var norm = sentenceTokens[i].Normalized;
                if (norm is "," or ";" or "." || Conjunctions.Contains(norm))
                {
                    dist += 10;
                }
            }

            if (dist < minDistance)
            {
                minDistance = dist;
                ownerVerbIndex = vIndex;
            }
        }

        bool isSubjectPosition = index < ownerVerbIndex;
        if (!isSubjectPosition) return GrammarCategory.ObjectNoun;

        bool isHeadNoun = true;
        for (int i = index + 1; i < ownerVerbIndex; i++)
        {
            var norm = sentenceTokens[i].Normalized;
            if (norm is "," or ";" or "." || Conjunctions.Contains(norm))
            {
                break;
            }

            if (IsNoun(norm))
            {
                isHeadNoun = false;
                break;
            }
        }

        return isHeadNoun ? GrammarCategory.SubjectNoun : GrammarCategory.ObjectNoun;
    }

    private static bool IsNoun(string word)
    {
        return !Determiners.Contains(word)
            && !Interrogatives.Contains(word)
            && !Pronouns.Contains(word)
            && !Conjunctions.Contains(word)
            && !Prepositions.Contains(word)
            && !IsAdverb(word)
            && !IsVerb(word)
            && !IsAdjective(word);
    }

    private static bool IsVerb(string word)
    {
        if (CommonNouns.Contains(word) || CommonAdjectives.Contains(word)) return false;

        return CommonVerbs.Contains(word)
            || (word.Length > 4 && (word.EndsWith("ing", StringComparison.OrdinalIgnoreCase)
                || word.EndsWith("ed", StringComparison.OrdinalIgnoreCase)
                || word.EndsWith("ize", StringComparison.OrdinalIgnoreCase)
                || word.EndsWith("ise", StringComparison.OrdinalIgnoreCase)
                || word.EndsWith("ify", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsAdverb(string word)
    {
        return word is "very" or "quite" or "rather" or "often" or "always" or "never" or "soon" or "well" or "too" or "then" or "now" or "instead"
            || (word.Length > 4 && word.EndsWith("ly", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAdjective(string word)
    {
        if (CommonNouns.Contains(word) || CommonVerbs.Contains(word)) return false;

        return CommonAdjectives.Contains(word)
            || (word.Length > 4 && (word.EndsWith("ous", StringComparison.OrdinalIgnoreCase)
                || word.EndsWith("ful", StringComparison.OrdinalIgnoreCase)
                || word.EndsWith("less", StringComparison.OrdinalIgnoreCase)
                || word.EndsWith("able", StringComparison.OrdinalIgnoreCase)
                || word.EndsWith("ible", StringComparison.OrdinalIgnoreCase)
                || word.EndsWith("ive", StringComparison.OrdinalIgnoreCase)
                || word.EndsWith("al", StringComparison.OrdinalIgnoreCase)));
    }
}
