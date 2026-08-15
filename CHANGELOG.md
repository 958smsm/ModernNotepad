# Changelog

## Unreleased

- Expanded the grammar switch while retaining both original modes: **Logic & Traditional NLP** and **OpenAI**.
- Added **Python spaCy**, **Python NLTK**, and **Google Cloud Natural Language** as additional direct grammar modes.
- Added selectable Windows **Named Pipes** or **Shared Memory** IPC for persistent Python grammar workers.
- Added `scripts/grammar_provider.py` and `scripts/setup-grammar-providers.ps1` for local Python provider setup.
- Google Cloud syntax responses use UTF-16 offsets and are mapped back to the existing `GrammarAnalysis` category/span contract.
- Provider failures are logged with known API keys redacted and automatically fall back to local grammar analysis for the current pass.
- Reworked **Logic & Traditional NLP** into a context-sensitive sentence/clause analyzer with stronger polysemy, noun-role, relative-clause, inversion, coordination, gerund/participle, and function-word handling.
- Replaced the small hand-maintained Traditional lexicon with a deterministic,
  compressed 108,845-word WordNet/Brown asset that preserves weighted
  multi-part-of-speech profiles while retaining the public
  `GrammarLexicon.Lexicon` compatibility field.
- Added productive morphology for regular inflections, broad irregular
  verb/plural/comparison forms, possessives and compounds, plus context-aware
  proper-name, acronym, contraction, phrasal-verb, and unknown-word handling.
- Added a versioned Universal Dependencies English EWT 2.18 accuracy/throughput
  benchmark with release gates and third-party data notices.
- Replaced repeated sentence/paragraph token rescans with forward span/token indexing; removed core analysis/finding truncation and moved the visual-span limit to WPF rendering only.
- Hardened sentence segmentation around common abbreviations, initials, initialisms, decimals, ellipses, and closing punctuation.
- Added pronunciation-aware article checks, high-confidence pronoun/auxiliary agreement checks, and broader passive patterns.
- Added 102,000-word Traditional-analysis stress coverage and grammar regression tests for ambiguity and complex syntax.
- Added production regressions for rare lexicon entries, ambiguous noun/verb
  readings, regular and irregular forms, Unicode and stacked contractions,
  proper names/acronyms, split infinitives, phrasal particles, and coined words.
- Batched optional NLTK tagging and chunked optional spaCy processing below the active pipeline's `max_length`.
- Split Traditional function-word output into explicit **Determiner** and **Particle** categories so articles (`a`, `an`, `the`) and infinitival/phrasal particles (`to`, `off`) are no longer mislabeled or omitted.
- Added contextual handling for content-vs-relative `that`, wh-subordinators, gerunds/progressives, passive-vs-stative participles, serial coordinated subjects, contractions, and numeric/date/percentage/ordinal tokens.
- Precomputed relative-clause verb/clause context and bounded local ambiguity probes to prevent the accuracy refinements from reintroducing pathological sentence-level rescans.

## 1.0.0 - 2026-08-06

- Initial WPF/.NET 10 implementation.
- Core text and RTF file operations with encoding/line-ending fidelity.
- Rich formatting toolbar, lists, alignment, indentation, colors, and dark mode.
- Tabs, recent files, recovery snapshots, session restore, drag/drop, search, zoom, and status bar.
- Optional duplicate detection, Smart Coloring, writing assistance, statistics, and local spell checking.
- Strict JSON validation/formatting, XML declaration-preserving formatting, and basic YAML indentation validation.
- Settings UI, unit tests, sample files, publish profiles, CI, and optional Inno Setup template.
