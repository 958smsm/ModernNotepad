using System.Diagnostics;
using System.Globalization;
using ModernNotepad.Core.Analysis;

var options = BenchmarkOptions.Parse(args);
var corpusPath = options.CorpusPath
                 ?? Path.Combine(AppContext.BaseDirectory, "Data", "en_ewt-ud-test.conllu");
if (!File.Exists(corpusPath))
{
    Console.Error.WriteLine($"Corpus not found: {corpusPath}");
    return 2;
}

var sentences = ConlluReader.Read(corpusPath);
if (sentences.Count == 0)
{
    Console.Error.WriteLine("The benchmark corpus contains no readable sentences.");
    return 2;
}

// Exclude one-time regex/lexicon JIT and static initialization from throughput.
var analyzer = new GrammarColorAnalyzer();
_ = analyzer.Analyze("The quick brown fox jumps over the lazy dog.");

var totals = new AccuracyTotals();
var byTag = new Dictionary<string, AccuracyTotals>(StringComparer.Ordinal);
var accuracyTimer = Stopwatch.StartNew();
foreach (var sentence in sentences)
{
    EvaluateSentence(analyzer, sentence, totals, byTag, options.ShowErrors);
}
accuracyTimer.Stop();

var throughput = MeasureThroughput(analyzer, sentences);
var accuracy = totals.Evaluated == 0 ? 0 : (double)totals.Correct / totals.Evaluated;
var alignment = totals.Eligible == 0 ? 0 : (double)totals.Aligned / totals.Eligible;
var nounRoleAccuracy = totals.RoleEvaluated == 0
    ? 0
    : (double)totals.RoleCorrect / totals.RoleEvaluated;

Console.WriteLine("Traditional Grammar Analyzer — UD English EWT 2.18 test");
Console.WriteLine($"Sentences:             {sentences.Count,10:N0}");
Console.WriteLine($"Gold lexical tokens:   {totals.Eligible,10:N0}");
Console.WriteLine($"Aligned coverage:      {alignment,10:P2}");
Console.WriteLine($"Coarse category:       {accuracy,10:P2} ({totals.Correct:N0}/{totals.Evaluated:N0})");
Console.WriteLine($"Noun role accuracy:    {nounRoleAccuracy,10:P2} ({totals.RoleCorrect:N0}/{totals.RoleEvaluated:N0})");
Console.WriteLine($"Accuracy pass time:    {accuracyTimer.Elapsed.TotalMilliseconds,10:N0} ms");
Console.WriteLine($"Sustained throughput:  {throughput.TokensPerSecond,10:N0} tokens/s");
Console.WriteLine($"Throughput corpus:     {throughput.TokenCount,10:N0} tokens × {throughput.Iterations}");
Console.WriteLine();
Console.WriteLine("UPOS        accuracy       correct/evaluated");
foreach (var (tag, tagTotals) in byTag.OrderBy(pair => pair.Key, StringComparer.Ordinal))
{
    var tagAccuracy = tagTotals.Evaluated == 0
        ? 0
        : (double)tagTotals.Correct / tagTotals.Evaluated;
    Console.WriteLine($"{tag,-8} {tagAccuracy,12:P2}   {tagTotals.Correct,7:N0}/{tagTotals.Evaluated,-7:N0}");
}
Console.WriteLine();
Console.WriteLine("Most frequent confusions");
foreach (var (confusion, count) in totals.Confusions
             .OrderByDescending(pair => pair.Value)
             .ThenBy(pair => pair.Key, StringComparer.Ordinal)
             .Take(20))
{
    Console.WriteLine($"{confusion,-30} {count,7:N0}");
}
Console.WriteLine();
Console.WriteLine("Most frequent error forms");
foreach (var (form, count) in totals.ErrorForms
             .OrderByDescending(pair => pair.Value)
             .ThenBy(pair => pair.Key, StringComparer.Ordinal)
             .Take(25))
{
    Console.WriteLine($"{form,-48} {count,7:N0}");
}
if (totals.Mistakes.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Sample errors");
    foreach (var mistake in totals.Mistakes)
    {
        Console.WriteLine(mistake);
    }
}

