using ModernNotepad.Core.Models;

namespace ModernNotepad.Core.Services;

public sealed class FileService
{
    public async Task<FileLoadResult> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The document could not be found.", fullPath);
        }

        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var format = DocumentFormatExtensions.FromPath(fullPath);
        var modifiedUtc = File.GetLastWriteTimeUtc(fullPath);

        if (format.IsRichText())
        {
            return new FileLoadResult(
                fullPath,
                format,
                null,
                bytes,
                TextEncodingInfo.Utf8NoBom,
                LineEndingProfile.WindowsDefault,
                modifiedUtc);
        }

        var (text, encoding) = EncodingDetector.Decode(bytes);
        return new FileLoadResult(
            fullPath,
            format,
            text,
            null,
            encoding,
            LineEndingProfile.Detect(text),
            modifiedUtc);
    }

    public async Task SaveTextAsync(
        string path,
        string editorText,
        TextEncodingInfo encoding,
        LineEndingProfile lineEndings,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(editorText);
        ArgumentNullException.ThrowIfNull(encoding);
        ArgumentNullException.ThrowIfNull(lineEndings);

        var output = lineEndings.ApplyTo(editorText);
        await AtomicFileWriter.WriteBytesAsync(
            Path.GetFullPath(path),
            encoding.Encode(output),
            cancellationToken).ConfigureAwait(false);
    }

    public Task SaveRichTextAsync(
        string path,
        byte[] richTextBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(richTextBytes);
        return AtomicFileWriter.WriteBytesAsync(Path.GetFullPath(path), richTextBytes, cancellationToken);
    }
}
