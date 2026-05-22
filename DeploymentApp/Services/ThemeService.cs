using System.Windows;
using DeploymentApp.Models;
using DeploymentApp.Services.Interfaces;

namespace DeploymentApp.Services;

public sealed class ThemeService : IThemeService
{
    private readonly UserPreferences _prefs;

    public AppTheme CurrentTheme => _prefs.Theme;

    public ThemeService()
    {
        _prefs = UserPreferences.Load();
        // The App will call SetTheme(CurrentTheme) on startup, 
        // but we ensure it's applied here just in case.
        ApplyTheme(_prefs.Theme);
    }

    public void SetTheme(AppTheme theme)
    {
        _prefs.Theme = theme;
        _prefs.Save();
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

        // Add the new theme dictionary at the beginning
        var themePath = theme == AppTheme.Dark
            ? "pack://application:,,,/Themes/Dark.xaml"
            : "pack://application:,,,/Themes/Light.xaml";

        dictionaries.Insert(0, new ResourceDictionary { Source = new Uri(themePath) });
    }
}
