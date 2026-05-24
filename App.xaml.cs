using System.Net;
using System.Net.Http;
using System.IO;
using System.Diagnostics;
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
            if (await TryHandleUpdateModeAsync(e))
                return;

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

    private static async Task<bool> TryHandleUpdateModeAsync(StartupEventArgs e)
    {
        var args = e.Args ?? Array.Empty<string>();
        if (!args.Contains("--apply-update", StringComparer.OrdinalIgnoreCase))
            return false;

        var pid = TryGetArgInt(args, "--pid");
        var target = TryGetArgString(args, "--target");
        var source = Environment.ProcessPath;

        if (pid is null || string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(source))
        {
            Current?.Shutdown(-1);
            return true;
        }

        try
        {
            try
            {
                var p = Process.GetProcessById(pid.Value);
                await Task.Run(() => p.WaitForExit(30_000));
            }
            catch { }

            for (var i = 0; i < 30; i++)
            {
                try
                {
                    File.Copy(source, target, overwrite: true);
                    break;
                }
                catch
                {
                    await Task.Delay(200);
                }
            }

            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        finally
        {
            Current?.Shutdown();
        }

        return true;
    }

    private static int? TryGetArgInt(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase)) continue;
            return int.TryParse(args[i + 1], out var v) ? v : null;
        }
        return null;
    }

    private static string? TryGetArgString(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase)) continue;
            return args[i + 1];
        }
        return null;
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
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<IPreferencesService, PreferencesService>();
        services.AddSingleton<IInstallerService, InstallerService>();
        services.AddSingleton<ILicenseScraperService, LicenseScraperService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IDownloadDirectoryListingService, DownloadDirectoryListingService>();
        services.AddSingleton<IAppUpdateService, AppUpdateService>();
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
            sp.GetRequiredService<IDownloadDirectoryListingService>(),
            sp.GetRequiredService<IAppUpdateService>(),
            sp.GetRequiredService<IProcessExecutionService>(),
            sp.GetRequiredService<ILicenseScraperService>(),
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
