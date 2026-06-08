using KlevaDeploy.Models;

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

    ArgumentPromptResponse ShowArgumentPrompt(string processName, string subtitle, IReadOnlyList<ArgumentInputDefinition> inputs, IReadOnlyDictionary<string, string> prefill);
}

public enum ArgumentPromptChoice
{
    Cancel,
    RunOnce,
    RunAlways
}

public sealed record ArgumentPromptResponse(ArgumentPromptChoice Choice, IReadOnlyDictionary<string, string> Values);
