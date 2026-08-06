namespace ModernNotepad.Core.Models;

public sealed class SessionState
{
    public List<string> OpenFilePaths { get; set; } = [];
    public string? SelectedFilePath { get; set; }
}
