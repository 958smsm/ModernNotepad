using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModernNotepad.Core.Analysis;
using ModernNotepad.Core.Models;

namespace ModernNotepad.Tests;

[TestClass]
public sealed class GrammarProviderAnalyzerTests
{
    [TestMethod]
    public void PythonWorkerResponse_MapsEveryLocalToken()
    {
        const string text = "Birds build nests.";
        var tokens = TextTokenizer.Tokenize(text);
        var response = Encoding.UTF8.GetBytes(
            """{"ok":true,"assignments":["SubjectNoun","Verb","ObjectNoun"]}""");

        var analysis = PythonGrammarAnalyzer.ParseResponse(response, tokens);

        Assert.AreEqual(3, analysis.Counts.Values.Sum());
        Assert.AreEqual(1, analysis.Counts[GrammarCategory.SubjectNoun]);
        Assert.AreEqual(1, analysis.Counts[GrammarCategory.Verb]);
        Assert.AreEqual(1, analysis.Counts[GrammarCategory.ObjectNoun]);
    }

    [TestMethod]
    public void PythonWorkerResponse_AcceptsExpandedFunctionWordTaxonomy()
    {
        const string text = "The model uses 3 tests to run.";
        var tokens = TextTokenizer.Tokenize(text);
        var response = Encoding.UTF8.GetBytes(
            """{"ok":true,"assignments":["Determiner","SubjectNoun","Verb","Quantifier","ObjectNoun","Particle","Verb"]}""");

        var analysis = PythonGrammarAnalyzer.ParseResponse(response, tokens);

        Assert.AreEqual(tokens.Count, analysis.Counts.Values.Sum());
        Assert.AreEqual(1, analysis.Counts[GrammarCategory.Determiner]);
        Assert.AreEqual(1, analysis.Counts[GrammarCategory.Quantifier]);
        Assert.AreEqual(1, analysis.Counts[GrammarCategory.Particle]);
    }

    [TestMethod]
    public void GoogleSyntaxResponse_UsesUtf16OffsetsAndDependencies()
    {
        const string text = "😀 Birds build nests.";
        var tokens = TextTokenizer.Tokenize(text);
        var response = Encoding.UTF8.GetBytes(
            """
            {
              "tokens": [
                {"text":{"content":"😀","beginOffset":0},"partOfSpeech":{"tag":"X"},"dependencyEdge":{"label":"P"}},
                {"text":{"content":"Birds","beginOffset":3},"partOfSpeech":{"tag":"NOUN"},"dependencyEdge":{"label":"NSUBJ"}},
                {"text":{"content":"build","beginOffset":9},"partOfSpeech":{"tag":"VERB"},"dependencyEdge":{"label":"ROOT"}},
                {"text":{"content":"nests","beginOffset":15},"partOfSpeech":{"tag":"NOUN"},"dependencyEdge":{"label":"DOBJ"}},
                {"text":{"content":".","beginOffset":20},"partOfSpeech":{"tag":"PUNCT"},"dependencyEdge":{"label":"P"}}
              ],
              "language":"en"
            }
            """);

        var analysis = GoogleCloudGrammarAnalyzer.ParseSyntaxResponse(response, tokens);

        Assert.AreEqual(1, analysis.Counts[GrammarCategory.SubjectNoun]);
        Assert.AreEqual(1, analysis.Counts[GrammarCategory.Verb]);
        Assert.AreEqual(1, analysis.Counts[GrammarCategory.ObjectNoun]);
        CollectionAssert.AreEqual(
            tokens.Select(token => token.Span).ToArray(),
            analysis.Spans.Select(span => span.Span).ToArray());
    }

    [TestMethod]
    public void GoogleApiKeyResolution_UsesFirstNonBlankValue()
    {
        Assert.AreEqual("cloud-key", GoogleCloudGrammarAnalyzer.ResolveApiKey(" ", " cloud-key ", "other"));
    }

    [TestMethod]
    public void PythonConfiguration_UsesEnvironmentOverridesWhenPresent()
    {
        Assert.AreEqual("C:\\Python\\python.exe", PythonGrammarAnalyzer.ResolvePythonExecutable(" C:\\Python\\python.exe "));
        Assert.AreEqual(
            "C:\\tools\\worker.py",
            PythonGrammarAnalyzer.ResolveWorkerScriptPath(" C:\\tools\\worker.py ", "C:\\app"));
    }
    [TestMethod]
    public void Coordinator_ResolvesDirectAndIntermediateGrammarModes()
    {
        var direct = AppSettings.CreateDefaults();
        direct.GrammarMode = GrammarAnalysisMode.OpenAI;
        Assert.AreEqual(
            GrammarAnalysisMode.OpenAI,
            AnalysisCoordinator.ResolveConfiguredMode(direct));

        var intermediate = AppSettings.CreateDefaults();
        intermediate.GrammarMode = GrammarAnalysisMode.Provider;
        intermediate.GrammarProvider = GrammarAnalysisProvider.GoogleCloudNaturalLanguage;
        Assert.AreEqual(
            GrammarAnalysisMode.GoogleCloudNaturalLanguage,
            AnalysisCoordinator.ResolveConfiguredMode(intermediate));
    }

}
