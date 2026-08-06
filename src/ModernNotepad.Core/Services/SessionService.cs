using System.Text.Json;
using ModernNotepad.Core.Models;

namespace ModernNotepad.Core.Services;

public sealed class SessionService
{
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public SessionService(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        SessionPath = Path.Combine(baseDirectory, "session.json");
    }

    public string SessionPath { get; }

    public async Task<SessionState> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SessionPath))
        {
            return new SessionState();
        }

        try
        {
            var json = await File.ReadAllTextAsync(SessionPath, cancellationToken).ConfigureAwait(false);
            var state = JsonSerializer.Deserialize<SessionState>(json, _jsonOptions) ?? new SessionState();
            state.OpenFilePaths ??= [];
            state.OpenFilePaths = state.OpenFilePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return state;
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new SessionState();
        }
    }

    public async Task SaveAsync(SessionState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var json = JsonSerializer.Serialize(state, _jsonOptions);
        await AtomicFileWriter.WriteBytesAsync(
            SessionPath,
            TextEncodingInfo.Utf8NoBom.Encode(json),
            cancellationToken).ConfigureAwait(false);
    }
}
