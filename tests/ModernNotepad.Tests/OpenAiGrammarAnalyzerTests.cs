using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModernNotepad.Core.Analysis;

namespace ModernNotepad.Tests;

[TestClass]
public sealed class OpenAiGrammarAnalyzerTests
{
    [TestMethod]
    public void ParseAssignments_RequiresAndMapsEveryGrammarCategory()
    {
        const string response = """
        {
          "SubjectNoun": [0],
          "Verb": [1],
          "ObjectNoun": [2],
          "Adjective": [],
          "Adverb": [],
          "Pronoun": [],
          "Preposition": [],
          "Conjunction": [],
          "Interrogative": [],
          "Quantifier": [],
          "Other": []
        }
        """;

        var assignments = OpenAiGrammarAnalyzer.ParseAssignments(response, 0, 3);

        Assert.AreEqual(3, assignments.Count);
        Assert.AreEqual(GrammarCategory.SubjectNoun, assignments[0]);
        Assert.AreEqual(GrammarCategory.Verb, assignments[1]);
        Assert.AreEqual(GrammarCategory.ObjectNoun, assignments[2]);
    }

    [TestMethod]
    public void CreateAnalysis_ReturnsSameGrammarContractAsTraditionalAnalyzer()
    {
        const string text = "Birds build nests.";
        var tokens = TextTokenizer.Tokenize(text);
        var assignments = new Dictionary<int, GrammarCategory>
        {
            [0] = GrammarCategory.SubjectNoun,
            [1] = GrammarCategory.Verb,
            [2] = GrammarCategory.ObjectNoun
        };

        var aiAnalysis = OpenAiGrammarAnalyzer.CreateAnalysis(tokens, assignments);
        var traditionalAnalysis = new GrammarColorAnalyzer().Analyze(text, tokens);

        CollectionAssert.AreEquivalent(
            Enum.GetValues<GrammarCategory>(),
            aiAnalysis.Counts.Keys.ToArray());
        CollectionAssert.AreEquivalent(
            Enum.GetValues<GrammarCategory>(),
            traditionalAnalysis.Counts.Keys.ToArray());
        Assert.AreEqual(tokens.Count, aiAnalysis.Counts.Values.Sum());
        Assert.IsTrue(aiAnalysis.Spans.All(span => tokens.Any(token => token.Span == span.Span)));
    }

    [TestMethod]
    public void ParseAssignments_RejectsDuplicateTokenClassification()
    {
        const string response = """
        {
          "SubjectNoun": [0],
          "Verb": [0],
          "ObjectNoun": [],
          "Adjective": [],
          "Adverb": [],
          "Pronoun": [],
          "Preposition": [],
          "Conjunction": [],
          "Interrogative": [],
          "Quantifier": [],
          "Other": []
        }
        """;

        Assert.ThrowsExactly<InvalidDataException>(
            () => OpenAiGrammarAnalyzer.ParseAssignments(response, 0, 1));
    }
}
