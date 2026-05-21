namespace DeploymentApp.Services.Interfaces;

/// <summary>Shows modal dialogs from ViewModels without coupling to the View layer.</summary>
public interface IDialogService
{
    /// <summary>
    /// Shows the "disable required process" warning dialog.
    /// Returns <c>true</c> if the user confirms they want to disable the step anyway.
    /// </summary>
    bool ShowDisableRequiredWarning(string processName);
}