var failed = false;
if (options.MinimumAccuracy is { } minimumAccuracy && accuracy < minimumAccuracy)
{
    Console.Error.WriteLine(
        $"Accuracy {accuracy:P2} is below the required {minimumAccuracy:P2}.");
    failed = true;
}
if (options.MinimumCoverage is { } minimumCoverage && alignment < minimumCoverage)
{
    Console.Error.WriteLine(
        $"Alignment coverage {alignment:P2} is below the required {minimumCoverage:P2}.");
    failed = true;
}
if (options.MinimumTokensPerSecond is { } minimumThroughput
    && throughput.TokensPerSecond < minimumThroughput)
{
    Console.Error.WriteLine(
        $"Throughput {throughput.TokensPerSecond:N0} tokens/s is below the required " +
        $"{minimumThroughput:N0} tokens/s.");
    failed = true;
}
return failed ? 1 : 0;

static void EvaluateSentence(
    GrammarColorAnalyzer analyzer,
    GoldSentence sentence,
    AccuracyTotals totals,
    IDictionary<string, AccuracyTotals> byTag,
    int errorLimit)
{
    var eligible = sentence.Tokens.Where(IsEligible).ToArray();
    totals.Eligible += eligible.Length;
    if (eligible.Length == 0)
    {
        return;
    }

    var tokens = TextTokenizer.Tokenize(sentence.Text);
    var analysis = analyzer.Analyze(sentence.Text, tokens);
    var categories = analysis.Spans.ToDictionary(span => span.Span.Start, span => span.Category);
    var goldIndex = 0;

    foreach (var token in tokens)
    {
        var match = FindMatch(eligible, goldIndex, token.Text);
        if (match < 0)
        {
            continue;
        }
        goldIndex = match + 1;
        var gold = eligible[match];
        totals.Aligned++;
        if (!categories.TryGetValue(token.Span.Start, out var predicted))
        {
            continue;
        }

        totals.Evaluated++;
        var tagTotals = GetTagTotals(byTag, gold.Upostag);
        tagTotals.Evaluated++;
        if (IsCoarseCategoryCorrect(gold, predicted))
        {
            totals.Correct++;
            tagTotals.Correct++;
        }
        else
        {
            var confusion = $"{gold.Upostag} -> {predicted}";
            totals.Confusions[confusion] = totals.Confusions.GetValueOrDefault(confusion) + 1;
            var errorForm = $"{gold.Upostag}:{gold.Form.ToLowerInvariant()} -> {predicted}";
            totals.ErrorForms[errorForm] = totals.ErrorForms.GetValueOrDefault(errorForm) + 1;
            if (totals.Mistakes.Count < errorLimit)
            {
                totals.Mistakes.Add(
                    $"{gold.Upostag,-6} {predicted,-12} {gold.Form,-18} {sentence.Text}");
            }
        }

        if (TryGetNounRole(gold, out var expectedSubject))
        {
            totals.RoleEvaluated++;
            var predictedSubject = predicted == GrammarCategory.SubjectNoun;
            if (predictedSubject == expectedSubject)
            {
                totals.RoleCorrect++;
            }
        }
    }
}

static int FindMatch(IReadOnlyList<GoldToken> gold, int start, string surface)
{
    var end = Math.Min(gold.Count, start + 5);
    for (var index = start; index < end; index++)
    {
        if (Normalize(gold[index].Form).Equals(
                Normalize(surface),
                StringComparison.OrdinalIgnoreCase))
        {
            return index;
        }
    }
    return -1;
}

static string Normalize(string value) =>
    value.Replace('’', (char)0x27).Replace('ʼ', (char)0x27);

static bool IsEligible(GoldToken token)
{
    if (token.Upostag is "PUNCT" or "SYM" or "X" or "INTJ")
    {
        return false;
    }
    var pieces = TextTokenizer.Tokenize(token.Form);
    return pieces.Count == 1
           && pieces[0].Text.Equals(token.Form, StringComparison.OrdinalIgnoreCase);
}

