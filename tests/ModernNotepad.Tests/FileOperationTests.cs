using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModernNotepad.Core.Models;
using ModernNotepad.Core.Services;

namespace ModernNotepad.Tests;

[TestClass]
public sealed class FileOperationTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "ModernNotepad.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // Test cleanup is best effort on systems with delayed antivirus file handles.
        }
    }

    [TestMethod]
    public async Task LoadAndSave_PreservesUtf8BomAndMixedLineEndings()
    {
        var source = Path.Combine(_directory, "mixed.yaml");
        var destination = Path.Combine(_directory, "saved.yaml");
        var originalText = "name: café\r\nitems:\n  - one\r  - two";
        var originalBytes = TextEncodingInfo.Utf8Bom.Encode(originalText);
        await File.WriteAllBytesAsync(source, originalBytes);

        var service = new FileService();
        var loaded = await service.LoadAsync(source);

        Assert.AreEqual(originalText, loaded.Text);
        Assert.AreEqual(65001, loaded.Encoding.CodePage);
        Assert.IsTrue(loaded.Encoding.EmitBom);
        Assert.IsTrue(loaded.LineEndings.HasMixedLineEndings);
        Assert.AreEqual(1, loaded.LineEndings.CrLfCount);
        Assert.AreEqual(1, loaded.LineEndings.LfCount);
        Assert.AreEqual(1, loaded.LineEndings.CrCount);

        await service.SaveTextAsync(
            destination,
            loaded.Text!,
            loaded.Encoding,
            loaded.LineEndings);

        CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(destination));
    }

    [TestMethod]
    public async Task LoadAndSave_PreservesWindows1252SpecialCharacters()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(1252);
        var source = Path.Combine(_directory, "legacy.txt");
        var destination = Path.Combine(_directory, "legacy-saved.txt");
        var text = "Résumé — £12.50\r\nnaïve façade";
        await File.WriteAllBytesAsync(source, encoding.GetBytes(text));

        var service = new FileService();
        var loaded = await service.LoadAsync(source);

        Assert.AreEqual(1252, loaded.Encoding.CodePage);
        Assert.AreEqual(text, loaded.Text);

        await service.SaveTextAsync(destination, loaded.Text!, loaded.Encoding, loaded.LineEndings);
        CollectionAssert.AreEqual(encoding.GetBytes(text), await File.ReadAllBytesAsync(destination));
    }

    [TestMethod]
    public void LineEndingProfile_UsesOriginalSequenceThenPreferredEnding()
    {
        var profile = LineEndingProfile.Detect("a\nb\r\nc\n");
        var result = profile.ApplyTo("a\r\nb\r\nc\r\nd\r\n");

        Assert.AreEqual("a\nb\r\nc\nd\n", result);
        Assert.AreEqual(LineEnding.Lf, profile.Preferred);
    }

    [TestMethod]
    public async Task SaveText_ReplacesExistingFileWithoutLeavingTemporaryFiles()
    {
        var path = Path.Combine(_directory, "atomic.json");
        await File.WriteAllTextAsync(path, "old");

        var service = new FileService();
        await service.SaveTextAsync(
            path,
            "{\"value\": 2}",
            TextEncodingInfo.Utf8NoBom,
            LineEndingProfile.WindowsDefault);

        Assert.AreEqual("{\"value\": 2}", await File.ReadAllTextAsync(path));
        Assert.AreEqual(1, Directory.EnumerateFiles(_directory).Count());
    }
}
