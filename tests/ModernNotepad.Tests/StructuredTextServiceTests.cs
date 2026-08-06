using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModernNotepad.Core.Models;
using ModernNotepad.Core.Structured;

namespace ModernNotepad.Tests;

[TestClass]
public sealed class StructuredTextServiceTests
{
    [TestMethod]
    public void ValidateJson_ReturnsLineInformationForInvalidInput()
    {
        var result = new StructuredTextService().Validate(
            "{\n  \"name\": true,\n}",
            DocumentFormat.Json);

        Assert.IsFalse(result.IsValid);
        Assert.IsNotNull(result.Line);
    }

    [TestMethod]
    public void FormatJson_IndentsValidDocument()
    {
        var formatted = new StructuredTextService().Format(
            "{\"name\":\"Modern Notepad\",\"enabled\":true}",
            DocumentFormat.Json);

        StringAssert.Contains(formatted, "\n");
        StringAssert.Contains(formatted, "  \"name\"");
    }

    [TestMethod]
    public void ValidateYaml_FlagsTabIndentation()
    {
        var result = new StructuredTextService().Validate(
            "root:\n\tchild: value",
            DocumentFormat.Yaml);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(2, result.Line);
    }

    [TestMethod]
    public void ValidateJson_RejectsCommentsInsteadOfSilentlyRemovingThem()
    {
        var result = new StructuredTextService().Validate(
            "{\n  // comment\n  \"name\": \"Modern Notepad\"\n}",
            DocumentFormat.Json);

        Assert.IsFalse(result.IsValid);
    }

    [TestMethod]
    public void FormatXml_PreservesDeclarationAndIndentsElements()
    {
        var formatted = new StructuredTextService().Format(
            "<?xml version=\"1.0\" encoding=\"windows-1252\"?><root><item>one</item></root>",
            DocumentFormat.Xml);

        Assert.IsTrue(formatted.StartsWith(
            "<?xml version=\"1.0\" encoding=\"windows-1252\"?>",
            StringComparison.Ordinal));
        StringAssert.Contains(formatted, "\n  <item>one</item>");
    }
}
