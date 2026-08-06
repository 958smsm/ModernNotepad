namespace ModernNotepad.Core.Models;

public enum DocumentFormat
{
    PlainText,
    RichText,
    Markdown,
    Yaml,
    Json,
    Xml
}

public static class DocumentFormatExtensions
{
    public static DocumentFormat FromPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".rtf" => DocumentFormat.RichText,
            ".md" or ".markdown" => DocumentFormat.Markdown,
            ".yaml" or ".yml" => DocumentFormat.Yaml,
            ".json" => DocumentFormat.Json,
            ".xml" => DocumentFormat.Xml,
            _ => DocumentFormat.PlainText
        };
    }

    public static string DefaultExtension(this DocumentFormat format) => format switch
    {
        DocumentFormat.RichText => ".rtf",
        DocumentFormat.Markdown => ".md",
        DocumentFormat.Yaml => ".yaml",
        DocumentFormat.Json => ".json",
        DocumentFormat.Xml => ".xml",
        _ => ".txt"
    };

    public static string DisplayName(this DocumentFormat format) => format switch
    {
        DocumentFormat.RichText => "Rich Text",
        DocumentFormat.Markdown => "Markdown",
        DocumentFormat.Yaml => "YAML",
        DocumentFormat.Json => "JSON",
        DocumentFormat.Xml => "XML",
        _ => "Plain Text"
    };

    public static bool IsRichText(this DocumentFormat format) => format == DocumentFormat.RichText;

    public static bool IsStructured(this DocumentFormat format) =>
        format is DocumentFormat.Json or DocumentFormat.Xml or DocumentFormat.Yaml;
}
