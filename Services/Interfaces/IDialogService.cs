namespace KlevaDeploy.Services.Interfaces;

public interface IDialogService
{
    public enum UnrarPromptResult
    {
        Installa,
        FermaCoda,
        SaltaPassaggio
    }

    bool ShowDisableRequiredWarning(string processName);

    bool Confirm(string title, string message);

    UnrarPromptResult ShowUnrarRequiredPrompt(string processName, string details);

    void ResetDisableRequiredWarningPreference();
}
