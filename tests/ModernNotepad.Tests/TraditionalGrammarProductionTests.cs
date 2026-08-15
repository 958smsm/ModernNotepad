using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModernNotepad.Core.Analysis;

namespace ModernNotepad.Tests;

[TestClass]
public sealed class TraditionalGrammarProductionTests
{
    [TestMethod]
    public void Lexicon_ContainsMoreThanOneHundredThousandOfflineEntries()
    {
        Assert.AreEqual(GrammarLexicon.GeneratedWordCount, GrammarLexicon.Lexicon.Count);
        Assert.IsTrue(GrammarLexicon.Lexicon.Count >= 100_000);

        foreach (var word in new[]
                 {
                     "abecedarian", "geophyte", "lexicographer", "sesquipedalian", "zymurgy"
                 })
        {
            Assert.IsTrue(GrammarLexicon.Lexicon.ContainsKey(word),
                $"The generated lexicon should contain '{word}'.");
        }
    }

    [TestMethod]
    public void Analyze_UsesContextForWordsThatCanBeNounsOrVerbs()
    {
        const string text = "They record data. The record contains data. " +
                            "They object loudly. The object moved. " +
                            "They project results. The project succeeded.";
        var result = Analyze(text);

        AssertCategory(result, "record", 0, GrammarCategory.Verb);
        AssertCategory(result, "record", 1, GrammarCategory.SubjectNoun);
        AssertCategory(result, "object", 0, GrammarCategory.Verb);
        AssertCategory(result, "object", 1, GrammarCategory.SubjectNoun);
        AssertCategory(result, "project", 0, GrammarCategory.Verb);
        AssertCategory(result, "project", 1, GrammarCategory.SubjectNoun);
    }

    [TestMethod]
    public void Analyze_ResolvesRegularAndIrregularInflections()
    {
        const string text = "The lexicographer catalogues geophytes. " +
                            "The children went and wrote analyses.";
        var result = Analyze(text);

        AssertCategory(result, "lexicographer", 0, GrammarCategory.SubjectNoun);
        AssertCategory(result, "catalogues", 0, GrammarCategory.Verb);
        AssertCategory(result, "geophytes", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "children", 0, GrammarCategory.SubjectNoun);
        AssertCategory(result, "went", 0, GrammarCategory.Verb);
        AssertCategory(result, "wrote", 0, GrammarCategory.Verb);
        AssertCategory(result, "analyses", 0, GrammarCategory.ObjectNoun);
    }

    [TestMethod]
    public void Analyze_HandlesAsciiCurlyAndStackedContractions()
    {
        const string text = "I\u2019m ready. We\u02BCre prepared. They shouldn't've left. " +
                            "Who's calling? Google's model works. Let's begin.";
        var result = Analyze(text);

        AssertCategory(result, "I\u2019m", 0, GrammarCategory.Pronoun);
        AssertCategory(result, "We\u02BCre", 0, GrammarCategory.Pronoun);
        AssertCategory(result, "shouldn't've", 0, GrammarCategory.Verb);
        AssertCategory(result, "Who's", 0, GrammarCategory.Interrogative);
        AssertCategory(result, "Google's", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "Let's", 0, GrammarCategory.Verb);
    }

    [TestMethod]
    public void Analyze_DistinguishesAmbiguousFunctionWordsFromClauseContext()
    {
        const string text = "I waited for the train, for it was late. " +
                            "We work so quickly, so we finish early. " +
                            "After the meeting, we left. After the rain stopped, we left.";
        var result = Analyze(text);

        AssertCategory(result, "for", 0, GrammarCategory.Preposition);
        AssertCategory(result, "for", 1, GrammarCategory.Conjunction);
        AssertCategory(result, "so", 0, GrammarCategory.Adverb);
        AssertCategory(result, "so", 1, GrammarCategory.Conjunction);
        AssertCategory(result, "After", 0, GrammarCategory.Preposition);
        AssertCategory(result, "After", 1, GrammarCategory.Conjunction);
    }

    [TestMethod]
    public void Analyze_DistinguishesInfinitivalToFromPrepositionalToAcrossAdverbs()
    {
        const string text = "We plan to carefully project results. We walked to school.";
        var result = Analyze(text);

        AssertCategory(result, "to", 0, GrammarCategory.Particle);
        AssertCategory(result, "project", 0, GrammarCategory.Verb);
        AssertCategory(result, "to", 1, GrammarCategory.Preposition);
    }

    [TestMethod]
    public void Analyze_RecognizesProperNamesAcronymsAndTitleCaseAmbiguities()
    {
        const string text = "Dr. Zorblax visited NeoCairo. NASA validated QXParser. " +
                            "May works in Paris. Will Smith writes.";
        var result = Analyze(text);

        AssertCategory(result, "Zorblax", 0, GrammarCategory.SubjectNoun);
        AssertCategory(result, "NeoCairo", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "NASA", 0, GrammarCategory.SubjectNoun);
        AssertCategory(result, "QXParser", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "May", 0, GrammarCategory.SubjectNoun);
        AssertCategory(result, "Will", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "Smith", 0, GrammarCategory.SubjectNoun);
        AssertCategory(result, "Paris", 0, GrammarCategory.ObjectNoun);
    }

