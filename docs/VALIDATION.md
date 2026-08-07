# Validation report

Validation date: 2026-08-06

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

## Grammar-analysis mode change — 2026-08-08

The Traditional/AI grammar-mode change received an additional static validation pass in this artifact environment:

- `MainWindow.xaml`, `SettingsWindow.xaml`, project files, and central package props parse as XML.
- New XAML names/event wiring were checked against the code-behind (`GrammarAnalysisMode_Click` and the named mode controls).
- The AI adapter validates a token-to-category JSON map, requires every requested token ID exactly once, rejects unknown categories, and maps IDs back to the existing `GrammarAnalysis` spans/counts contract.
- No-network unit tests were added for AI response parsing, duplicate-token rejection, and the shared grammar-output contract.
- Settings round-trip coverage now includes the persisted grammar mode.

The environment still does not contain the .NET SDK/MSBuild, so the updated source and tests were **not compiled or executed here**. Run the Windows/.NET commands below for the definitive build and test result.

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
