# Modern Notepad

Modern Notepad is an offline-first Windows text editor built with WPF and .NET 10. It combines standard Notepad workflows with RTF formatting, tabs, recovery, structured-document fidelity, and selectable local, Python, or Google Cloud grammar analysis.

The application targets Windows 10 and Windows 11 on x64 and Arm64. Microsoft currently requires Visual Studio 2026 version 18.0 or later for the .NET 10 SDK; command-line builds can use the .NET 10 SDK directly.

## Architecture at a glance

```text
ModernNotepad.App (WPF shell)
  ├─ MainWindow / EditorDocumentView
  ├─ FlowDocument + RichTextBox formatting
  ├─ Theme, command, tab, and dialog coordination
  └─ UI-only mapping between FlowDocument positions and plain-text spans

ModernNotepad.Core (UI-independent)
  ├─ File and encoding services
  ├─ Recovery, settings, session, and recent-file services
  ├─ JSON / XML / YAML structured-text services
  └─ Cancellable text-analysis modules
       ├─ tokenizer and segmentation
       ├─ duplicate detector
       ├─ grammar analyzer (local heuristic or switchable provider)
       ├─ writing assistant
       └─ statistics and readability

ModernNotepad.Tests (MSTest)
  ├─ file fidelity and atomic saves
  ├─ duplicate detection
  ├─ text statistics
  ├─ structured validation / formatting
  └─ settings persistence and corruption recovery
```

The WPF project owns presentation concerns. `ModernNotepad.Core` has no WPF dependency, so file handling and analysis can be tested, reused, or replaced independently. Smart features receive plain text and return immutable spans/findings; they never rewrite the document text.

See [Architecture](docs/ARCHITECTURE.md) for component boundaries and data flows.

## Implemented features

### Editing and files

- New, open, edit, save, and Save As.
- `.txt`, `.rtf`, `.md`, `.yaml`, `.yml`, `.json`, and `.xml` support.
- BOM-aware UTF-8, UTF-16, and UTF-32 detection; Windows-1252 fallback for legacy text.
- Original encoding, special characters, preferred line ending, and mixed line-ending sequence retained for text formats.
- Atomic file replacement, external-change warning, recent files, and drag-and-drop opening.
- Undo/redo, cut/copy/paste/delete/select all, find/replace, word wrap, and 50–300% zoom.
- Configurable tab mode, session restore, timed recovery snapshots, and unsaved-change confirmation.
- Asynchronous reads/writes and cancellable background analysis. Large plain-text documents are materialized incrementally to keep the dispatcher responsive.

### Rich text

- Font family and size.
- Bold, italic, underline, and strikethrough.
- Text color and highlight color.
- Left, center, and right alignment.
- Bulleted and numbered lists.
- Increase/decrease indentation and clear formatting.
- RTF round-tripping through WPF `TextRange`; saving a rich document as plain text gives a format-loss warning.

### Optional smart features

- Debounced Smart Coloring with configurable colors for subjects/nouns, verbs, objects/nouns, adjectives, adverbs, pronouns, prepositions, conjunctions, interrogatives, and quantifiers. Grammar analysis can use the existing local logic or a selectable Python spaCy, Python NLTK, or Google Cloud Natural Language provider.
- Duplicate words in a sentence, frequent words by paragraph/document, and duplicate sentences.
- Configurable repetition threshold, strict common-word checking, duplicate highlight color, and per-warning ignore.
- Word, character, sentence, and paragraph counts; reading time; Flesch reading-ease estimate; grammatical-category counts.
- Local common-typo suggestions, article/phrase checks, multiple-space checks, long-sentence warnings, and passive-voice heuristics.
- WPF real-time spell checking, using the configured language and operating entirely offline.
- Smart colors are visual overlays for plain-text files. Duplicate highlights are never persisted. Smart colors can be retained when saving as RTF.

### UI and settings

- Menu bar, formatting toolbar, tab strip, editor, status bar, and optional writing panel.
- Light/dark themes, custom accent, grammar colors, duplicate color, and keyboard navigation/access keys.
- Status indicators for line/column, words, characters, zoom, encoding, line endings, and save/recovery state.
- Settings for font, size, theme, autosave, default format, wrapping, tabs, session restore, spell language, smart features, thresholds, and visual-span cap.

The complete implementation/planning matrix is in [Features](docs/FEATURES.md).

## Prerequisites

### Visual Studio

1. Install **Visual Studio 2026 18.0 or later**.
2. In Visual Studio Installer, select **.NET desktop development**.
3. Ensure the **.NET 10 SDK** is installed.

### Command line

Install the .NET 10 SDK, then verify:

```powershell
dotnet --version
```

The repository pins SDK feature band `10.0.302` in `global.json` and permits roll-forward to a newer installed .NET 10 feature band.

### Optional grammar-analysis configuration

The grammar-mode switch offers five choices: **Logic & Traditional NLP**, **OpenAI**, **Python spaCy**, **Python NLTK**, and **Google Cloud Natural Language**. Traditional mode works without a network connection, Python, or an API key.

For **OpenAI**, keep the original configuration and set `OPENAI_API_KEY` before using that mode.

For **Python spaCy** or **Python NLTK**, install the optional local dependencies once:

```powershell
./scripts/setup-grammar-providers.ps1
```

