# Validation report

Validation date: 2026-08-09

This report distinguishes checks that were completed in the artifact environment from checks that require a Windows machine with the .NET desktop toolchain.

## Completed checks

A repository-wide static validation pass completed with **zero errors and zero warnings** before packaging. It checked:

- XML parsing for every XAML, project, props, publish-profile, manifest, and XML sample file.
- JSON parsing for project/sample JSON files.
- Solution project paths and every `ProjectReference`.
- WPF `x:Class` declarations, code-behind classes, and XAML event-handler names.
- Balanced C# delimiters and terminated comments, character literals, and string literals.
- Local Markdown links and the absence of unfinished-work or unimplemented-code placeholder markers.
- The intentional mixed-line-ending sample and the Windows-1252 fallback sample.
- Duplicate non-partial type declarations.
- SHA-256 hashes for all packaged source files.

The following version references were also checked against current Microsoft/NuGet releases on the validation date:

- .NET SDK `10.0.302` and .NET runtime/package patch `10.0.10`.
- `System.Text.Encoding.CodePages` `10.0.10`.
- `Microsoft.NET.Test.Sdk` `18.8.1`.
- `MSTest.TestAdapter` and `MSTest.TestFramework` `4.3.3`.

## Grammar-mode expansion — 2026-08-08

The grammar-mode enhancement received an additional validation pass in this artifact environment:

- `MainWindow.xaml`, `SettingsWindow.xaml`, and the edited project files parse as XML.
- The new Python worker passes `python -m py_compile`.
- The worker's UTF-16-to-Python offset conversion was smoke-tested with non-BMP text.
- spaCy/NLTK dependency failures were exercised in the available Python environment and return actionable setup errors instead of crashing the worker protocol.
- No-network unit tests cover the restored OpenAI response helpers, Python worker response mapping, Google Cloud syntax-response mapping, API-key resolution, and mode/transport settings persistence.
- The settings model normalizes invalid mode/provider/transport enum values, migrates the original saved `"AI"` value to `OpenAI`, and migrates the intermediate `"Provider"` + `GrammarProvider` format to the equivalent direct Python/Google mode.


## Traditional grammar robustness upgrade — 2026-08-09

The Traditional grammar upgrade received a focused validation pass in this artifact environment:

- Added regression tests for the reported `Recall measures ... / High recall means ...` classification errors and for relative clauses, subject–auxiliary inversion, passive constructions, coordinated subjects, gerunds/participles, demonstratives/complementizers, and adjective/verb polysemy.
- Added a 102,000-token stress test that asserts every token contributes to grammar-category counts and that category spans are not truncated.
- Replaced per-sentence/per-paragraph full token rescans with `SpanTokenIndex` forward alignment and moved `MaxVisualAnalysisSpans` enforcement out of the core result into WPF overlay rendering.
- `scripts/grammar_provider.py` passes `python -m py_compile`; its chunk iterator was exercised with 102,000 words and verified to reconstruct the input byte-for-character without dropping text. UTF-16 boundary mapping was also smoke-tested with a non-BMP character.
- A delimiter/comment/string static scan completed successfully for every C# file changed by this upgrade.

Because this environment still has no .NET SDK/MSBuild, the new C# regression/stress tests are included but could not be executed here. Run the Windows/.NET commands below before release.

The environment still does not contain the .NET SDK/MSBuild, so the updated C# source and MSTest suite were **not compiled or executed here**. The Windows/.NET commands below remain the definitive build and test result.

## Traditional grammar accuracy refinement — 2026-08-09

A second focused pass addressed classification errors found after the initial robustness upgrade:

- Added explicit `Determiner` and `Particle` categories across Traditional, OpenAI, spaCy, NLTK, Google Cloud, settings, colors, and provider-response parsing. This prevents articles from being reported as quantifiers and prevents infinitival `to` from disappearing as `Other`.
- Added regressions for the reported `Recall measures ...` paragraph, `to` particle vs preposition, phrasal particles, content-vs-relative `that`, wh-subordinators, gerunds/progressives, passive-vs-stative participles, serial subjects, contractions, and numeric/date/percentage/ordinal tokens.
- The tokenizer now retains numeric forms such as `3.14`, `50%`, `2026-08-09`, and case-insensitive ordinals such as `1ST` as analyzable tokens.
- The expanded token regex keeps `RegexOptions.NonBacktracking` but avoids lookarounds, which .NET does not support in non-backtracking mode; boundary matching is expressed with `\b` instead.
- Relative-clause subject inference now precomputes verb prefix/suffix and clause-start context once per sentence. The object-gap ambiguity search is bounded to local context, avoiding a new unbounded scan per complementizer.
- `scripts/grammar_provider.py` and `scripts/generate_lexicon.py` pass `python -m py_compile`; all XAML/XML/JSON files parse successfully; a repository-wide C# lexical delimiter/literal/comment scan passed for 54 C# files.

The artifact environment still has no .NET SDK/MSBuild, so these newly added C# tests are included but could not be compiled or executed here. The Windows/.NET commands below remain the definitive build/test validation.

## Checks that require Windows

The artifact environment does not contain the .NET SDK, MSBuild, the Windows Desktop targeting pack, or a Windows GUI session. Network policy also prevented installing the SDK there. Consequently, the following claims are intentionally **not** made in this report:

- that the WPF application was compiled or launched in this environment;
- that the MSTest suite was executed in this environment;
- that a Windows installer binary was generated or code-signed here.

The repository includes a Windows GitHub Actions workflow that restores, builds, tests, publishes a self-contained x64 application, and uploads test/publish artifacts. It is the definitive automated compile check.

## Reproduce the definitive build

On Windows with Visual Studio 2026 18.0+ and the .NET desktop workload, or with the .NET 10 SDK and Windows Desktop targeting pack:

```powershell
dotnet --info
./scripts/build.ps1
./scripts/test.ps1
./scripts/publish.ps1 -Runtime win-x64 -SelfContained
```

For a direct command-line run:

```powershell
dotnet restore ModernNotepad.sln
dotnet build ModernNotepad.sln -c Release --no-restore
dotnet test tests/ModernNotepad.Tests/ModernNotepad.Tests.csproj -c Release --no-build
```

## Recommended release smoke test

Before distributing a signed release, manually verify these Windows-only behaviors:

1. Open, edit, and save each sample file; confirm encoding and mixed line endings with a byte-level comparison where applicable.
2. Round-trip `sample-rich-text.rtf`; exercise every formatting command, list type, and theme.
3. Confirm tab/session recovery after a forced process termination.
4. Open a large text file; verify the UI remains responsive and smart analysis can be cancelled.
5. Exercise keyboard navigation, access keys, screen-reader labels, high-DPI scaling, and both x64 and Arm64 packages.
6. Install and uninstall the Inno Setup package on clean Windows test machines.
7. Sign the executable and installer with the publisher's certificate and verify the signature and SmartScreen reputation process.
