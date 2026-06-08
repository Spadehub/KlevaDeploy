using KlevaDeploy.Models;
using KlevaDeploy.Services.Interfaces;
using KlevaDeploy.Views;
using KlevaDeploy.ViewModels;
using System.Windows;

namespace KlevaDeploy.Services;

/// <summary>
/// Concrete dialog service — must be called on the UI thread.
/// Registered as Singleton in the DI container.
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly UserPreferences _prefs;

    public DialogService()
    {
        _prefs = UserPreferences.Load();
    }

    public bool ShowDisableRequiredWarning(string processName)
    {
        // Check if user has chosen to suppress this warning
        if (_prefs.SuppressRequiredProcessWarning)
            return true; // Allow disable without showing dialog

        var dialog = new DisableFeatureWarningDialog(processName)
        {
            // ISSUE 2 FIX: Set Owner to ensure dialog appears centered and buttons are visible
            Owner = System.Windows.Application.Current.MainWindow
        };
        
        bool? result = dialog.ShowDialog();
        
        // Only save "Don't show again" preference if user confirmed (not cancelled)
        if (result == true && dialog.DontShowAgain)
        {
            _prefs.SuppressRequiredProcessWarning = true;
            _prefs.Save();
        }
        
        return result == true;
    }

    public bool Confirm(string title, string message)
    {
        var owner = Application.Current?.MainWindow;
        if (owner is null)
        {
            return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }

        var dialog = new ConfirmDialog(title, message)
        {
            Owner = owner
        };
        return dialog.ShowDialog() == true;
    }

    public IDialogService.UnrarPromptResult ShowUnrarRequiredPrompt(string processName, string details)
    {
        var owner = Application.Current?.MainWindow;
        if (owner is null)
        {
            var message =
                $"Per completare \"{processName}\", serve UnRAR per estrarre l'installer.\n\n{details}\n\n" +
                "Sì = Installa\nNo = Ferma coda\nAnnulla = Salta questo passaggio (non raccomandato)";

            var result = MessageBox.Show(message, "UnRAR richiesto", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes) return IDialogService.UnrarPromptResult.Installa;
            if (result == MessageBoxResult.No) return IDialogService.UnrarPromptResult.FermaCoda;

            var confirmSkip = MessageBox.Show(
                "Sei sicuro di voler saltare questo passaggio?\n\nNon è raccomandato: alcune funzionalità chiave non saranno disponibili.",
                "Conferma",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes;

            return confirmSkip ? IDialogService.UnrarPromptResult.SaltaPassaggio : IDialogService.UnrarPromptResult.FermaCoda;
        }

        var dialog = new UnrarRequiredDialog(processName, details)
        {
            Owner = owner
        };
        _ = dialog.ShowDialog();
        return dialog.Result;
    }

    public void ResetDisableRequiredWarningPreference()
    {
        _prefs.SuppressRequiredProcessWarning = false;
        _prefs.Save();
    }

    public ArgumentPromptResponse ShowArgumentPrompt(string processName, string subtitle, IReadOnlyList<ArgumentInputDefinition> inputs, IReadOnlyDictionary<string, string> prefill)
    {
        var owner = Application.Current?.MainWindow;
        if (owner is null)
            return new ArgumentPromptResponse(ArgumentPromptChoice.Cancel, new Dictionary<string, string>());

        var vm = new ArgumentPromptViewModel(processName, subtitle, inputs, prefill);
        var dialog = new ArgumentPromptDialog(vm)
        {
            Owner = owner
        };

        var ok = dialog.ShowDialog() == true;
        if (!ok || vm.Mode == ArgumentPromptMode.None)
            return new ArgumentPromptResponse(ArgumentPromptChoice.Cancel, new Dictionary<string, string>());

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in vm.Items)
        {
            if (string.IsNullOrWhiteSpace(i.Key)) continue;
            values[i.Key.Trim()] = i.Value ?? string.Empty;
        }

        var choice = vm.Mode == ArgumentPromptMode.RunAlways ? ArgumentPromptChoice.RunAlways : ArgumentPromptChoice.RunOnce;
        return new ArgumentPromptResponse(choice, values);
    }
}
