namespace ModernNotepad.Core.Models;

public sealed record ValidationResult(
    bool IsValid,
    string Message,
    int? Line = null,
    int? Column = null)
{
    public static ValidationResult Valid(string message = "The document is valid.") =>
        new(true, message);
}
