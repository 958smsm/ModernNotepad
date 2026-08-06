using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModernNotepad.Core.Analysis;

namespace ModernNotepad.Tests;

[TestClass]
public sealed class TextStatisticsTests
{
    [TestMethod]
    public void Analyze_CountsWordsCharactersSentencesAndParagraphs()
    {
        const string text = "The quick fox runs.\r\nIt jumps high!\r\n\r\nA final paragraph?";
        var statistics = new TextStatisticsAnalyzer().Analyze(text);

        Assert.AreEqual(10, statistics.WordCount);
        Assert.AreEqual(text.Length, statistics.CharacterCount);
        Assert.AreEqual(3, statistics.SentenceCount);
        Assert.AreEqual(2, statistics.ParagraphCount);
        Assert.AreEqual(0.05, statistics.ReadingTimeMinutes, 0.001);
        Assert.IsTrue(statistics.ReadabilityScore >= 0 && statistics.ReadabilityScore <= 100);
    }

    [TestMethod]
    public void Analyze_EmptyTextReturnsZeros()
    {
        var statistics = new TextStatisticsAnalyzer().Analyze(string.Empty);

        Assert.AreEqual(0, statistics.WordCount);
        Assert.AreEqual(0, statistics.CharacterCount);
        Assert.AreEqual(0, statistics.SentenceCount);
        Assert.AreEqual(0, statistics.ParagraphCount);
        Assert.AreEqual(0d, statistics.ReadingTimeMinutes);
        Assert.AreEqual(0d, statistics.ReadabilityScore);
    }

    [TestMethod]
    public void AnalysisCoordinator_IncludesGrammarCategoryStatistics()
    {
        var settings = ModernNotepad.Core.Models.AppSettings.CreateDefaults();
        var analysis = new AnalysisCoordinator().Analyze(
            "She carefully writes clear notes and organizes folders.",
            settings);

        Assert.IsTrue(analysis.Statistics.GrammarCategoryCounts.Values.Sum() > 0);
        Assert.IsTrue(analysis.Statistics.GrammarCategoryCounts.ContainsKey(GrammarCategory.Pronoun));
    }
}
