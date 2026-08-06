using ModernNotepad.Core.Models;

namespace ModernNotepad.Core.Services;

public static class AtomicFileWriter
{
    public static async Task WriteBytesAsync(
        string path,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The target path does not have a parent directory.");
        Directory.CreateDirectory(directory);

        var fileName = Path.GetFileName(fullPath);
        var temporaryPath = Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.tmp");
        var backupPath = Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.bak");

        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(fullPath))
            {
                File.Move(temporaryPath, fullPath);
                return;
            }

            try
            {
                File.Replace(temporaryPath, fullPath, backupPath, ignoreMetadataErrors: true);
                TryDelete(backupPath);
            }
            catch (Exception exception) when (
                exception is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
            {
                File.Move(temporaryPath, fullPath, overwrite: true);
                TryDelete(backupPath);
            }
        }
        finally
        {
            TryDelete(temporaryPath);
            TryDelete(backupPath);
        }
    }

    public static async Task WriteTextAsync(
        string path,
        string content,
        TextEncodingInfo encoding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(encoding);
        await WriteBytesAsync(path, encoding.Encode(content), cancellationToken).ConfigureAwait(false);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Cleanup failures should not hide the original file operation result.
        }
    }
}
