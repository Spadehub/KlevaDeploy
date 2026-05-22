using KlevaDeploy.Models;

namespace KlevaDeploy.Services.Interfaces;

public interface IThemeService
{
    AppTheme CurrentTheme { get; }
    void SetTheme(AppTheme theme);
    void ToggleTheme();
}
