using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModernNotepad.Core.Analysis;
using ModernNotepad.Core.Models;

namespace ModernNotepad.Tests;

[TestClass]
public sealed class TraditionalGrammarAnalyzerTests
{
    [TestMethod]
    public void Analyze_DisambiguatesRecallExampleFromContext()
    {
        const string text = "Recall measures the proportion of actual positive cases that a model correctly identifies. " +
                            "High recall means the model misses fewer real positive instances, making it useful when " +
                            "failing to detect an existing object or condition would be especially costly or important.";

        var result = Analyze(text);

        AssertCategory(result, "recall", 0, GrammarCategory.SubjectNoun);
        AssertCategory(result, "measures", 0, GrammarCategory.Verb);
        AssertCategory(result, "the", 0, GrammarCategory.Determiner);
        AssertCategory(result, "proportion", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "actual", 0, GrammarCategory.Adjective);
        AssertCategory(result, "positive", 0, GrammarCategory.Adjective);
        AssertCategory(result, "cases", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "that", 0, GrammarCategory.Pronoun);
        AssertCategory(result, "a", 0, GrammarCategory.Determiner);
        AssertCategory(result, "model", 0, GrammarCategory.SubjectNoun);
        AssertCategory(result, "correctly", 0, GrammarCategory.Adverb);
        AssertCategory(result, "identifies", 0, GrammarCategory.Verb);

        AssertCategory(result, "high", 0, GrammarCategory.Adjective);
        AssertCategory(result, "recall", 1, GrammarCategory.SubjectNoun);
        AssertCategory(result, "means", 0, GrammarCategory.Verb);
        AssertCategory(result, "the", 1, GrammarCategory.Determiner);
        AssertCategory(result, "model", 1, GrammarCategory.SubjectNoun);
        AssertCategory(result, "misses", 0, GrammarCategory.Verb);
        AssertCategory(result, "fewer", 0, GrammarCategory.Quantifier);
        AssertCategory(result, "instances", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "making", 0, GrammarCategory.Verb);
        AssertCategory(result, "it", 0, GrammarCategory.Pronoun);
        AssertCategory(result, "useful", 0, GrammarCategory.Adjective);
        AssertCategory(result, "when", 0, GrammarCategory.Conjunction);
        AssertCategory(result, "failing", 0, GrammarCategory.Verb);
        AssertCategory(result, "to", 0, GrammarCategory.Particle);
        AssertCategory(result, "detect", 0, GrammarCategory.Verb);
        AssertCategory(result, "an", 0, GrammarCategory.Determiner);
        AssertCategory(result, "existing", 0, GrammarCategory.Adjective);
        AssertCategory(result, "object", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "condition", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "would", 0, GrammarCategory.Verb);
        AssertCategory(result, "be", 0, GrammarCategory.Verb);
        AssertCategory(result, "especially", 0, GrammarCategory.Adverb);
        AssertCategory(result, "costly", 0, GrammarCategory.Adjective);
        AssertCategory(result, "important", 0, GrammarCategory.Adjective);

        Assert.AreEqual(result.Tokens.Count, result.Analysis.Counts.Values.Sum());
    }

