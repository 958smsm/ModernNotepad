using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using ModernNotepad.Core.Models;

namespace ModernNotepad.App.Services;

public static class DocumentFactory
{
    public static FlowDocument CreateEmpty(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var document = CreateBase(settings);
        document.Blocks.Add(CreateParagraph(string.Empty));
        return document;
    }

    public static FlowDocument FromPlainText(string text, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(settings);

        var document = CreateBase(settings);
        var normalized = LineEndingProfile.NormalizeToLf(text);
        var lines = normalized.Split('\n', StringSplitOptions.None);
        foreach (var line in lines)
        {
            document.Blocks.Add(CreateParagraph(line));
        }

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(CreateParagraph(string.Empty));
        }

        return document;
    }


    public static async Task<FlowDocument> FromPlainTextAsync(
        string text,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(settings);

        var document = CreateBase(settings);
        var normalized = LineEndingProfile.NormalizeToLf(text);
        var lines = normalized.Split('\n', StringSplitOptions.None);

        for (var index = 0; index < lines.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            document.Blocks.Add(CreateParagraph(lines[index]));

            if (index > 0 && index % 750 == 0)
            {
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
        }

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(CreateParagraph(string.Empty));
        }

        return document;
    }

    public static FlowDocument FromRtf(byte[] bytes, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var document = CreateBase(settings);
        document.Blocks.Add(CreateParagraph(string.Empty));

        using var stream = new MemoryStream(bytes, writable: false);
        var range = new TextRange(document.ContentStart, document.ContentEnd);
        range.Load(stream, DataFormats.Rtf);
        return document;
    }

    public static byte[] ToRtf(FlowDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        using var stream = new MemoryStream();
        var range = new TextRange(document.ContentStart, document.ContentEnd);
        range.Save(stream, DataFormats.Rtf);
        return stream.ToArray();
    }

    private static FlowDocument CreateBase(AppSettings settings)
    {
        var fontFamily = new FontFamily(settings.DefaultFontFamily);
        var document = new FlowDocument
        {
            FontFamily = fontFamily,
            FontSize = settings.DefaultFontSize,
            PagePadding = new Thickness(18),
            LineStackingStrategy = LineStackingStrategy.MaxHeight
        };

        var paragraphStyle = new Style(typeof(Paragraph));
        paragraphStyle.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0)));
        paragraphStyle.Setters.Add(new Setter(Block.LineHeightProperty, double.NaN));
        document.Resources[typeof(Paragraph)] = paragraphStyle;
        return document;
    }

    private static Paragraph CreateParagraph(string text) => new(new Run(text))
    {
        Margin = new Thickness(0)
    };
}
