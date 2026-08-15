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
- **Grammar / providers**: Traditional-context regressions (relative/content clauses, inversion, passive-vs-stative participles, coordination, gerunds/progressives, demonstratives/complementizers, wh-subordinators, ASCII/Unicode/stacked contractions, determiner/quantifier/particle distinctions, numeric forms, weighted lexical ambiguity, regular/irregular inflections, proper names/acronyms, phrasal verbs, and unknown-word morphology), embedded 100,000+ lexicon compatibility, 102,000-word no-drop stress coverage, Python worker response mapping, Google Cloud UTF-16 syntax-token mapping, provider-setting round trips, and the shared `GrammarAnalysis` category-count/span contract without making a network request.
- **Settings**: serialization round trip, corruption fallback/preservation, bounds normalization, and default color repair.
- **Structured text**: strict JSON parsing (including comment rejection), JSON error positions/formatting, XML declaration-preserving formatting, and YAML tab-indentation warnings.

## Traditional grammar accuracy benchmark

Run the release gate:

```powershell
./scripts/benchmark-grammar.ps1
```

The checked-in benchmark evaluates the analyzer against the official Universal
Dependencies English Web Treebank 2.18 test split. It aligns the analyzer's
tokens with 21,133 eligible lexical gold tokens, maps UD universal POS tags to the
existing `GrammarCategory` compatibility taxonomy, reports per-category
accuracy and confusions, and separately scores `SubjectNoun`/`ObjectNoun`
role assignment. The default gate requires 90% coarse-category accuracy, 98%
alignment coverage, and 25,000 sustained tokens/second.

The 2026-08-09 reference run produced **91.44%** coarse-category accuracy
(19,061/20,846 aligned/evaluable tokens), **98.64%** alignment coverage, and
**88.86%** noun-role accuracy. Throughput depends on build configuration and
hardware and is printed on every run. Use `-ShowErrors` to include sample
misclassifications, or invoke the project directly to set different gates:

```powershell
dotnet run --project benchmarks/ModernNotepad.GrammarBenchmark -c Release -- `
  --minimum-accuracy 0.90 --minimum-coverage 0.98 --minimum-throughput 25000
```

The UD corpus and its CC BY-SA 4.0 license are stored under the benchmark's
`Data` directory. They are test inputs and are not embedded in the application.

## Lexicon reproducibility

`GrammarLexicon.tsv.gz` is a deterministic generated asset. With NLTK and its
WordNet/Brown resources installed, verify that both the asset and C# loader
match the generator without rewriting them:

```powershell
python scripts/generate_lexicon.py --no-download --check
```

Run once without `--no-download` to fetch missing generator-only corpus data.
The generated asset contains 108,845 entries and has SHA-256
`d6bb00754d7107aee080638982160a87a32aa0a65dd705392e8731c00b50b03c`.
See [Third-party data notices](../THIRD_PARTY_NOTICES.md) for attribution.

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
11. Cycle the grammar switch through **Logic & Traditional NLP**, **OpenAI**, **Python spaCy**, **Python NLTK**, and **Google Cloud Natural Language**. Test OpenAI with `OPENAI_API_KEY`; test spaCy and NLTK with both **Named Pipes** and **Shared Memory**; then test Google Cloud with `GOOGLE_CLOUD_NL_API_KEY`. Remove a required model/API key and confirm the warning appears while Traditional grammar results keep the editor usable.
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

Core analysis is not truncated by `MaxVisualAnalysisSpans`; that setting limits only WPF overlay rendering. Findings are likewise retained instead of applying the old 500-item result cap. The automated suite includes a 102,000-token Traditional-analysis stress case. For multi-megabyte UI performance testing, continue measuring cancellation latency and peak working set because the editor itself is not a virtualized text surface.

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
