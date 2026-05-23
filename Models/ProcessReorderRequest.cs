namespace DeploymentApp.Models;

public sealed record ProcessReorderRequest(ProcessSelectionItem Source, ProcessSelectionItem? Target, bool InsertAfter);

