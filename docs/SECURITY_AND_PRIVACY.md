# Security and privacy

## Offline behavior

Modern Notepad contains no telemetry, advertising, account, HTTP client, cloud API, or automatic content upload. Smart analysis and spell checking are local.

## Local storage

The app writes under `%LOCALAPPDATA%\ModernNotepad`:

- `settings.json`
- `session.json`
- `Recovery\*.recovery.json`
- `Recovery\*.content`

Recovery files may contain unsaved document text and are not encrypted by the app. Windows account/file-system protection is the security boundary. Sensitive environments should use full-disk encryption, appropriate profile permissions, and a managed cleanup policy.

## Files

The application opens user-selected paths and command-line/drop paths. It does not execute document contents. RTF is parsed by the WPF rich-text stack; only supported flow content is used by the editor.

Atomic saving limits partial-write risk but does not replace backups/version control. Network and removable drives can have different replace semantics; the fallback path is used when required.

## Dependencies

Runtime dependency surface is intentionally small. Keep .NET, Windows, and NuGet packages patched. For release distribution, produce a software bill of materials, scan outputs, sign binaries/installers, and protect signing keys outside the repository.