    [TestMethod]
    public void Analyze_ClassifiesUnknownWordsByShapeMorphologyAndSyntax()
    {
        const string text = "The quizzacious engine transmogrified widgets florptastically. " +
                            "Zorblax recalibrates nanogadgets.";
        var result = Analyze(text);

        AssertCategory(result, "quizzacious", 0, GrammarCategory.Adjective);
        AssertCategory(result, "engine", 0, GrammarCategory.SubjectNoun);
        AssertCategory(result, "transmogrified", 0, GrammarCategory.Verb);
        AssertCategory(result, "widgets", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "florptastically", 0, GrammarCategory.Adverb);
        AssertCategory(result, "Zorblax", 0, GrammarCategory.SubjectNoun);
        AssertCategory(result, "recalibrates", 0, GrammarCategory.Verb);
        AssertCategory(result, "nanogadgets", 0, GrammarCategory.ObjectNoun);
    }

    [TestMethod]
    public void Analyze_TreatsNounModifiersAndSeparablePhrasalParticlesContextually()
    {
        const string text = "She implemented a deep learning model. " +
                            "They carefully looked the identifier up.";
        var result = Analyze(text);

        AssertCategory(result, "deep", 0, GrammarCategory.Adjective);
        AssertCategory(result, "learning", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "model", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "looked", 0, GrammarCategory.Verb);
        AssertCategory(result, "up", 0, GrammarCategory.Particle);
    }

    [TestMethod]
    public void Analyze_CorrectsTheDeepLearningInterviewPrompt()
    {
        const string text =
            "Can you describe a project where you implemented a deep learning model " +
            "(e.g., CNN, RNN) to solve a problem?";
        var result = Analyze(text);

        AssertCategory(result, "Can", 0, GrammarCategory.Verb);
        AssertCategory(result, "project", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "where", 0, GrammarCategory.Conjunction);
        AssertCategory(result, "implemented", 0, GrammarCategory.Verb);
        AssertCategory(result, "deep", 0, GrammarCategory.Adjective);
        AssertCategory(result, "learning", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "model", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "e.g.", 0, GrammarCategory.Adverb);
        AssertCategory(result, "CNN", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "RNN", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "to", 0, GrammarCategory.Particle);
        AssertCategory(result, "solve", 0, GrammarCategory.Verb);
        AssertCategory(result, "problem", 0, GrammarCategory.ObjectNoun);
    }

    [TestMethod]
    public void Analyze_CorrectsTheLlmValidationInterviewPrompt()
    {
        const string text =
            "In this job, you won't be working on a project. You'll be interacting and " +
            "validating the output of an LLM (asking questions based on an open source " +
            "data set and then verifying and providing feedback on the response you " +
            "received). Are you comfortable with this type of job? Why do you believe " +
            "you would be a good fit";
        var result = Analyze(text);

        AssertCategory(result, "You'll", 0, GrammarCategory.Pronoun);
        AssertCategory(result, "validating", 0, GrammarCategory.Verb);
        AssertCategory(result, "output", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "LLM", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "questions", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "open", 0, GrammarCategory.Adjective);
        AssertCategory(result, "source", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "data", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "set", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "verifying", 0, GrammarCategory.Verb);
        AssertCategory(result, "providing", 0, GrammarCategory.Verb);
        AssertCategory(result, "feedback", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "received", 0, GrammarCategory.Verb);
        AssertCategory(result, "Why", 0, GrammarCategory.Interrogative);
    }

    [TestMethod]
    public void Tokenize_KeepsDottedInitialismsTogether()
    {
        var tokens = TextTokenizer.Tokenize("Use e.g., CNN models in the U.S. market.");

        CollectionAssert.Contains(tokens.Select(token => token.Text).ToList(), "e.g.");
        CollectionAssert.Contains(tokens.Select(token => token.Text).ToList(), "U.S.");
    }

    private static AnalysisResult Analyze(string text)
    {
        var tokens = TextTokenizer.Tokenize(text);
        var sentences = TextSegmentation.GetSentences(text);
        var analysis = new GrammarColorAnalyzer().Analyze(text, tokens, sentences);
        var categoryByStart = analysis.Spans.ToDictionary(span => span.Span.Start, span => span.Category);
        return new AnalysisResult(tokens, categoryByStart);
    }

    private static void AssertCategory(
        AnalysisResult result,
        string tokenText,
        int occurrence,
        GrammarCategory expected)
    {
        var matches = result.Tokens
            .Where(token => token.Text.Equals(tokenText, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.IsTrue(matches.Length > occurrence,
            $"Token '{tokenText}' occurrence {occurrence} was not found.");

        var token = matches[occurrence];
        var actual = result.CategoryByStart.TryGetValue(token.Span.Start, out var category)
            ? category
            : GrammarCategory.Other;
        var diagnostic = string.Join(
            ", ",
            result.Tokens.Select(candidate =>
                $"{candidate.Text}:{(result.CategoryByStart.TryGetValue(candidate.Span.Start, out var value) ? value : GrammarCategory.Other)}"));
        Assert.AreEqual(expected, actual,
            $"Unexpected category for '{token.Text}' at character {token.Span.Start}. Analysis: {diagnostic}");
    }

    private sealed record AnalysisResult(
        IReadOnlyList<TextToken> Tokens,
        IReadOnlyDictionary<int, GrammarCategory> CategoryByStart);
}
