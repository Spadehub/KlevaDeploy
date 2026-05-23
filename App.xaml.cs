using System.Net;
using System.Net.Http;
using System.IO;
using System.Windows;
using KlevaDeploy.Services;
using KlevaDeploy.Services.Interfaces;
using KlevaDeploy.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace KlevaDeploy;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        InstallCrashHandlers();

        try
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
        catch (Exception ex)
        {
            WriteCrashLog("Startup", ex);
            MessageBox.Show(ex.ToString(), "KlevaDeploy crashed during startup", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
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
        services.AddSingleton<IPreferencesService, PreferencesService>();
        services.AddSingleton<IInstallerService, InstallerService>();
        services.AddSingleton<ILicenseScraperService, LicenseScraperService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IProcessExecutionService, ProcessExecutionService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IPresetIconService, PresetIconService>();

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
            sp.GetRequiredService<IPresetIconService>(),
            sp.GetRequiredService<IPreferencesService>(),
            loginVmFactory: () => sp.GetRequiredService<LoginViewModel>(),
            sp.GetRequiredService<LogViewModel>()
        ));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private void InstallCrashHandlers()
    {
        // NOTE: WPF WinExe apps often crash without a console stack trace.
        // These handlers persist a stack trace to Data\crash.log.
        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrashLog("DispatcherUnhandledException", args.Exception);
            MessageBox.Show(args.Exception.ToString(), "Unhandled UI exception", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(-1);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                WriteCrashLog("AppDomain.UnhandledException", ex);
            }
            else
            {
                WriteCrashLog("AppDomain.UnhandledException", new Exception($"Non-exception unhandled error: {args.ExceptionObject}"));
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteCrashLog("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    private static void WriteCrashLog(string context, Exception ex)
    {
        try
        {
            var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
            Directory.CreateDirectory(dataDir);
            var crashPath = Path.Combine(dataDir, "crash.log");

            File.AppendAllText(crashPath,
                $"[{DateTimeOffset.Now:O}] {context}{Environment.NewLine}{ex}{Environment.NewLine}{new string('-', 120)}{Environment.NewLine}");
        }
        catch
        {
            // Last resort: swallow. We don't want crash-logging to crash.
        }
    }
}
