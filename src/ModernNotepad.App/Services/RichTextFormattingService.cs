using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ModernNotepad.App.Services;

public static class RichTextFormattingService
{
    public static void ApplyFontFamily(RichTextBox editor, string familyName)
    {
        ArgumentNullException.ThrowIfNull(editor);
        if (string.IsNullOrWhiteSpace(familyName))
        {
            return;
        }

        editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(familyName));
    }

    public static void ApplyFontSize(RichTextBox editor, double size)
    {
        ArgumentNullException.ThrowIfNull(editor);
        if (!double.IsFinite(size) || size is < 6 or > 144)
        {
            return;
        }

        editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
    }

    public static void ToggleBold(RichTextBox editor) => Execute(EditingCommands.ToggleBold, editor);
    public static void ToggleItalic(RichTextBox editor) => Execute(EditingCommands.ToggleItalic, editor);
    public static void ToggleBullets(RichTextBox editor) => Execute(EditingCommands.ToggleBullets, editor);
    public static void ToggleNumbering(RichTextBox editor) => Execute(EditingCommands.ToggleNumbering, editor);
    public static void IncreaseIndentation(RichTextBox editor) => Execute(EditingCommands.IncreaseIndentation, editor);
    public static void DecreaseIndentation(RichTextBox editor) => Execute(EditingCommands.DecreaseIndentation, editor);

    public static void ToggleUnderline(RichTextBox editor) =>
        ToggleDecoration(editor, TextDecorationLocation.Underline);

    public static void ToggleStrikethrough(RichTextBox editor) =>
        ToggleDecoration(editor, TextDecorationLocation.Strikethrough);

    public static void ApplyForeground(RichTextBox editor, Brush brush)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(brush);
        editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
    }

    public static void ApplyBackground(RichTextBox editor, Brush brush)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(brush);
        editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, brush);
    }

    public static void AlignLeft(RichTextBox editor) => Execute(EditingCommands.AlignLeft, editor);
    public static void AlignCenter(RichTextBox editor) => Execute(EditingCommands.AlignCenter, editor);
    public static void AlignRight(RichTextBox editor) => Execute(EditingCommands.AlignRight, editor);

    public static void ClearFormatting(
        RichTextBox editor,
        string defaultFontFamily,
        double defaultFontSize)
    {
        ArgumentNullException.ThrowIfNull(editor);
        editor.Selection.ClearAllProperties();
        editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(defaultFontFamily));
        editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, defaultFontSize);
        editor.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
        editor.Selection.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Normal);
        editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, new TextDecorationCollection());
        editor.Selection.ApplyPropertyValue(Paragraph.TextAlignmentProperty, TextAlignment.Left);
        editor.Selection.ApplyPropertyValue(Paragraph.TextIndentProperty, 0d);
    }

    private static void ToggleDecoration(RichTextBox editor, TextDecorationLocation location)
    {
        ArgumentNullException.ThrowIfNull(editor);
        var raw = editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
        var decorations = raw as TextDecorationCollection;
        var updated = decorations?.CloneCurrentValue() ?? new TextDecorationCollection();
        var existing = updated.Where(decoration => decoration.Location == location).ToArray();

        if (existing.Length > 0)
        {
            foreach (var decoration in existing)
            {
                updated.Remove(decoration);
            }
        }
        else
        {
            var source = location == TextDecorationLocation.Underline
                ? TextDecorations.Underline[0]
                : TextDecorations.Strikethrough[0];
            updated.Add(source);
        }

        editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, updated);
    }

    private static void Execute(System.Windows.Input.RoutedUICommand command, RichTextBox editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        if (command.CanExecute(null, editor))
        {
            command.Execute(null, editor);
        }
    }
}
