using ModernNotepad.Core.Models;

namespace ModernNotepad.Core.Services;

public static class RecentFilesManager
{
    public static void Add(AppSettings settings, string path, int maximum = 12)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        settings.RecentFiles.RemoveAll(existing =>
            string.Equals(existing, fullPath, StringComparison.OrdinalIgnoreCase));
        settings.RecentFiles.Insert(0, fullPath);

        if (settings.RecentFiles.Count > maximum)
        {
            settings.RecentFiles.RemoveRange(maximum, settings.RecentFiles.Count - maximum);
        }
    }

    public static void Remove(AppSettings settings, string path)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.RecentFiles.RemoveAll(existing =>
            string.Equals(existing, path, StringComparison.OrdinalIgnoreCase));
    }
}