static bool IsCoarseCategoryCorrect(GoldToken gold, GrammarCategory predicted) =>
    gold.Upostag switch
    {
        "NOUN" or "PROPN" => predicted is GrammarCategory.SubjectNoun
            or GrammarCategory.ObjectNoun
            || predicted == GrammarCategory.Pronoun
               && gold.Form is "one" or "ones",
        "VERB" or "AUX" => predicted == GrammarCategory.Verb,
        "ADJ" => predicted == GrammarCategory.Adjective
                 || predicted == GrammarCategory.Quantifier
                    && IsTraditionalQuantifier(gold.Form)
                 || predicted == GrammarCategory.Determiner
                    && gold.Form.ToLowerInvariant() is "other" or "such",
        "ADV" when gold.Features.Contains("PronType=Int", StringComparison.Ordinal) =>
            predicted is GrammarCategory.Interrogative or GrammarCategory.Adverb,
        "ADV" when gold.Features.Contains("PronType=Rel", StringComparison.Ordinal) =>
            predicted is GrammarCategory.Conjunction or GrammarCategory.Adverb,
        "ADV" => predicted == GrammarCategory.Adverb
                 || predicted == GrammarCategory.Quantifier
                    && IsTraditionalQuantifier(gold.Form),
        "PRON" when gold.Features.Contains("PronType=Int", StringComparison.Ordinal) =>
            predicted is GrammarCategory.Interrogative or GrammarCategory.Pronoun,
        "PRON" when gold.Features.Contains("Poss=Yes", StringComparison.Ordinal) =>
            predicted is GrammarCategory.Pronoun or GrammarCategory.Determiner,
        "PRON" => predicted == GrammarCategory.Pronoun
                  || predicted == GrammarCategory.Adverb
                     && gold.Form.Equals("there", StringComparison.OrdinalIgnoreCase),
        "DET" => predicted is GrammarCategory.Determiner or GrammarCategory.Quantifier,
        "ADP" when gold.Relation.Contains("compound:prt", StringComparison.Ordinal) =>
            predicted == GrammarCategory.Particle,
        "ADP" => predicted == GrammarCategory.Preposition,
        "CCONJ" or "SCONJ" => predicted == GrammarCategory.Conjunction,
        "PART" when gold.Form.Equals("not", StringComparison.OrdinalIgnoreCase)
                    || gold.Form.Equals("n't", StringComparison.OrdinalIgnoreCase) =>
            predicted is GrammarCategory.Adverb or GrammarCategory.Particle,
        "PART" => predicted == GrammarCategory.Particle,
        "NUM" => predicted == GrammarCategory.Quantifier,
        _ => false
    };

static bool IsTraditionalQuantifier(string word) =>
    word.ToLowerInvariant() is "all" or "any" or "both" or "each" or "either"
        or "enough" or "every" or "few" or "fewer" or "fewest" or "less"
        or "least" or "little" or "many" or "more" or "most" or "much"
        or "neither" or "no" or "several" or "some";

static bool TryGetNounRole(GoldToken token, out bool subject)
{
    subject = false;
    if (token.Upostag is not ("NOUN" or "PROPN"))
    {
        return false;
    }
    if (token.Relation.StartsWith("nsubj", StringComparison.Ordinal)
        || token.Relation.StartsWith("csubj", StringComparison.Ordinal))
    {
        subject = true;
        return true;
    }
    if (token.Relation is "obj" or "iobj"
        || token.Relation.StartsWith("obl", StringComparison.Ordinal)
        || token.Relation.StartsWith("nmod", StringComparison.Ordinal))
    {
        return true;
    }
    return false;
}

static AccuracyTotals GetTagTotals(
    IDictionary<string, AccuracyTotals> totals,
    string upostag)
{
    if (!totals.TryGetValue(upostag, out var value))
    {
        value = new AccuracyTotals();
        totals.Add(upostag, value);
    }
    return value;
}

static ThroughputResult MeasureThroughput(
    GrammarColorAnalyzer analyzer,
    IReadOnlyList<GoldSentence> sentences)
{
    var text = string.Join(Environment.NewLine, sentences.Select(sentence => sentence.Text));
    var tokens = TextTokenizer.Tokenize(text);
    var spans = TextSegmentation.GetSentences(text);
    var iterations = Math.Max(3, 150_000 / Math.Max(1, tokens.Count));
    var timer = Stopwatch.StartNew();
    for (var iteration = 0; iteration < iterations; iteration++)
    {
        _ = analyzer.Analyze(text, tokens, spans);
    }
    timer.Stop();
    return new ThroughputResult(
        tokens.Count,
        iterations,
        tokens.Count * iterations / Math.Max(0.001, timer.Elapsed.TotalSeconds));
}

