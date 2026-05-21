using DeploymentApp.Models;
using DeploymentApp.Services.Interfaces;
using DeploymentApp.Views;

namespace DeploymentApp.Services;

/// <summary>
/// Concrete dialog service — must be called on the UI thread.
/// Registered as Singleton in the DI container.
/// </summary>
public sealed class DialogService : IDialogService
{
    public bool ShowDisableRequiredWarning(string processName)
    {
        // Check if user has chosen to suppress this warning
        if (UserPreferences.SuppressRequiredProcessWarning)
            return true; // Allow disable without showing dialog

        var dialog = new DisableFeatureWarningDialog(processName)
        {
            // ISSUE 2 FIX: Set Owner to ensure dialog appears centered and buttons are visible
            Owner = System.Windows.Application.Current.MainWindow
        };
        
        bool? result = dialog.ShowDialog();
        
        // Only save "Don't show again" preference if user confirmed (not cancelled)
        if (result == true && dialog.DontShowAgain)
            UserPreferences.SuppressRequiredProcessWarning = true;
        
        return result == true;
    }

    public void ResetDisableRequiredWarningPreference()
    {
        UserPreferences.SuppressRequiredProcessWarning = false;
    }
}