    [TestMethod]
    public void Analyze_RecallExampleProducesCompleteExpectedTokenSequence()
    {
        const string text = "Recall measures the proportion of actual positive cases that a model correctly identifies. " +
                            "High recall means the model misses fewer real positive instances, making it useful when " +
                            "failing to detect an existing object or condition would be especially costly or important.";

        var result = Analyze(text);
        var expected = new (string Token, GrammarCategory Category)[]
        {
            ("Recall", GrammarCategory.SubjectNoun),
            ("measures", GrammarCategory.Verb),
            ("the", GrammarCategory.Determiner),
            ("proportion", GrammarCategory.ObjectNoun),
            ("of", GrammarCategory.Preposition),
            ("actual", GrammarCategory.Adjective),
            ("positive", GrammarCategory.Adjective),
            ("cases", GrammarCategory.ObjectNoun),
            ("that", GrammarCategory.Pronoun),
            ("a", GrammarCategory.Determiner),
            ("model", GrammarCategory.SubjectNoun),
            ("correctly", GrammarCategory.Adverb),
            ("identifies", GrammarCategory.Verb),
            ("High", GrammarCategory.Adjective),
            ("recall", GrammarCategory.SubjectNoun),
            ("means", GrammarCategory.Verb),
            ("the", GrammarCategory.Determiner),
            ("model", GrammarCategory.SubjectNoun),
            ("misses", GrammarCategory.Verb),
            ("fewer", GrammarCategory.Quantifier),
            ("real", GrammarCategory.Adjective),
            ("positive", GrammarCategory.Adjective),
            ("instances", GrammarCategory.ObjectNoun),
            ("making", GrammarCategory.Verb),
            ("it", GrammarCategory.Pronoun),
            ("useful", GrammarCategory.Adjective),
            ("when", GrammarCategory.Conjunction),
            ("failing", GrammarCategory.Verb),
            ("to", GrammarCategory.Particle),
            ("detect", GrammarCategory.Verb),
            ("an", GrammarCategory.Determiner),
            ("existing", GrammarCategory.Adjective),
            ("object", GrammarCategory.ObjectNoun),
            ("or", GrammarCategory.Conjunction),
            ("condition", GrammarCategory.ObjectNoun),
            ("would", GrammarCategory.Verb),
            ("be", GrammarCategory.Verb),
            ("especially", GrammarCategory.Adverb),
            ("costly", GrammarCategory.Adjective),
            ("or", GrammarCategory.Conjunction),
            ("important", GrammarCategory.Adjective)
        };

        Assert.AreEqual(expected.Length, result.Tokens.Count);
        Assert.AreEqual(expected.Length, result.Analysis.Spans.Count);
        for (var index = 0; index < expected.Length; index++)
        {
            var token = result.Tokens[index];
            Assert.AreEqual(expected[index].Token, token.Text, $"Unexpected token at index {index}.");
            Assert.IsTrue(result.CategoryByStart.TryGetValue(token.Span.Start, out var actual),
                $"Token '{token.Text}' at index {index} has no grammar span.");
            Assert.AreEqual(expected[index].Category, actual,
                $"Unexpected category for '{token.Text}' at index {index}.");
        }
    }

    [TestMethod]
    public void Analyze_HandlesRelativeClausesInversionPassiveAndCoordination()
    {
        const string text = "Are the results accurate? Alice and Bob write tests. " +
                            "The results were reviewed by Carol. The report that the team wrote passed.";
        var result = Analyze(text);

        AssertCategory(result, "results", 0, GrammarCategory.SubjectNoun);
        AssertCategory(result, "Alice", 0, GrammarCategory.SubjectNoun);
        AssertCategory(result, "Bob", 0, GrammarCategory.SubjectNoun);
        AssertCategory(result, "write", 0, GrammarCategory.Verb);
        AssertCategory(result, "tests", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "results", 1, GrammarCategory.SubjectNoun);
        AssertCategory(result, "reviewed", 0, GrammarCategory.Verb);
        AssertCategory(result, "Carol", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "report", 0, GrammarCategory.SubjectNoun);
        AssertCategory(result, "that", 0, GrammarCategory.Pronoun);
        AssertCategory(result, "team", 0, GrammarCategory.SubjectNoun);
        AssertCategory(result, "wrote", 0, GrammarCategory.Verb);
        AssertCategory(result, "passed", 0, GrammarCategory.Verb);
    }

    [TestMethod]
    public void Analyze_DisambiguatesParticiplesGerundsAndDemonstratives()
    {
        const string text = "Running water can help. Running is healthy. She is running quickly. " +
                            "That model works. I know that the model works.";
        var result = Analyze(text);

        AssertCategory(result, "Running", 0, GrammarCategory.Adjective);
        AssertCategory(result, "Running", 1, GrammarCategory.SubjectNoun);
        AssertCategory(result, "running", 2, GrammarCategory.Verb);
        AssertCategory(result, "That", 0, GrammarCategory.Determiner);
        AssertCategory(result, "model", 0, GrammarCategory.SubjectNoun);
        AssertCategory(result, "that", 1, GrammarCategory.Conjunction);
        AssertCategory(result, "model", 1, GrammarCategory.SubjectNoun);
    }

    [TestMethod]
    public void Analyze_UsesSyntaxForPolysemousAdjectiveVerbWords()
    {
        const string text = "Open the file. The open file is valid. The file is open.";
        var result = Analyze(text);

        AssertCategory(result, "Open", 0, GrammarCategory.Verb);
        AssertCategory(result, "open", 1, GrammarCategory.Adjective);
        AssertCategory(result, "open", 2, GrammarCategory.Adjective);
    }

