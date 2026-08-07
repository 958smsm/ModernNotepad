# Changelog

## Unreleased

- Added a persisted Grammar Analysis mode toggle between **Logic & Traditional NLP** and **AI**.
- Added OpenAI `2.12.0` Responses API integration using `gpt-5.4-mini` and `OPENAI_API_KEY`.
- AI token classifications are validated and mapped back to the existing `GrammarAnalysis` category-count/span format; failures fall back to the local analyzer with an explicit warning.
- Reduced unnecessary AI requests by using a longer AI debounce and reusing existing visual analysis when serializing RTF/recovery snapshots.

## 1.0.0 - 2026-08-06

- Initial WPF/.NET 10 implementation.
- Core text and RTF file operations with encoding/line-ending fidelity.
- Rich formatting toolbar, lists, alignment, indentation, colors, and dark mode.
- Tabs, recent files, recovery snapshots, session restore, drag/drop, search, zoom, and status bar.
- Optional duplicate detection, Smart Coloring, writing assistance, statistics, and local spell checking.
- Strict JSON validation/formatting, XML declaration-preserving formatting, and basic YAML indentation validation.
- Settings UI, unit tests, sample files, publish profiles, CI, and optional Inno Setup template.
