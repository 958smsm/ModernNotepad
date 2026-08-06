# Application architecture

## Decision

Modern Notepad uses **WPF on .NET 10 LTS**.

WPF supplies a mature `RichTextBox`/`FlowDocument` model, routed editing commands, RTF serialization, local spell checking, accessibility automation properties, and a low-dependency desktop deployment path. The trade-off is that `FlowDocument` is heavier than a plain `TextBox`; the design therefore keeps analysis outside the visual tree and leaves a future virtualized editor replaceable behind the document-view boundary.

## Projects

### `ModernNotepad.App`

The Windows presentation layer.

- `MainWindow` coordinates commands, dialogs, tabs, save/close policy, status, recent files, autosave, and session restore.
- `EditorDocumentView` owns one editor surface, the find/replace bar, zoom, text snapshots, debounced analysis, and reversible visual overlays.
- `DocumentSession` is the tab/session state: `FlowDocument`, path, format, encoding, line endings, dirty state, recovery ID, statistics, and findings.
- `DocumentFactory` converts plain text or RTF into `FlowDocument` and serializes RTF.
- `DocumentTextSnapshot` maps a `FlowDocument` to plain text plus stable `TextPointer` boundaries. Analysis modules only see plain text; returned spans are mapped back to the editor here.
- `FormattingOverlayManager` applies/restores smart foreground or duplicate background formatting without changing characters.
- `RichTextFormattingService` wraps selection-scoped WPF formatting commands.
- `ThemeManager` swaps light/dark resource dictionaries and applies a custom accent.

### `ModernNotepad.Core`

UI-independent domain and infrastructure logic.

#### File pipeline

1. `FileService.LoadAsync` reads bytes asynchronously.
2. `.rtf` remains opaque bytes for WPF.
3. Text files are decoded by `EncodingDetector`:
   - UTF-32/UTF-16/UTF-8 BOMs are recognized first.
   - Strict UTF-8 is attempted next.
   - Windows-1252 is the conservative legacy fallback.
4. `LineEndingProfile` records every CRLF/LF/CR break, counts them, and selects a preferred ending.
5. On save, editor newlines are normalized internally, then the original sequence is reused by position; new line breaks use the original preferred style.
6. `AtomicFileWriter` writes beside the target and replaces/moves the completed temporary file.

#### Persistence

- `SettingsService`: normalized JSON settings; preserves a corrupt file with a timestamp before loading defaults.
- `RecoveryService`: one content file plus JSON metadata per dirty document.
- `SessionService`: paths for previously open saved files and the selected file.
- `RecentFilesManager`: bounded, de-duplicated most-recent-first list.

All state is under `%LOCALAPPDATA%\ModernNotepad`.

#### Analysis pipeline

`AnalysisCoordinator` is the module façade. A debounced editor pass captures text on the UI thread and calls it through `Task.Run` with a `CancellationToken`.

```text
text
 ├─ TextTokenizer ───────────────┐
 ├─ TextSegmentation             │
 │   ├─ sentences                │
 │   └─ paragraphs               │
 ├─ GrammarColorAnalyzer         ├─> TextStatisticsAnalyzer
 ├─ DuplicateDetector            │
 └─ WritingAssistantAnalyzer ────┘
                    |
                    v
        immutable DocumentAnalysis
        (statistics, findings, color spans, duplicate spans)
```

Cancellation checks are placed in token, character, sentence, and paragraph loops. A new keystroke cancels the previous pass. Visual spans and findings are capped in settings to prevent pathological UI work.

#### Structured documents

`StructuredTextService` provides:

- JSON validation with line/byte-column information and opt-in pretty printing.
- XML validation with line/column information and opt-in pretty printing.
- YAML indentation/tab checks without rewriting the source.

Normal opening/saving uses the generic file pipeline, so indentation, encoding, special characters, and line endings remain intact. Formatting commands are explicit because they intentionally normalize whitespace.

### `ModernNotepad.Tests`

MSTest tests only the core project. This keeps tests fast and runnable without a UI session. WPF command wiring and accessibility should eventually receive Windows UI automation tests in a separate test layer.

## Threading model

- WPF objects and `FlowDocument` remain on the dispatcher thread.
- File byte I/O uses asynchronous .NET file APIs.
- CPU analysis receives an immutable string and runs on a thread-pool task.
- Cancellation is cooperative and checked frequently.
- Results are applied only when the originating session is still attached.
- Plain-text document creation yields periodically to the dispatcher to reduce long UI stalls.

## Reliability decisions

- Save prompts occur per dirty document on close.
- Recovery is independent of normal save and is deleted after a successful save or explicit discard.
- Existing disk timestamps are compared before overwrite.
- File writes are atomic where the file system supports replace semantics, with a move-overwrite fallback.
- One corrupt recovery record does not prevent other records from loading.
- User-facing errors state the failed operation and a likely remedy.
- A legacy encoding that cannot represent new characters triggers an offer to convert the document to UTF-8.

## Extension seams

The following can be replaced without rewriting the shell:

- `GrammarColorAnalyzer` with a local ONNX/NLP implementation.
- `WritingAssistantAnalyzer` with language-specific providers.
- YAML validation with an adapter around a full parser.
- `FormattingOverlayManager` with a non-mutating adorner renderer.
- The WPF editor view with a virtualized plain-text/syntax component.
- `RecoveryService` with encrypted or enterprise-managed storage.

## Dependency policy

Runtime dependencies are intentionally minimal. The only non-framework runtime package is Microsoft's `System.Text.Encoding.CodePages`, used for legacy Windows encodings. Test packages are Microsoft Test SDK/MSTest. No cloud service is required.