    [TestMethod]
    public void Analyze_DistinguishesDeterminersQuantifiersParticlesAndPrepositions()
    {
        const string text = "The model uses several tests to detect errors. We walked to school. Turn off the light.";
        var result = Analyze(text);

        AssertCategory(result, "The", 0, GrammarCategory.Determiner);
        AssertCategory(result, "several", 0, GrammarCategory.Quantifier);
        AssertCategory(result, "to", 0, GrammarCategory.Particle);
        AssertCategory(result, "to", 1, GrammarCategory.Preposition);
        AssertCategory(result, "off", 0, GrammarCategory.Particle);
    }

    [TestMethod]
    public void Analyze_DistinguishesGerundsProgressivesAndPassiveParticiples()
    {
        const string text = "She enjoys running. She is running quickly. Running is healthy. " +
                            "The window was broken by wind. The window is broken.";
        var result = Analyze(text);

        AssertCategory(result, "running", 0, GrammarCategory.ObjectNoun);
        AssertCategory(result, "running", 1, GrammarCategory.Verb);
        AssertCategory(result, "Running", 2, GrammarCategory.SubjectNoun);
        AssertCategory(result, "broken", 0, GrammarCategory.Verb);
        AssertCategory(result, "broken", 1, GrammarCategory.Adjective);
    }

    [TestMethod]
    public void Analyze_LooksThroughAdverbsForCopularAndPassiveContext()
    {
        const string text = "The door is very open. The window is completely broken. " +
                            "The results were carefully reviewed thoroughly by Carol.";
        var result = Analyze(text);

        AssertCategory(result, "open", 0, GrammarCategory.Adjective);
        AssertCategory(result, "broken", 0, GrammarCategory.Adjective);
        AssertCategory(result, "reviewed", 0, GrammarCategory.Verb);
    }

    [TestMethod]
    public void Analyze_DistinguishesRelativePossessiveDeterminersAndFreeRelatives()
    {
        const string text = "The author whose book won arrived. Whatever works is acceptable.";
        var result = Analyze(text);

        AssertCategory(result, "whose", 0, GrammarCategory.Determiner);
        AssertCategory(result, "book", 0, GrammarCategory.SubjectNoun);
        AssertCategory(result, "Whatever", 0, GrammarCategory.Pronoun);
    }

    [TestMethod]
    public void Analyze_DistinguishesWhSubordinatorsFromInterrogatives()
    {
        const string text = "When the rain stops, will we leave? When did we leave? I know when we left.";
        var result = Analyze(text);

        AssertCategory(result, "When", 0, GrammarCategory.Conjunction);
        AssertCategory(result, "When", 1, GrammarCategory.Interrogative);
        AssertCategory(result, "when", 2, GrammarCategory.Interrogative);
    }

    [TestMethod]
    public void Analyze_HandlesContentClausesRelativeClausesAndSerialSubjects()
    {
        const string text = "The fact that the model works matters. " +
                            "The report that the team wrote passed. Alice, Bob, and Carol write tests.";
        var result = Analyze(text);

        AssertCategory(result, "fact", 0, GrammarCategory.SubjectNoun);
        AssertCategory(result, "that", 0, GrammarCategory.Conjunction);
        AssertCategory(result, "report", 0, GrammarCategory.SubjectNoun);
        AssertCategory(result, "that", 1, GrammarCategory.Pronoun);
        AssertCategory(result, "Alice", 0, GrammarCategory.SubjectNoun);
        AssertCategory(result, "Bob", 0, GrammarCategory.SubjectNoun);
        AssertCategory(result, "Carol", 0, GrammarCategory.SubjectNoun);
    }

    [TestMethod]
    public void TextSegmentation_DoesNotSplitCommonAbbreviationsDecimalsOrInitials()
    {
        const string text = "Dr. A. Smith measured 3.14 units. It worked. The U.S. policy changed.";
        var sentences = TextSegmentation.GetSentences(text);

        Assert.AreEqual(3, sentences.Count);
        Assert.AreEqual("Dr. A. Smith measured 3.14 units.", Slice(text, sentences[0]));
        Assert.AreEqual("It worked.", Slice(text, sentences[1]));
        Assert.AreEqual("The U.S. policy changed.", Slice(text, sentences[2]));
    }

