using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.Services;

public static class ExecutionErrorFormatter
{
    public static string BuildDetail(string processName, ProcessResult result, string? statusFallback = null)
    {
        var detail = FirstRelevantLine(result.StdErr);
        if (string.IsNullOrWhiteSpace(detail))
            detail = FirstRelevantLine(result.StdOut);

        if (string.IsNullOrWhiteSpace(detail))
        {
            var fallback = (statusFallback ?? string.Empty).Trim();
            if (IsUsefulFallback(fallback))
                detail = fallback;
        }

        detail = NormalizeKnownInstallerDetail(detail);

        if (ShouldPreferDetailOnly(result.ExitCode, detail))
            return detail;

        var mapped = MapExitCode(processName, result.ExitCode, detail);
        if (string.IsNullOrWhiteSpace(mapped))
            return detail;

        if (string.IsNullOrWhiteSpace(detail))
            return mapped;

        if (detail.Contains(mapped, StringComparison.OrdinalIgnoreCase))
            return detail;

        if (LooksLikeNoise(detail))
            return mapped;

        return $"{mapped} — {detail}";
    }

    private static string FirstRelevantLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var lines = value
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static line => !LooksLikeNoise(line))
            .ToArray();

        return lines.Length == 0 ? string.Empty : lines[0];
    }

    private static bool LooksLikeNoise(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var line = value.Trim();
        return line.StartsWith("KLEVADEPLOY_PROGRESS:", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("Inline PowerShell script kept for debugging", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("Inline batch script kept for debugging", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("Admin process output captured at:", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("Admin wrapper script kept for debugging:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUsefulFallback(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.StartsWith("In esecuzione", StringComparison.OrdinalIgnoreCase) &&
        !value.StartsWith("Errore (exit", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(value, "Errore", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeKnownInstallerDetail(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return string.Empty;

        if (detail.StartsWith("Prerequisito MSI ", StringComparison.OrdinalIgnoreCase))
            return detail;

        if (detail.Contains("not supported on this operating system", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("non supportato su questo sistema operativo", StringComparison.OrdinalIgnoreCase))
        {
            if (detail.Contains("native client", StringComparison.OrdinalIgnoreCase) ||
                detail.Contains("sqlncli", StringComparison.OrdinalIgnoreCase))
            {
                return "Il prerequisito SQL Server Native Client incluso dal pacchetto non e supportato su questo sistema operativo.";
            }

            return "Il prerequisito o installer incluso dal pacchetto non e supportato su questo sistema operativo.";
        }

        return detail;
    }

    private static bool ShouldPreferDetailOnly(int exitCode, string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return false;

        if (exitCode != 1603)
            return false;

        return detail.StartsWith("Errore SQL durante l'installazione Retail:", StringComparison.OrdinalIgnoreCase) ||
               detail.StartsWith("Connessione SQL non riuscita durante l'installazione Retail", StringComparison.OrdinalIgnoreCase) ||
               detail.StartsWith("È presente un riavvio di Windows in sospeso", StringComparison.OrdinalIgnoreCase) ||
               detail.StartsWith("Spazio su disco insufficiente", StringComparison.OrdinalIgnoreCase) ||
               detail.Contains("Errore 1001", StringComparison.OrdinalIgnoreCase);
    }

    private static string? MapExitCode(string processName, int exitCode, string? detail)
    {
        var normalized = unchecked((uint)exitCode);
        if (!string.IsNullOrWhiteSpace(detail) &&
            (detail.StartsWith("Prerequisito MSI ", StringComparison.OrdinalIgnoreCase) ||
             detail.Contains("non e supportato su questo sistema operativo", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var isOffice = processName.Contains("office", StringComparison.OrdinalIgnoreCase) ||
                       processName.Contains("microsoft 365", StringComparison.OrdinalIgnoreCase) ||
                       (!string.IsNullOrWhiteSpace(detail) && detail.Contains("office", StringComparison.OrdinalIgnoreCase));

        return exitCode switch
        {
            2 or 3 => "File o percorso non trovato.",
            5 => "Accesso negato. Prova a rieseguire come amministratore.",
            8 => "Memoria insufficiente per completare l'operazione.",
            32 => "File in uso da un altro processo.",
            87 => "Parametri non validi passati al processo.",
            112 => isOffice
                ? "Spazio su disco insufficiente per scaricare o aggiornare la cache di Microsoft 365."
                : "Spazio su disco insufficiente per completare l'operazione.",
            1602 => "Installazione annullata dall'utente o dal sistema.",
            1603 => "Errore irreversibile durante l'installazione.",
            1618 => "Un'altra installazione e gia in corso. Attendi che finisca e riprova.",
            1619 => "Pacchetto di installazione non trovato o non accessibile.",
            1620 => "Pacchetto di installazione non valido.",
            1638 => "E gia installata un'altra versione dello stesso prodotto.",
            740 => "Sono richiesti privilegi amministrativi per eseguire questo processo.",
            9009 => "Comando o eseguibile non trovato.",
            _ => normalized switch
            {
                0x80070002 or 0x80070003 => "File o percorso non trovato.",
                0x80070005 => "Accesso negato. Prova a rieseguire come amministratore.",
                0x80070008 => "Memoria insufficiente per completare l'operazione.",
                0x80070020 => "File in uso da un altro processo.",
                0x80070057 => "Parametri non validi passati al processo.",
                0x80070070 => isOffice
                    ? "Spazio su disco insufficiente per scaricare o aggiornare la cache di Microsoft 365."
                    : "Spazio su disco insufficiente per completare l'operazione.",
                0x8007007A => "Buffer insufficiente per completare l'operazione.",
                0x800700B7 => "File gia esistente e non sovrascrivibile.",
                0x800700C1 => "Il file non e un'applicazione Win32 valida o non e compatibile con questa architettura.",
                0x800700CE => "Percorso o nome file troppo lungo.",
                0x800704C7 => "Operazione annullata dall'utente.",
                0x8007010B => "Directory non valida o non accessibile.",
                _ => null
            }
        };
    }
}
