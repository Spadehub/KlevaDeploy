using System.Net;
using System.Net.Http;
using System.Windows;
using DeploymentApp.Services;
using DeploymentApp.Services.Interfaces;
using DeploymentApp.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace DeploymentApp;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Apply the theme from preferences via the service
        var themeService = _serviceProvider.GetRequiredService<IThemeService>();
        themeService.SetTheme(themeService.CurrentTheme);

        var mainVm = _serviceProvider.GetRequiredService<MainViewModel>();
        var mainWindow = new MainWindow(mainVm);
        mainWindow.Show();

        await mainVm.InitializeCommand.ExecuteAsync(null);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Shared cookie container — persists auth session across all HTTP calls
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            AllowAutoRedirect = false,
            UseCookies = true,
        };
        var httpClient = new HttpClient(handler);

        services.AddSingleton(cookieContainer);
        services.AddSingleton(handler);
        services.AddSingleton(httpClient);

        // Services
        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<IInstallerService, InstallerService>();
        services.AddSingleton<ILicenseScraperService, LicenseScraperService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IProcessExecutionService, ProcessExecutionService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IDialogService, DialogService>();

        // ViewModels
        services.AddSingleton<LogViewModel>();
        // LoginViewModel is transient — a new instance per dialog
        services.AddTransient<LoginViewModel>();
        services.AddSingleton<MainViewModel>(sp => new MainViewModel(
            sp.GetRequiredService<IInstallerService>(),
            sp.GetRequiredService<IUpdateService>(),
            sp.GetRequiredService<IAuthService>(),
            sp.GetRequiredService<ILogService>(),
            sp.GetRequiredService<IThemeService>(),
            sp.GetRequiredService<IDialogService>(),
            loginVmFactory: () => sp.GetRequiredService<LoginViewModel>(),
            sp.GetRequiredService<LogViewModel>()
        ));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