    [TestMethod]
    public void Analyze_ClassifiesNumericFormsWithoutDroppingThem()
    {
        const string text = "Version 3.14 reached 50% on 2026-08-09 during the 1ST run.";
        var result = Analyze(text);

        AssertCategory(result, "3.14", 0, GrammarCategory.Quantifier);
        AssertCategory(result, "50%", 0, GrammarCategory.Quantifier);
        AssertCategory(result, "2026-08-09", 0, GrammarCategory.Quantifier);
        AssertCategory(result, "1ST", 0, GrammarCategory.Quantifier);
    }

    [TestMethod]
    public void Analyze_HandlesCommonContractionsWithoutNounFallbacks()
    {
        const string text = "I'm testing because they don't know whether it's ready. We’re prepared. It's reviewed by the team.";
        var result = Analyze(text);

        AssertCategory(result, "I'm", 0, GrammarCategory.Pronoun);
        AssertCategory(result, "testing", 0, GrammarCategory.Verb);
        AssertCategory(result, "they", 0, GrammarCategory.Pronoun);
        AssertCategory(result, "don't", 0, GrammarCategory.Verb);
        AssertCategory(result, "whether", 0, GrammarCategory.Conjunction);
        AssertCategory(result, "it's", 0, GrammarCategory.Pronoun);
        AssertCategory(result, "ready", 0, GrammarCategory.Adjective);
        AssertCategory(result, "We’re", 0, GrammarCategory.Pronoun);
        AssertCategory(result, "prepared", 0, GrammarCategory.Adjective);
        AssertCategory(result, "reviewed", 0, GrammarCategory.Verb);
    }

    [TestMethod]
    public void Analyze_ProcessesMoreThanOneHundredThousandWordsWithoutDroppingTokens()
    {
        const int repetitions = 17_000; // 6 tokens each = 102,000 words.
        var sentence = "The model correctly identifies positive cases.";
        var text = string.Join(' ', Enumerable.Repeat(sentence, repetitions));
        var tokens = TextTokenizer.Tokenize(text);
        var sentences = TextSegmentation.GetSentences(text);

        var analysis = new GrammarColorAnalyzer().Analyze(text, tokens, sentences);

        Assert.AreEqual(102_000, tokens.Count);
        Assert.AreEqual(repetitions, sentences.Count);
        Assert.AreEqual(tokens.Count, analysis.Counts.Values.Sum());
        Assert.AreEqual(tokens.Count, analysis.Spans.Count);
    }

    [TestMethod]
    public void Coordinator_KeepsCompleteAnalysisButVisualLimitRemainsASetting()
    {
        const int repetitions = 500;
        var text = string.Join(' ', Enumerable.Repeat("The model identifies cases.", repetitions));
        var settings = AppSettings.CreateDefaults();
        settings.SmartColoringEnabled = true;
        settings.MaxVisualAnalysisSpans = 100;
        settings.SmartPanelVisible = false;
        settings.DuplicateDetectionEnabled = false;

        using var coordinator = new AnalysisCoordinator();
        var analysis = coordinator.Analyze(text, settings);

        Assert.IsTrue(analysis.ColoredSpans.Count > settings.MaxVisualAnalysisSpans,
            "Core analysis must remain complete; only the WPF renderer applies the visual cap.");
        Assert.AreEqual(TextTokenizer.Tokenize(text).Count, analysis.Statistics.GrammarCategoryCounts.Values.Sum());
    }

    private static AnalysisResult Analyze(string text)
    {
        var tokens = TextTokenizer.Tokenize(text);
        var sentences = TextSegmentation.GetSentences(text);
        var analysis = new GrammarColorAnalyzer().Analyze(text, tokens, sentences);
        var categoryByStart = analysis.Spans.ToDictionary(span => span.Span.Start, span => span.Category);
        return new AnalysisResult(tokens, analysis, categoryByStart);
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
        Assert.AreEqual(expected, actual,
            $"Unexpected category for '{token.Text}' at character {token.Span.Start}.");
    }

    private static string Slice(string text, TextSpan span) => text.Substring(span.Start, span.Length);

    private sealed record AnalysisResult(
        IReadOnlyList<TextToken> Tokens,
        GrammarAnalysis Analysis,
        IReadOnlyDictionary<int, GrammarCategory> CategoryByStart);
}