The setup script installs spaCy, NLTK, `en_core_web_sm`, and NLTK's English perceptron tagger data. By default Modern Notepad launches `python`; set `MODERNNOTEPAD_PYTHON` to a specific `python.exe` if needed. The Python IPC transport can be switched between **Named Pipes** and **Shared Memory** in Settings or directly in the Grammar Analyzer panel.

For **Google Cloud Natural Language**, enable the Cloud Natural Language API for your Google Cloud project and provide an API key before launching Modern Notepad:

```powershell
$env:GOOGLE_CLOUD_NL_API_KEY = "your-key-here"
```

`GOOGLE_API_KEY` is also accepted. Modern Notepad does not persist either key in `settings.json`.

## Build and run

### Visual Studio

1. Open `ModernNotepad.sln`.
2. Select `Debug | Any CPU` or an x64/Arm64 Release configuration.
3. Set `ModernNotepad.App` as the startup project.
4. Press `F5`.

### PowerShell

If script execution is restricted or scripts are blocked by Windows, run:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
Get-ChildItem .\scripts\*.ps1 | Unblock-File
```

Then execute the scripts:

```powershell
./scripts/build.ps1
./scripts/test.ps1
./scripts/run.ps1
```

Equivalent CLI commands:

```powershell
dotnet restore ModernNotepad.sln
dotnet build ModernNotepad.sln -c Release --no-restore
dotnet test tests/ModernNotepad.Tests/ModernNotepad.Tests.csproj -c Release --no-build
```

## Publish

Create a self-contained x64 build that does not require a separately installed runtime:

```powershell
./scripts/publish.ps1 -Runtime win-x64 -SelfContained
```

Create an Arm64 build:

```powershell
./scripts/publish.ps1 -Runtime win-arm64 -SelfContained
```

The script writes publish output and a distributable ZIP under `artifacts/`. Visual Studio folder profiles are also included under `src/ModernNotepad.App/Properties/PublishProfiles`.

Detailed publishing, signing, optional Inno Setup packaging, and framework-dependent alternatives are in [Build and Publish](docs/BUILD_AND_PUBLISH.md).

## Tests

```powershell
./scripts/test.ps1
```

Tests use isolated temporary directories and cover:

- UTF-8 BOM, Windows-1252, special-character, mixed-line-ending, and atomic-save behavior.
- Duplicate words, stop-word mode, thresholds, and duplicate sentences.
- Counts, reading time, readability bounds, and grammar-category statistics.
- Python-provider response mapping and Google Cloud syntax-response/token-offset mapping without network calls.
- Settings round trips, grammar-mode persistence, numeric normalization, and corrupted settings recovery.
- JSON formatting/error locations and YAML indentation checks.

See [Testing](docs/TESTING.md).

## Sample documents

The `samples/` directory contains:

- `sample-rich-text.rtf` — fonts, emphasis, colors, highlight, bullets, and numbering.
- `smart-analysis.txt` — repeated words, common typos, passive voice, and a long sentence.
- `sample.md`, `sample.json`, `sample.xml`, and `sample.yaml`.
- `sample-mixed-line-endings.yaml` — intentionally mixed CRLF/LF/CR endings.
- `sample-windows-1252.txt` — legacy Windows-1252 characters.

## Local data and privacy

By default, analysis is local. Traditional mode is in-process, and Python spaCy/NLTK modes also stay on the machine while exchanging document text with a local worker over Named Pipes or Shared Memory. **OpenAI** and **Google Cloud Natural Language** modes send document text to their respective network services. Settings, session state, and recovery snapshots are stored under:

```text
%LOCALAPPDATA%\ModernNotepad\
```

Recovery content is unencrypted local application data. Do not rely on it as a secure vault or a version-control system. See [Security and Privacy](docs/SECURITY_AND_PRIVACY.md).

## Important limitations

- Logic & Traditional NLP grammar categories and grammar/passive-voice checks are deterministic English-language heuristics. OpenAI, spaCy, NLTK, and Google Cloud classification can also be wrong for ambiguous, fragmented, specialized, or unsupported-language text. OpenAI and Google Cloud modes require API credentials and send document text to their respective services; other writing-assistance checks remain local.
- YAML validation intentionally performs safe indentation checks only; a full YAML parser/schema engine is planned as an optional adapter.
- JSON/XML formatting normalizes whitespace by design. Ordinary save operations preserve text exactly apart from user edits and restored line-ending choices.
- WPF `RichTextBox` is optimized for formatted editing, not multi-gigabyte files. The UI yields while loading and cancels analysis, but a future virtualized plain-text editor is planned for extremely large documents.
- Smart visual overlays currently use reversible WPF text formatting. A future adorner-based renderer can further isolate overlays from the editor undo stream.

## Microsoft platform references

- WPF RichTextBox overview: <https://learn.microsoft.com/dotnet/desktop/wpf/controls/richtextbox-overview>
- WPF commanding overview: <https://learn.microsoft.com/dotnet/desktop/wpf/advanced/commanding-overview>
- WPF spell checking: <https://learn.microsoft.com/dotnet/desktop/wpf/controls/how-to-enable-spell-checking-in-a-text-editing-control>
- .NET cancellation: <https://learn.microsoft.com/dotnet/api/system.threading.cancellationtoken>
- .NET single-file deployment: <https://learn.microsoft.com/dotnet/core/deploying/single-file/overview>
- Install .NET on Windows: <https://learn.microsoft.com/dotnet/core/install/windows>
- .NET support policy: <https://dotnet.microsoft.com/platform/support/policy/dotnet-core>

## License

MIT. See [LICENSE](LICENSE).
