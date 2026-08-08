# Changelog

## Unreleased

- Expanded the grammar switch while retaining both original modes: **Logic & Traditional NLP** and **OpenAI**.
- Added **Python spaCy**, **Python NLTK**, and **Google Cloud Natural Language** as additional direct grammar modes.
- Added selectable Windows **Named Pipes** or **Shared Memory** IPC for persistent Python grammar workers.
- Added `scripts/grammar_provider.py` and `scripts/setup-grammar-providers.ps1` for local Python provider setup.
- Google Cloud syntax responses use UTF-16 offsets and are mapped back to the existing `GrammarAnalysis` category/span contract.
- Provider failures are logged with known API keys redacted and automatically fall back to local grammar analysis for the current pass.

## 1.0.0 - 2026-08-06

- Initial WPF/.NET 10 implementation.
- Core text and RTF file operations with encoding/line-ending fidelity.
- Rich formatting toolbar, lists, alignment, indentation, colors, and dark mode.
- Tabs, recent files, recovery snapshots, session restore, drag/drop, search, zoom, and status bar.
- Optional duplicate detection, Smart Coloring, writing assistance, statistics, and local spell checking.
- Strict JSON validation/formatting, XML declaration-preserving formatting, and basic YAML indentation validation.
- Settings UI, unit tests, sample files, publish profiles, CI, and optional Inno Setup template.
