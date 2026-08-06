namespace ModernNotepad.Core.Models;

public sealed record FileLoadResult(
    string Path,
    DocumentFormat Format,
    string? Text,
    byte[]? RichTextBytes,
    TextEncodingInfo Encoding,
    LineEndingProfile LineEndings,
    DateTime LastWriteTimeUtc)
{
    public bool IsRichText => Format.IsRichText();
}
