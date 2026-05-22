using DeploymentApp.Models;

namespace DeploymentApp.Services.Interfaces;

public interface IThemeService
{
    AppTheme CurrentTheme { get; }
    void SetTheme(AppTheme theme);
    void ToggleTheme();
}
