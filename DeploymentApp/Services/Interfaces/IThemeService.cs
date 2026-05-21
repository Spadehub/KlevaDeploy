namespace DeploymentApp.Services.Interfaces;

public enum AppTheme { Dark, Light }

public interface IThemeService
{
    AppTheme CurrentTheme { get; }
    void SetTheme(AppTheme theme);
    void ToggleTheme();
}
