using System.Windows;
using System.Windows.Media;
using ModernNotepad.Core.Models;
using AppThemeMode = ModernNotepad.Core.Models.ThemeMode;

namespace ModernNotepad.App.Services;

public static class ThemeManager
{
    public static void Apply(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var resources = Application.Current.Resources;
        var merged = resources.MergedDictionaries;

        var existingTheme = merged.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.Contains("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) == true
            || dictionary.Source?.OriginalString.Contains("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) == true);
        if (existingTheme is not null)
        {
            merged.Remove(existingTheme);
        }

        var themeName = settings.Theme == AppThemeMode.Dark ? "Dark" : "Light";
        merged.Insert(0, new ResourceDictionary
        {
            Source = new Uri($"Themes/{themeName}.xaml", UriKind.Relative)
        });

        var accent = ParseColor(settings.AccentColor, Color.FromRgb(79, 107, 237));
        resources["AccentColor"] = accent;
        resources["AccentBrush"] = Freeze(new SolidColorBrush(accent));
        resources["AccentMutedBrush"] = Freeze(new SolidColorBrush(Color.FromArgb(44, accent.R, accent.G, accent.B)));
        resources["AccentHoverBrush"] = Freeze(new SolidColorBrush(ChangeBrightness(accent, settings.Theme == AppThemeMode.Dark ? 0.18 : -0.10)));
    }

    public static Color ParseColor(string? value, Color fallback)
    {
        try
        {
            return value is null ? fallback : (Color)ColorConverter.ConvertFromString(value);
        }
        catch
        {
            return fallback;
        }
    }

    private static Color ChangeBrightness(Color color, double amount)
    {
        static byte Adjust(byte value, double amount)
        {
            var adjusted = amount >= 0
                ? value + ((255 - value) * amount)
                : value * (1 + amount);
            return (byte)Math.Clamp((int)Math.Round(adjusted), 0, 255);
        }

        return Color.FromArgb(color.A, Adjust(color.R, amount), Adjust(color.G, amount), Adjust(color.B, amount));
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        if (freezable.CanFreeze)
        {
            freezable.Freeze();
        }

        return freezable;
    }
}
