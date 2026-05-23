using System.Windows;
using KlevaDeploy.Models;
using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.Services;

public sealed class ThemeService : IThemeService
{
    private readonly IPreferencesService _prefsService;
    private readonly UserPreferences _prefs;

    public AppTheme CurrentTheme => _prefs.Theme;

    public ThemeService(IPreferencesService prefsService)
    {
        _prefsService = prefsService;
        _prefs = prefsService.Preferences;
        // The App will call SetTheme(CurrentTheme) on startup, 
        // but we ensure it's applied here just in case.
        ApplyTheme(_prefs.Theme);
    }

    public void SetTheme(AppTheme theme)
    {
        _prefs.Theme = theme;
        _prefsService.Save();
        ApplyTheme(theme);
    }

    public void ToggleTheme() =>
        SetTheme(CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);

    private static void ApplyTheme(AppTheme theme)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;

        // Remove existing theme dictionaries
        var toRemove = dictionaries
            .Where(d => d.Source is not null &&
                        (d.Source.OriginalString.Contains("Dark.xaml") ||
                         d.Source.OriginalString.Contains("Light.xaml")))
            .ToList();
        foreach (var d in toRemove) dictionaries.Remove(d);

        var toRemoveSupport = dictionaries
            .Where(d => d.Source is not null &&
                        (d.Source.OriginalString.Contains("Win11Styles.xaml") ||
                         d.Source.OriginalString.Contains("Icons.xaml")))
            .ToList();
        foreach (var d in toRemoveSupport) dictionaries.Remove(d);

        // Add the new theme dictionary at the beginning
        var themePath = theme == AppTheme.Dark
            ? "pack://application:,,,/Themes/Dark.xaml"
            : "pack://application:,,,/Themes/Light.xaml";

        dictionaries.Insert(0, new ResourceDictionary { Source = new Uri(themePath) });
        dictionaries.Insert(1, new ResourceDictionary { Source = new Uri("pack://application:,,,/Themes/Win11Styles.xaml") });
        dictionaries.Insert(2, new ResourceDictionary { Source = new Uri("pack://application:,,,/Themes/Icons.xaml") });

        if (!Application.Current.Resources.Contains("TextPrimaryBrush") ||
            !Application.Current.Resources.Contains("TextSecondaryBrush") ||
            !Application.Current.Resources.Contains("AccentBrush"))
        {
            var fallback = new ResourceDictionary { Source = new Uri("pack://application:,,,/Themes/Dark.xaml") };
            dictionaries.RemoveAt(0);
            dictionaries.Insert(0, fallback);
        }
    }
}
