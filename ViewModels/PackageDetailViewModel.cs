using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KlevaDeploy.Models;
using KlevaDeploy.Services.Interfaces;
using KlevaDeploy.Views;

namespace KlevaDeploy.ViewModels;

public sealed class PackageDetailViewModel : ObservableObject
{
    private readonly IInstallerService _installerService;
    private readonly IProcessExecutionService _processService;
    private readonly ILicenseScraperService _licenseScraperService;
    private readonly IAuthService _authService;
    private readonly ILogService _log;

    private SoftwarePackage? _selectedPackage;
    public SoftwarePackage? SelectedPackage
    {
        get => _selectedPackage;
        set
        {
            if (!SetProperty(ref _selectedPackage, value)) return;
            InstallCommand.NotifyCanExecuteChanged();
            RebuildFeatures(value);
        }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            InstallCommand.NotifyCanExecuteChanged();
        }
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>Feature items for the currently selected package preset.</summary>
    public ObservableCollection<FeatureItemViewModel> Features { get; } = new();
    public IAsyncRelayCommand InstallCommand { get; }

    public PackageDetailViewModel(
        IInstallerService installerService,
        IProcessExecutionService processService,
        ILicenseScraperService licenseScraperService,
        IAuthService authService,
        ILogService log)
    {
        _installerService = installerService;
        _processService = processService;
        _licenseScraperService = licenseScraperService;
        _authService = authService;
        _log = log;

        InstallCommand = new AsyncRelayCommand(InstallAsync, CanInstall);
    }

    /// <summary>
    /// Rebuilds the <see cref="Features"/> collection whenever a new preset is selected.
    /// All features start enabled; required ones are flagged.
    /// </summary>
    private void RebuildFeatures(SoftwarePackage? package)
    {
        // Unsubscribe old items
        foreach (var old in Features)
            old.RequiredFeatureWarningRequested -= OnRequiredFeatureWarning;

        Features.Clear();

        if (package is null) return;

        foreach (var feature in package.Features)
        {
            var vm = new FeatureItemViewModel(feature);
            vm.RequiredFeatureWarningRequested += OnRequiredFeatureWarning;
            Features.Add(vm);
        }
    }

    /// <summary>
    /// Shows the <see cref="DisableFeatureWarningDialog"/> and populates the event args
    /// with the user's decision.
    /// </summary>
    private static void OnRequiredFeatureWarning(object? sender, RequiredFeatureWarningEventArgs e)
    {
        var dialog = new DisableFeatureWarningDialog(e.FeatureName);
        var result = dialog.ShowDialog();
        e.Confirmed = result == true;
        e.DontShowAgain = dialog.DontShowAgain;
    }

    private async Task InstallAsync()
    {
        if (SelectedPackage is null) return;
        IsBusy = true;
        StatusMessage = $"Installazione di {SelectedPackage.Name} in corso...";

        try
        {
            string args = SelectedPackage.SilentArgs;

            if (SelectedPackage.RequiresLicense)
            {
                var licenses = await _licenseScraperService.FetchLicensesAsync();
                var key = _licenseScraperService.ExtractLicenseKey(licenses, SelectedPackage.Name, customerName: "");
                if (key is null)
                {
                    StatusMessage = "Licenza non trovata nel foglio Excel.";
                    _log.Warning($"License key not found for {SelectedPackage.Name}");
                    return;
                }
                args = args.Replace("{LICENSE_KEY}", key);
            }

            var installerPath = Path.Combine(AppContext.BaseDirectory, SelectedPackage.LocalInstallerRelativePath);
            var result = await _processService.RunAsync(installerPath, args);
            StatusMessage = result.ExitCode == 0
                ? $"{SelectedPackage.Name} installato con successo."
                : $"Errore durante l'installazione (exit code {result.ExitCode}).";
        }
        catch (Exception ex)
        {
            _log.Error("Install failed", ex);
            StatusMessage = $"Errore: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private bool CanInstall() => SelectedPackage is not null && !IsBusy;
}
