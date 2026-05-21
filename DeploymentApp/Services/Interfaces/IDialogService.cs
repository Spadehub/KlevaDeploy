namespace DeploymentApp.Services.Interfaces;

/// <summary>Shows modal dialogs from ViewModels without coupling to the View layer.</summary>
public interface IDialogService
{
    /// <summary>
    /// Shows the "disable required process" warning dialog.
    /// Returns <c>true</c> if the user confirms they want to disable the step anyway.
    /// If the user has previously checked "Don't show again", returns <c>true</c> without showing the dialog.
    /// </summary>
    bool ShowDisableRequiredWarning(string processName);

    /// <summary>
    /// Resets the "Don't show again" preference for the disable required warning dialog.
    /// Useful for testing or allowing users to re-enable the warning.
    /// </summary>
    void ResetDisableRequiredWarningPreference();
}
