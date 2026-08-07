# Implemented and planned features

Legend: **Implemented**, **Partial**, **Planned**.

| Area | Capability | Status | Notes |
|---|---|---:|---|
| Files | New/open/save/Save As | **Implemented** | Atomic replacement and clear error dialogs. |
| Files | TXT/RTF/MD/YAML/YML/JSON/XML | **Implemented** | Extension selects storage behavior. |
| Fidelity | BOM/encoding preservation | **Implemented** | UTF-8/16/32 plus Windows-1252 fallback. |
| Fidelity | Mixed line-ending preservation | **Implemented** | Original sequence reused; new breaks use preferred style. |
| Fidelity | External modification warning | **Implemented** | Timestamp check before overwrite. |
| Editing | Undo/redo and clipboard commands | **Implemented** | WPF routed commands. |
| Editing | Find/replace/replace all | **Implemented** | Match-case option and wraparound search. |
| Editing | Word wrap and zoom | **Implemented** | 50–300%. |
| Editing | Keyboard shortcuts | **Implemented** | File, search, formatting, zoom, analysis. |
| Editing | Drag-and-drop opening | **Implemented** | Multiple files when tabs are enabled. |
| Rich text | Font/size/emphasis/colors | **Implemented** | Selection or typing-format behavior. |
| Rich text | Alignment/lists/indent/clear | **Implemented** | WPF editing commands. |
| Tabs | Multiple documents | **Implemented** | Can be disabled for single-document behavior. |
| Recovery | Timed recovery snapshots | **Implemented** | Restored at startup; configurable interval. |
| Recovery | Previous saved-file session | **Implemented** | Optional. |
| Smart | Duplicate words/frequency/sentences | **Implemented** | Stop-word and strict modes. |
| Smart | Per-warning ignore | **Implemented** | Stable local IDs stored in settings. |
| Smart | Grammar-category coloring | **Implemented** | Mode toggle: local Logic & Traditional NLP or OpenAI `gpt-5.4-mini`, with the same category output contract. |
| Smart | Configurable category colors | **Implemented** | Hex colors in Settings. |
| Smart | Statistics/readability/reading time | **Implemented** | Flesch estimate is English-oriented. |
| Smart | Basic spelling | **Implemented** | WPF offline checker plus common-typo suggestions. |
| Smart | Grammar categories / long sentence / passive voice | **Implemented** | Selectable local heuristic or OpenAI `gpt-5.4-mini` for grammar categories; other checks remain local and advisory. |
| Structured | JSON validation/formatting | **Implemented** | Strict JSON; comments/trailing commas are rejected and formatting is explicit. |
| Structured | XML validation/formatting | **Implemented** | Formatting is explicit and retains an existing XML declaration. |
| Structured | YAML validation | **Partial** | Safe indentation/tab checks only. |
| Structured | Syntax highlighting | **Planned** | Keep separate from Smart Coloring. |
| Structured | Auto-indent/bracket matching | **Planned** | Editor-language adapter. |
| Structured | Folding/collapsible sections | **Planned** | Requires a syntax-aware editor surface. |
| Structured | Full YAML schema validation/formatting | **Planned** | Optional parser adapter. |
| UI | Light/dark/custom accent | **Implemented** | Dynamic resource dictionaries. |
| UI | Responsive smart panel | **Implemented** | Collapsible, width adapts to window. |
| UI | Accessibility labels/access keys | **Implemented** | Automation names and menu mnemonics. |
| Accessibility | Full UI automation regression suite | **Planned** | Separate Windows-only test project. |
| Performance | Cancellable background analysis | **Implemented** | Debounced and span-capped. |
| Performance | Very-large-file virtualized mode | **Planned** | Current FlowDocument mode is suitable for normal documents. |
| Packaging | Self-contained x64/Arm64 publish | **Implemented** | Scripts and publish profiles. |
| Packaging | ZIP distribution | **Implemented** | `scripts/publish.ps1`. |
| Packaging | Optional Inno Setup installer | **Implemented** | Template included; signing left to publisher. |
| Packaging | Signed MSIX / Store package | **Planned** | Requires publisher identity/certificate. |
| Internationalization | Configurable spell locale | **Implemented** | Depends on Windows/WPF dictionaries. |
| Internationalization | Localized UI and multilingual grammar | **Planned** | Analyzer-provider interface is the intended seam. |

## Development phases represented in this repository

1. **Basic editor and files** — complete.
2. **Undo/redo/search/shortcuts/status** — complete.
3. **Rich formatting and lists** — complete.
4. **Tabs/recovery/session** — complete.
5. **Duplicate detection** — complete.
6. **Smart grammatical coloring** — complete with selectable Logic & Traditional NLP or OpenAI AI grammar analysis.
7. **Writing assistance** — complete as a basic offline heuristic module.
8. **Settings/accessibility/tests/packaging** — substantially complete; UI automation, localization, signed packaging, syntax/folding, and a large-file editor remain planned.
