# File fidelity

## Text formats

`.txt`, `.md`, `.yaml`, `.yml`, `.json`, and `.xml` use the same fidelity pipeline.

### Encoding detection

1. UTF-32 big-endian BOM.
2. UTF-32 little-endian BOM.
3. UTF-8 BOM.
4. UTF-16 big-endian BOM.
5. UTF-16 little-endian BOM.
6. Strict UTF-8 without BOM.
7. Windows-1252 fallback.

The detected code page and BOM choice are stored in `DocumentSession`. Save reuses them. If newly typed characters cannot be represented, the UI offers UTF-8 conversion instead of silently replacing data.

This is intentionally deterministic rather than probabilistic. Files in other legacy code pages should be converted to UTF-8 before editing or supported later through an explicit encoding picker.

### Line endings

`LineEndingProfile` records each original CRLF, LF, or CR in sequence. Internally, editor text is mapped consistently. At save:

- existing line-break positions reuse the recorded sequence;
- extra line breaks use the original dominant/preferred ending;
- removed line breaks remove the corresponding entries naturally.

This preserves untouched mixed-ending files byte-for-byte when encoding is unchanged.

### Structured text

Open/save does not parse or reserialize JSON, XML, or YAML. Therefore whitespace, indentation, property order, comments where the format allows them, and special characters remain unchanged except for user edits.

The **Format JSON or XML** command is explicitly destructive to whitespace because it pretty-prints the parsed structure. YAML formatting is not implemented.

## RTF

RTF bytes are loaded/saved through WPF `TextRange` with `DataFormats.Rtf`. The app preserves supported `FlowDocument` formatting such as fonts, emphasis, colors, paragraph alignment, indentation, and lists. WPF can normalize RTF control syntax while preserving the rendered content; byte-for-byte RTF identity is not promised.

## Atomic saves

Content is first written to a temporary sibling file. Existing files are replaced with `File.Replace` where available, then with move-overwrite as a fallback. Temporary and backup files are cleaned best-effort.
