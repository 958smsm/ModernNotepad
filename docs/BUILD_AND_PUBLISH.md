# Build, publish, and install

## Supported development environment

- Windows 11, or a Windows 10 LTSC/Enterprise development machine supported by the installed .NET SDK.
- Visual Studio 2026 18.0+ with **.NET desktop development**, or the .NET 10 SDK.
- PowerShell 7 is recommended for scripts; Windows PowerShell 5.1 also works.

The app itself is published for `win-x64` and `win-arm64`. Microsoft limits supported modern .NET 10 combinations on Windows 10 to LTSC/Enterprise releases; ordinary Windows 10 Home/Pro 22H2 reached end of support on October 14, 2025. Keep Windows and the .NET servicing patch current, and validate any legacy or ESU deployment on representative hardware.

## Restore and build

```powershell
dotnet restore ModernNotepad.sln
dotnet build ModernNotepad.sln -c Release --no-restore
```

Or:

```powershell
./scripts/build.ps1 -Configuration Release
```

## Run from source

```powershell
./scripts/run.ps1
```

To open files at startup:

```powershell
dotnet run --project src/ModernNotepad.App/ModernNotepad.App.csproj -- samples/sample.md samples/sample.json
```

## Test

```powershell
./scripts/test.ps1 -Configuration Release
```

## Self-contained publish

A self-contained package includes the .NET runtime. It is larger, but the destination computer does not need a separate .NET installation.

```powershell
./scripts/publish.ps1 -Runtime win-x64 -Configuration Release -SelfContained
./scripts/publish.ps1 -Runtime win-arm64 -Configuration Release -SelfContained
```

Output:

```text
artifacts/
  publish/win-x64/
  ModernNotepad-1.0.0-win-x64-self-contained.zip
```

The script enables single-file publishing. WPF/native support files may still be extracted by the .NET host at runtime; do not enable trimming because the WPF object/XAML model is not broadly trim-safe.

## Framework-dependent publish

This is smaller and receives shared runtime servicing, but requires .NET Desktop Runtime 10 on the target computer.

```powershell
./scripts/publish.ps1 -Runtime win-x64
```

## Visual Studio profiles

Right-click `ModernNotepad.App` → **Publish** and select one of:

- `win-x64-self-contained.pubxml`
- `win-arm64-self-contained.pubxml`
- `win-x64-framework-dependent.pubxml`

## Portable ZIP installation

1. Build a self-contained package.
2. Copy the ZIP to the target PC.
3. Extract it to a user-writable folder such as `%LOCALAPPDATA%\Programs\ModernNotepad` or an administrator-managed `Program Files` directory.
4. Run `ModernNotepad.exe`.

Settings and recovery remain in `%LOCALAPPDATA%\ModernNotepad` even if the program folder is replaced during upgrades.

## Optional Inno Setup installer

`installer/ModernNotepad.iss` creates a per-user installer from the x64 self-contained publish directory.

1. Install Inno Setup 6.
2. Publish x64:

   ```powershell
   ./scripts/publish.ps1 -Runtime win-x64 -SelfContained
   ```

3. Open `installer/ModernNotepad.iss` in Inno Setup Compiler.
4. Update publisher/support URL and optional code-signing directives.
5. Compile. The installer is written to `artifacts/installer`.

The template creates Start Menu and optional desktop shortcuts and includes an uninstall entry. It deliberately does not claim all supported text extensions by default.

## Signing

Before broad distribution, sign both the executable and installer with an organization-owned code-signing certificate. Signing requires private publisher material that is intentionally not stored in this repository. A typical release pipeline should:

1. Build and test from a clean commit.
2. Publish each architecture.
3. Run malware scanning and smoke tests on supported Windows 11 and Windows 10 LTSC/Enterprise test machines.
4. Sign `ModernNotepad.exe` and the installer.
5. Apply a trusted timestamp.
6. Verify signatures and checksums.
7. Archive the source commit, symbols, checksums, and build log.

## MSIX

A signed MSIX is a good future distribution option, but it requires a package identity, publisher certificate, assets, and desired file-association policy. Those values cannot be made trustworthy as generic sample constants, so this repository provides reproducible folder/ZIP publishing and an optional Inno Setup template instead.

## CI

`.github/workflows/windows-ci.yml` restores, builds, tests, publishes x64, and uploads the publish folder on a Windows runner. Configure signing as a protected release-stage job rather than exposing certificate secrets to pull-request workflows.
