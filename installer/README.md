# Optional installer

The Inno Setup template consumes `artifacts/publish/win-x64`, produced by:

```powershell
../scripts/publish.ps1 -Runtime win-x64 -SelfContained
```

Compile `ModernNotepad.iss` with Inno Setup 6. Replace the sample publisher metadata and add organization-specific signing directives before public distribution. The template is per-user and intentionally does not register file associations.