internal sealed class AccuracyTotals
{
    public int Eligible { get; set; }
    public int Aligned { get; set; }
    public int Evaluated { get; set; }
    public int Correct { get; set; }
    public int RoleEvaluated { get; set; }
    public int RoleCorrect { get; set; }
    public Dictionary<string, int> Confusions { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> ErrorForms { get; } = new(StringComparer.Ordinal);
    public List<string> Mistakes { get; } = [];
}

internal sealed record ThroughputResult(
    int TokenCount,
    int Iterations,
    double TokensPerSecond);

internal sealed record GoldToken(
    string Form,
    string Upostag,
    string Features,
    string Relation);

internal sealed record GoldSentence(string Text, IReadOnlyList<GoldToken> Tokens);

internal sealed record ConlluRow(
    string Id,
    string Form,
    string Upostag,
    string Features,
    string Relation)
{
    public int? IntegerId =>
        int.TryParse(Id, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}

internal static class ConlluReader
{
    public static IReadOnlyList<GoldSentence> Read(string path)
    {
        var sentences = new List<GoldSentence>();
        var rows = new List<ConlluRow>();
        string? text = null;
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("# text = ", StringComparison.Ordinal))
            {
                text = line[9..];
                continue;
            }
            if (line.Length == 0)
            {
                AddSentence(sentences, text, rows);
                rows.Clear();
                text = null;
                continue;
            }
            if (line[0] == '#')
            {
                continue;
            }
            var columns = line.Split('	');
            if (columns.Length >= 8 && !columns[0].Contains('.'))
            {
                rows.Add(new ConlluRow(
                    columns[0],
                    columns[1],
                    columns[3],
                    columns[5],
                    columns[7]));
            }
        }
        AddSentence(sentences, text, rows);
        return sentences;
    }

    private static void AddSentence(
        ICollection<GoldSentence> sentences,
        string? text,
        IReadOnlyList<ConlluRow> rows)
    {
        if (string.IsNullOrWhiteSpace(text) || rows.Count == 0)
        {
            return;
        }
        var tokens = new List<GoldToken>();
        var consumed = new HashSet<int>();
        foreach (var row in rows)
        {
            if (row.Id.Contains('-'))
            {
                var bounds = row.Id.Split('-', 2);
                if (!int.TryParse(bounds[0], out var start)
                    || !int.TryParse(bounds[1], out var end))
                {
                    continue;
                }
                var parts = rows
                    .Where(candidate =>
                        candidate.IntegerId is { } id && id >= start && id <= end)
                    .ToArray();
                foreach (var part in parts)
                {
                    consumed.Add(part.IntegerId!.Value);
                }
                var representative = parts.FirstOrDefault(
                    part => part.Upostag is not ("PART" or "PUNCT"))
                    ?? parts.FirstOrDefault();
                if (representative is not null)
                {
                    tokens.Add(new GoldToken(
                        row.Form,
                        representative.Upostag,
                        representative.Features,
                        representative.Relation));
                }
                continue;
            }
            if (row.IntegerId is not { } integerId || consumed.Contains(integerId))
            {
                continue;
            }
            tokens.Add(new GoldToken(row.Form, row.Upostag, row.Features, row.Relation));
        }
        sentences.Add(new GoldSentence(text, tokens));
    }
}

internal sealed record BenchmarkOptions(
    string? CorpusPath,
    double? MinimumAccuracy,
    double? MinimumCoverage,
    double? MinimumTokensPerSecond,
    int ShowErrors)
{
    public static BenchmarkOptions Parse(IReadOnlyList<string> arguments)
    {
        string? corpus = null;
        double? accuracy = null;
        double? coverage = null;
        double? throughput = null;
        var showErrors = 0;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument == "--corpus" && ++index < arguments.Count)
            {
                corpus = arguments[index];
            }
            else if (argument == "--minimum-accuracy" && ++index < arguments.Count)
            {
                accuracy = ParseRatio(arguments[index], argument);
            }
            else if (argument == "--minimum-coverage" && ++index < arguments.Count)
            {
                coverage = ParseRatio(arguments[index], argument);
            }
            else if (argument == "--minimum-throughput" && ++index < arguments.Count)
            {
                throughput = double.Parse(arguments[index], CultureInfo.InvariantCulture);
            }
            else if (argument == "--show-errors" && ++index < arguments.Count)
            {
                showErrors = int.Parse(arguments[index], CultureInfo.InvariantCulture);
            }
            else
            {
                throw new ArgumentException($"Unknown or incomplete benchmark option: {argument}");
            }
        }
        return new BenchmarkOptions(corpus, accuracy, coverage, throughput, showErrors);
    }

    private static double ParseRatio(string value, string option)
    {
        var ratio = double.Parse(value, CultureInfo.InvariantCulture);
        if (ratio is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(option, "Expected a ratio from 0 to 1.");
        }
        return ratio;
    }
}
