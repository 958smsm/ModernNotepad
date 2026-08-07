# Testing guide

## Automated unit tests

Run:

```powershell
dotnet test tests/ModernNotepad.Tests/ModernNotepad.Tests.csproj -c Release
```

Current test groups:

- **File operations**: UTF-8 BOM, Windows-1252, mixed CRLF/LF/CR preservation, special characters, and atomic replacement cleanup.
- **Duplicate detection**: repeated words, stop-word behavior, strict mode, duplicate sentences, and configured thresholds.
- **Text statistics**: word/character/sentence/paragraph counts, reading time, readability range, and grammar-category counts.
- **AI grammar contract**: token-to-category JSON parsing, duplicate-token rejection, and mapping AI token classifications into the same `GrammarAnalysis` category-count/span structure as Traditional mode without making a network request.
- **Settings**: serialization round trip, corruption fallback/preservation, bounds normalization, and default color repair.
- **Structured text**: strict JSON parsing (including comment rejection), JSON error positions/formatting, XML declaration-preserving formatting, and YAML tab-indentation warnings.

## Recommended manual smoke test

1. Start with an empty local-data directory or back up `%LOCALAPPDATA%\ModernNotepad`.
2. Create a plain document, type non-ASCII text, save, close, and reopen.
3. Open every file in `samples/`.
4. Verify formatting in `sample-rich-text.rtf` and Save As to another RTF.
5. Save a rich document as TXT and verify the format-loss confirmation.
6. Open `sample-mixed-line-endings.yaml`, save without edits, and compare bytes.
7. Open `sample-windows-1252.txt`, append a representable character, save, and verify encoding in the status bar.
8. Append an emoji to that Windows-1252 file; verify the UTF-8 conversion prompt.
9. Exercise undo/redo, cut/copy/paste, find/replace, lists, indentation, alignment, colors, and zoom.
10. Enable Smart Coloring and duplicate detection; type quickly and confirm the UI remains responsive.
11. Toggle grammar analysis between **Logic & Traditional NLP** and **AI**. With `OPENAI_API_KEY` set, confirm AI category counts/highlights update. Then launch without the key, choose AI, and confirm a fallback warning appears while local grammar results keep the editor usable.
12. Double-click a finding, ignore it, and confirm it disappears after reanalysis.
13. Make two dirty tabs, close the window, and test Save, Discard, and Cancel paths.
14. Force-terminate the process with a dirty document after an autosave interval; restart and verify recovery.
15. Modify an open file in another editor and verify the external-change warning on save.
16. Toggle high-contrast/keyboard navigation where applicable and inspect labels with Windows accessibility tools.

## Performance test suggestions

Generate documents around 1 MB, 10 MB, and 50 MB. Measure:

- cold and warm startup;
- open-to-editable time;
- typing latency with smart features off/on;
- cancellation after a new edit;
- peak working set;
- save time and byte fidelity.

Analysis is intentionally capped by `MaxVisualAnalysisSpans`; findings are capped at 500 per pass. For very large files, disable smart features. A future virtualized editor mode is the intended solution for much larger inputs.

## UI automation plan

A future Windows-only UI test project should cover:

- command/shortcut routing;
- tab creation and close prompts;
- dialog paths with testable abstractions;
- formatting selection behavior;
- automation names and focus order;
- theme resource changes;
- recovery integration using an isolated app-data root.


## Validation status

The source-package checks and the boundary between static validation and Windows-only compile/run verification are recorded in [Validation report](VALIDATION.md).
