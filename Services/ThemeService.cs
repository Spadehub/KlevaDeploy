using System.Windows;
using KlevaDeploy.Models;
using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.Services;

public sealed class ThemeService : IThemeService
{
    private readonly IPreferencesService _prefsService;
    private readonly UserPreferences _prefs;

    public AppTheme CurrentTheme => _prefs.Theme;
    public AppThemeStyle CurrentThemeStyle => _prefs.ThemeStyle;

    public ThemeService(IPreferencesService prefsService)
    {
        _prefsService = prefsService;
        _prefs = prefsService.Preferences;
        // The App will call SetTheme(CurrentTheme) on startup, 
        // but we ensure it's applied here just in case.
        ApplyTheme(_prefs.Theme, _prefs.ThemeStyle);
    }

    public void SetTheme(AppTheme theme)
    {
        _prefs.Theme = theme;
        _prefsService.Save();
        ApplyTheme(theme, _prefs.ThemeStyle);
    }

    public void SetThemeStyle(AppThemeStyle style)
    {
        _prefs.ThemeStyle = style;
        _prefsService.Save();
        ApplyTheme(_prefs.Theme, style);
    }

    public void ToggleTheme() =>
        SetTheme(CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);

    private static void ApplyTheme(AppTheme theme, AppThemeStyle style)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;

        var icons = dictionaries
            .Where(d => d.Source is not null && d.Source.OriginalString.Contains("Icons.xaml", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var d in icons) dictionaries.Remove(d);

        var toRemove = dictionaries
            .Where(d => d.Source is not null &&
                        (d.Source.OriginalString.Contains("Dark.xaml") ||
                         d.Source.OriginalString.Contains("Light.xaml") ||
                         d.Source.OriginalString.Contains("DarkFluentClean.xaml") ||
                         d.Source.OriginalString.Contains("LightFluentClean.xaml")))
            .ToList();
        foreach (var d in toRemove) dictionaries.Remove(d);

        // Add the new theme dictionary at the beginning
        var themePath = theme == AppTheme.Dark
            ? "pack://application:,,,/Themes/Dark.xaml"
            : "pack://application:,,,/Themes/Light.xaml";

        dictionaries.Insert(0, new ResourceDictionary { Source = new Uri(themePath) });

        if (style == AppThemeStyle.FluentClean)
        {
            var overridePath = theme == AppTheme.Dark
                ? "pack://application:,,,/Themes/DarkFluentClean.xaml"
                : "pack://application:,,,/Themes/LightFluentClean.xaml";
            dictionaries.Insert(1, new ResourceDictionary { Source = new Uri(overridePath) });
        }

        dictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Themes/Icons.xaml") });

        if (!Application.Current.Resources.Contains("TextPrimaryBrush") ||
            !Application.Current.Resources.Contains("TextSecondaryBrush") ||
            !Application.Current.Resources.Contains("AccentBrush"))
        {
            var fallback = new ResourceDictionary { Source = new Uri("pack://application:,,,/Themes/Dark.xaml") };
            var bad = dictionaries
                .Where(d => d.Source is not null &&
                            (d.Source.OriginalString.Contains("Dark.xaml") ||
                             d.Source.OriginalString.Contains("Light.xaml") ||
                             d.Source.OriginalString.Contains("DarkFluentClean.xaml") ||
                             d.Source.OriginalString.Contains("LightFluentClean.xaml") ||
                             d.Source.OriginalString.Contains("Icons.xaml")))
                .ToList();
            foreach (var d in bad) dictionaries.Remove(d);
            dictionaries.Insert(0, fallback);
            dictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Themes/Icons.xaml") });
        }
    }
}
