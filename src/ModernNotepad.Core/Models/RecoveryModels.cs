namespace ModernNotepad.Core.Models;

public sealed record RecoveryPayload(
    string RecoveryId,
    string DisplayName,
    string? OriginalPath,
    DocumentFormat Format,
    TextEncodingInfo Encoding,
    LineEndingProfile LineEndings,
    byte[] Content,
    bool IsRichText,
    DateTime SavedAtUtc);

public sealed record RecoveryRecord(
    string RecoveryId,
    string DisplayName,
    string? OriginalPath,
    DocumentFormat Format,
    TextEncodingInfo Encoding,
    LineEndingProfile LineEndings,
    byte[] Content,
    bool IsRichText,
    DateTime SavedAtUtc);

internal sealed record RecoveryMetadata(
    string RecoveryId,
    string DisplayName,
    string? OriginalPath,
    DocumentFormat Format,
    TextEncodingInfo Encoding,
    LineEndingProfile LineEndings,
    string ContentFileName,
    bool IsRichText,
    DateTime SavedAtUtc);
