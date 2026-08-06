using System.Windows.Input;

namespace ModernNotepad.App.Commands;

public static class AppCommands
{
    public static RoutedUICommand NewDocument { get; } = Create("New", "NewDocument", Key.N, ModifierKeys.Control);
    public static RoutedUICommand Open { get; } = Create("Open", "Open", Key.O, ModifierKeys.Control);
    public static RoutedUICommand Save { get; } = Create("Save", "Save", Key.S, ModifierKeys.Control);
    public static RoutedUICommand SaveAs { get; } = Create("Save As", "SaveAs", Key.S, ModifierKeys.Control | ModifierKeys.Shift);
    public static RoutedUICommand CloseDocument { get; } = Create("Close", "CloseDocument", Key.W, ModifierKeys.Control);
    public static RoutedUICommand Find { get; } = Create("Find", "Find", Key.F, ModifierKeys.Control);
    public static RoutedUICommand Replace { get; } = Create("Replace", "Replace", Key.H, ModifierKeys.Control);
    public static RoutedUICommand FindNext { get; } = Create("Find Next", "FindNext", Key.F3, ModifierKeys.None);
    public static RoutedUICommand ZoomIn { get; } = Create("Zoom In", "ZoomIn", Key.OemPlus, ModifierKeys.Control);
    public static RoutedUICommand ZoomOut { get; } = Create("Zoom Out", "ZoomOut", Key.OemMinus, ModifierKeys.Control);
    public static RoutedUICommand ResetZoom { get; } = Create("Reset Zoom", "ResetZoom", Key.D0, ModifierKeys.Control);
    public static RoutedUICommand AnalyzeNow { get; } = Create("Analyze Now", "AnalyzeNow", Key.F7, ModifierKeys.None);

    private static RoutedUICommand Create(
        string text,
        string name,
        Key key,
        ModifierKeys modifiers)
    {
        return new RoutedUICommand(
            text,
            name,
            typeof(AppCommands),
            new InputGestureCollection { new KeyGesture(key, modifiers) });
    }
}
