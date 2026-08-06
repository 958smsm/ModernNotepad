using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using ModernNotepad.Core.Analysis;

namespace ModernNotepad.App.Services;

public sealed class FormattingOverlayManager
{
    private readonly DependencyProperty _property;
    private readonly Func<RichTextBox, object> _fallbackValue;
    private readonly List<AppliedOverlay> _applied = [];

    public FormattingOverlayManager(
        DependencyProperty property,
        Func<RichTextBox, object> fallbackValue)
    {
        _property = property ?? throw new ArgumentNullException(nameof(property));
        _fallbackValue = fallbackValue ?? throw new ArgumentNullException(nameof(fallbackValue));
    }

    public void Apply(
        RichTextBox editor,
        DocumentTextSnapshot snapshot,
        IEnumerable<(TextSpan Span, object Value)> overlays)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(overlays);

        Restore(editor);
        foreach (var overlay in overlays)
        {
            if (overlay.Span.IsEmpty)
            {
                continue;
            }

            var range = snapshot.CreateRange(overlay.Span);
            if (range is null)
            {
                continue;
            }

            var original = range.GetPropertyValue(_property);
            if (ReferenceEquals(original, DependencyProperty.UnsetValue))
            {
                original = _fallbackValue(editor);
            }

            range.ApplyPropertyValue(_property, overlay.Value);
            _applied.Add(new AppliedOverlay(range, original));
        }
    }

    public void Restore(RichTextBox editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        for (var index = _applied.Count - 1; index >= 0; index--)
        {
            try
            {
                var overlay = _applied[index];
                overlay.Range.ApplyPropertyValue(_property, overlay.OriginalValue);
            }
            catch (InvalidOperationException)
            {
                // A range can become invalid after structural edits. The next analysis pass rebuilds it.
            }
        }

        _applied.Clear();
    }

    private sealed record AppliedOverlay(TextRange Range, object OriginalValue);
}
