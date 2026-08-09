using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModernNotepad.Core.Analysis;

namespace ModernNotepad.Tests;

[TestClass]
public sealed class WritingAssistantAccuracyTests
{
    [TestMethod]
    public void Articles_UsePronunciationRatherThanFirstLetterOnly()
    {
        const string correct = "an hour, an honest answer, a university, a user, a European, and an MRI";
        var analyzer = new WritingAssistantAnalyzer();
        var correctFindings = analyzer.Analyze(
            correct,
            TextTokenizer.Tokenize(correct),
            TextSegmentation.GetSentences(correct),
            30,
            false);

        Assert.IsFalse(correctFindings.Any(finding => finding.Id.StartsWith("article", StringComparison.Ordinal)));

        const string incorrect = "a hour passed. an university opened.";
        var incorrectFindings = analyzer.Analyze(
            incorrect,
            TextTokenizer.Tokenize(incorrect),
            TextSegmentation.GetSentences(incorrect),
            30,
            false);

        Assert.AreEqual(2, incorrectFindings.Count(finding => finding.Message.StartsWith("Check the article", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void AgreementRules_FlagOnlyHighConfidencePronounAuxiliaryMismatches()
    {
        const string text = "She are ready. They is ready. I is ready. If I were ready, I would leave.";
        var analyzer = new WritingAssistantAnalyzer();
        var findings = analyzer.Analyze(
            text,
            TextTokenizer.Tokenize(text),
            TextSegmentation.GetSentences(text),
            30,
            false);

        var agreement = findings.Where(finding => finding.Message.Contains("agreement", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.AreEqual(3, agreement.Length);
        CollectionAssert.AreEquivalent(
            new[] { "is", "are", "am" },
            agreement.Select(finding => finding.Suggestion!).ToArray());
    }
}
