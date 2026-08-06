using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using ModernNotepad.Core.Models;

namespace ModernNotepad.Core.Structured;

public sealed class StructuredTextService
{
    public ValidationResult Validate(string text, DocumentFormat format)
    {
        ArgumentNullException.ThrowIfNull(text);

        return format switch
        {
            DocumentFormat.Json => ValidateJson(text),
            DocumentFormat.Xml => ValidateXml(text),
            DocumentFormat.Yaml => ValidateYaml(text),
            _ => ValidationResult.Valid("No structured validation is required for this format.")
        };
    }

    public string Format(string text, DocumentFormat format)
    {
        ArgumentNullException.ThrowIfNull(text);

        return format switch
        {
            DocumentFormat.Json => FormatJson(text),
            DocumentFormat.Xml => FormatXml(text),
            DocumentFormat.Yaml => text,
            _ => text
        };
    }

    private static ValidationResult ValidateJson(string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
            return ValidationResult.Valid("The JSON document is valid.");
        }
        catch (JsonException exception)
        {
            return new ValidationResult(
                false,
                exception.Message,
                exception.LineNumber is null ? null : checked((int)exception.LineNumber.Value + 1),
                exception.BytePositionInLine is null ? null : checked((int)exception.BytePositionInLine.Value + 1));
        }
    }

    private static ValidationResult ValidateXml(string text)
    {
        try
        {
            _ = XDocument.Parse(text, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            return ValidationResult.Valid("The XML document is valid.");
        }
        catch (XmlException exception)
        {
            return new ValidationResult(false, exception.Message, exception.LineNumber, exception.LinePosition);
        }
    }

    private static ValidationResult ValidateYaml(string text)
    {
        var lines = LineEndingProfile.NormalizeToLf(text).Split('\n');
        int? previousIndent = null;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var indentation = 0;
            foreach (var character in line)
            {
                if (character == ' ')
                {
                    indentation++;
                    continue;
                }

                if (character == '\t')
                {
                    return new ValidationResult(
                        false,
                        "Tabs in YAML indentation are discouraged and can be interpreted inconsistently.",
                        index + 1,
                        indentation + 1);
                }

                break;
            }

            if (previousIndent is not null && indentation - previousIndent > 8)
            {
                return new ValidationResult(
                    false,
                    "The indentation jumps by more than eight spaces. Check this block.",
                    index + 1,
                    1);
            }

            previousIndent = indentation;
        }

        return ValidationResult.Valid(
            "Basic YAML indentation checks passed. Full YAML schema validation is planned.");
    }

    private static string FormatJson(string text)
    {
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow
        });
        return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static string FormatXml(string text)
    {
        var document = XDocument.Parse(text, LoadOptions.None);
        var declaration = document.Declaration?.ToString();
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = true
        };

        using var writer = new StringWriter();
        using (var xmlWriter = XmlWriter.Create(writer, settings))
        {
            document.Save(xmlWriter);
        }

        var body = writer.ToString();
        return declaration is null ? body : $"{declaration}\n{body}";
    }
}
