using System.Text.Json;
using System.Text.Json.Serialization;
using ModernNotepad.Core.Models;

namespace ModernNotepad.Core.Services;

public sealed class RecoveryService
{
    private readonly JsonSerializerOptions _jsonOptions;

    public RecoveryService(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        RecoveryDirectory = Path.Combine(baseDirectory, "Recovery");
        _jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public string RecoveryDirectory { get; }

    public async Task SaveSnapshotAsync(
        RecoveryPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Directory.CreateDirectory(RecoveryDirectory);

        var safeId = SanitizeId(payload.RecoveryId);
        var contentFileName = $"{safeId}.content";
        var metadataPath = Path.Combine(RecoveryDirectory, $"{safeId}.recovery.json");
        var contentPath = Path.Combine(RecoveryDirectory, contentFileName);

        await AtomicFileWriter.WriteBytesAsync(contentPath, payload.Content, cancellationToken)
            .ConfigureAwait(false);

        var metadata = new RecoveryMetadata(
            payload.RecoveryId,
            payload.DisplayName,
            payload.OriginalPath,
            payload.Format,
            payload.Encoding,
            payload.LineEndings,
            contentFileName,
            payload.IsRichText,
            payload.SavedAtUtc);
        var json = JsonSerializer.Serialize(metadata, _jsonOptions);
        await AtomicFileWriter.WriteBytesAsync(
            metadataPath,
            TextEncodingInfo.Utf8NoBom.Encode(json),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RecoveryRecord>> LoadSnapshotsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(RecoveryDirectory))
        {
            return Array.Empty<RecoveryRecord>();
        }

        var records = new List<RecoveryRecord>();
        foreach (var metadataPath in Directory.EnumerateFiles(RecoveryDirectory, "*.recovery.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var json = await File.ReadAllTextAsync(metadataPath, cancellationToken).ConfigureAwait(false);
                var metadata = JsonSerializer.Deserialize<RecoveryMetadata>(json, _jsonOptions);
                if (metadata is null
                    || string.IsNullOrWhiteSpace(metadata.RecoveryId)
                    || string.IsNullOrWhiteSpace(metadata.DisplayName)
                    || metadata.Encoding is null
                    || metadata.LineEndings is null)
                {
                    continue;
                }

                var safeId = SanitizeId(metadata.RecoveryId);
                var expectedContentFileName = $"{safeId}.content";
                if (!string.Equals(
                        metadata.ContentFileName,
                        expectedContentFileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var contentPath = Path.Combine(RecoveryDirectory, expectedContentFileName);
                if (!File.Exists(contentPath))
                {
                    continue;
                }

                var content = await File.ReadAllBytesAsync(contentPath, cancellationToken).ConfigureAwait(false);
                records.Add(new RecoveryRecord(
                    metadata.RecoveryId,
                    metadata.DisplayName,
                    metadata.OriginalPath,
                    metadata.Format,
                    metadata.Encoding,
                    metadata.LineEndings,
                    content,
                    metadata.IsRichText,
                    metadata.SavedAtUtc));
            }
            catch (Exception exception) when (
                exception is IOException or JsonException or UnauthorizedAccessException or ArgumentException)
            {
                // A single damaged recovery entry must not block the remaining snapshots.
            }
        }

        return records.OrderBy(record => record.SavedAtUtc).ToArray();
    }

    public Task DeleteSnapshotAsync(string recoveryId)
    {
        var safeId = SanitizeId(recoveryId);
        TryDelete(Path.Combine(RecoveryDirectory, $"{safeId}.content"));
        TryDelete(Path.Combine(RecoveryDirectory, $"{safeId}.recovery.json"));
        return Task.CompletedTask;
    }

    private static string SanitizeId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
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
            // Recovery cleanup is best effort.
        }
    }
}
