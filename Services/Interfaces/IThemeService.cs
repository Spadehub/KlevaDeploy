using KlevaDeploy.Models;

namespace KlevaDeploy.Services.Interfaces;

public interface IThemeService
{
    AppTheme CurrentTheme { get; }
    AppThemeStyle CurrentThemeStyle { get; }
    void SetTheme(AppTheme theme);
    void SetThemeStyle(AppThemeStyle style);
    void ToggleTheme();
}
