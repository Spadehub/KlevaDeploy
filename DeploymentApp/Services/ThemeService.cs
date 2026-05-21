using System.Windows;
using DeploymentApp.Services.Interfaces;

namespace DeploymentApp.Services;

public sealed class ThemeService : IThemeService
{
    public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

    public void SetTheme(AppTheme theme)
    {
        CurrentTheme = theme;
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

        // Add the new theme dictionary
        var themePath = theme == AppTheme.Dark
            ? "pack://application:,,,/Themes/Dark.xaml"
            : "pack://application:,,,/Themes/Light.xaml";

        dictionaries.Add(new ResourceDictionary { Source = new Uri(themePath) });
    }
}
