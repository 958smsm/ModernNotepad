# Security and privacy

## Offline and provider behavior

Modern Notepad remains offline-first. Logic & Traditional NLP grammar analysis, writing-assistance checks, duplicate detection, and spell checking are local. No telemetry, advertising, or account system is added.

The **Python spaCy** and **Python NLTK** modes run locally in a child Python process. Document text crosses only the selected local IPC channel (Windows Named Pipes or named Shared Memory). The app does not send that text to a network service for these two modes.

When the user explicitly selects **OpenAI**, the original OpenAI grammar analyzer sends the text being classified to OpenAI and reads `OPENAI_API_KEY` from the environment. When the user selects **Google Cloud Natural Language**, the current document text is sent to Google's `documents:analyzeSyntax` API; its API key is read from `GOOGLE_CLOUD_NL_API_KEY` or `GOOGLE_API_KEY`. These API keys are not stored in `settings.json`. External-mode failures are reported and the local analyzer is used for that pass.

Provider diagnostics are written to `%LOCALAPPDATA%\ModernNotepad\grammar-provider-error.log`; known OpenAI and Google API-key environment values are redacted before logging.

## Local storage

The app writes under `%LOCALAPPDATA%\ModernNotepad`:

- `settings.json`
- `session.json`
- `grammar-provider-error.log` when provider failures occur
- `Recovery\*.recovery.json`
- `Recovery\*.content`

Recovery files may contain unsaved document text and are not encrypted by the app. Windows account/file-system protection is the security boundary. Sensitive environments should use full-disk encryption, appropriate profile permissions, and a managed cleanup policy.

## Files

The application opens user-selected paths and command-line/drop paths. It does not execute document contents. RTF is parsed by the WPF rich-text stack; only supported flow content is used by the editor.

Atomic saving limits partial-write risk but does not replace backups/version control. Network and removable drives can have different replace semantics; the fallback path is used when required.

## Dependencies

The main application keeps its managed runtime dependency surface small. Python grammar providers add optional Python packages/models outside the .NET process. Keep .NET, Windows, Python, spaCy/NLTK, and model/data packages patched. For release distribution, produce a software bill of materials, scan outputs, sign binaries/installers, and protect signing keys outside the repository.
