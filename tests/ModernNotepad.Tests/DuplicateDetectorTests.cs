using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModernNotepad.Core.Analysis;

namespace ModernNotepad.Tests;

[TestClass]
public sealed class DuplicateDetectorTests
{
    [TestMethod]
    public void Analyze_FindsRepeatedWordWithinSentence()
    {
        var result = new DuplicateDetector().Analyze(
            "The bright fox saw another bright fox.",
            repetitionThreshold: 3,
            strict: false);

        Assert.IsTrue(result.Findings.Any(finding => finding.Kind == FindingKind.RepeatedWord));
        Assert.IsTrue(result.HighlightSpans.Count >= 4);
    }

    [TestMethod]
    public void Analyze_IgnoresCommonWordsUnlessStrictModeIsEnabled()
    {
        const string text = "The cat and the dog and the bird arrived.";
        var relaxed = new DuplicateDetector().Analyze(text, repetitionThreshold: 2, strict: false);
        var strict = new DuplicateDetector().Analyze(text, repetitionThreshold: 2, strict: true);

        Assert.IsFalse(relaxed.Findings.Any(finding =>
            finding.Kind == FindingKind.RepeatedWord && finding.Message.Contains("the", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(strict.Findings.Any(finding =>
            finding.Kind == FindingKind.RepeatedWord && finding.Message.Contains("the", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Analyze_FindsDuplicateSentencesIgnoringCaseAndPunctuation()
    {
        var result = new DuplicateDetector().Analyze(
            "A useful sentence appears here. A useful sentence appears here!",
            repetitionThreshold: 3,
            strict: false);

        Assert.IsTrue(result.Findings.Any(finding => finding.Kind == FindingKind.DuplicateSentence));
    }

    [TestMethod]
    public void Analyze_FindsFrequentWordsAtConfiguredThreshold()
    {
        var result = new DuplicateDetector().Analyze(
            "Design matters because design clarifies choices and design makes intent visible.",
            repetitionThreshold: 3,
            strict: false);

        Assert.IsTrue(result.Findings.Any(finding =>
            finding.Kind == FindingKind.FrequentWord
            && finding.Message.Contains("design", StringComparison.OrdinalIgnoreCase)));
    }
}
