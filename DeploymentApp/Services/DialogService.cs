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
        var dialog = new DisableFeatureWarningDialog(processName);
        return dialog.ShowDialog() == true;
    }
}
