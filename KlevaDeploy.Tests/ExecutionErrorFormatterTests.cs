using KlevaDeploy.Services;
using KlevaDeploy.Services.Interfaces;
using System.Reflection;

namespace KlevaDeploy.Tests;

public sealed class ExecutionErrorFormatterTests
{
    [Fact]
    public void BuildDetail_MapsOfficeDiskFullHRESULT_ToFriendlyMessage()
    {
        var result = new ProcessResult(unchecked((int)0x80070070), "KLEVADEPLOY_PROGRESS:5", string.Empty);

        var detail = ExecutionErrorFormatter.BuildDetail("Microsoft 365 Apps", result);

        Assert.Equal("Spazio su disco insufficiente per scaricare o aggiornare la cache di Microsoft 365.", detail);
    }

    [Fact]
    public void BuildDetail_MapsMsiBusyExitCode_ToFriendlyMessage()
    {
        var result = new ProcessResult(1618, string.Empty, string.Empty);

        var detail = ExecutionErrorFormatter.BuildDetail("SQL Server", result);

        Assert.Equal("Un'altra installazione e gia in corso. Attendi che finisca e riprova.", detail);
    }

    [Fact]
    public void BuildDetail_PreservesUsefulDetail_AlongsideFriendlyMapping()
    {
        var result = new ProcessResult(5, string.Empty, "Access is denied.");

        var detail = ExecutionErrorFormatter.BuildDetail("Retail", result);

        Assert.Equal("Accesso negato. Prova a rieseguire come amministratore. — Access is denied.", detail);
    }

    [Fact]
    public void BuildDetail_UsesSpecificUnsupportedOsMessage_InsteadOfGeneric1603()
    {
        var result = new ProcessResult(
            1603,
            string.Empty,
            "Product: Microsoft SQL Server 2012 Native Client -- Installation of this product failed because it is not supported on this operating system.");

        var detail = ExecutionErrorFormatter.BuildDetail("Retail", result);

        Assert.Equal("Il prerequisito SQL Server Native Client incluso dal pacchetto non e supportato su questo sistema operativo.", detail);
    }

    [Fact]
    public void BuildDetail_PreservesFriendlyPrereqFailureDetail_WithoutPrependingGeneric1603()
    {
        var result = new ProcessResult(
            1603,
            string.Empty,
            "Prerequisito MSI non supportato su questo sistema operativo: sqlncli.msi (SQL Server Native Client legacy). Log: C:\\temp\\sqlncli.log");

        var detail = ExecutionErrorFormatter.BuildDetail("Retail", result);

        Assert.Equal("Prerequisito MSI non supportato su questo sistema operativo: sqlncli.msi (SQL Server Native Client legacy). Log: C:\\temp\\sqlncli.log", detail);
    }

    [Fact]
    public void BuildDetail_DoesNotPrependGeneric1603_WhenDetailIsAlreadyActionable()
    {
        var result = new ProcessResult(
            1603,
            string.Empty,
            "Errore SQL durante l'installazione Retail: il login SQL 'sa' non esiste, è disabilitato o non ha permessi per creare o popolare il database 'Test2DB' su 'VINCENZO-PC\\SQLPASS'. Verifica utente, password, Mixed Mode e permessi del login.");

        var detail = ExecutionErrorFormatter.BuildDetail("Passepartout Retail Server", result);

        Assert.Equal("Errore SQL durante l'installazione Retail: il login SQL 'sa' non esiste, è disabilitato o non ha permessi per creare o popolare il database 'Test2DB' su 'VINCENZO-PC\\SQLPASS'. Verifica utente, password, Mixed Mode e permessi del login.", detail);
    }

    [Fact]
    public void BuildMsiInstallCommandLine_PreservesRetailSqlInstanceName()
    {
        var method = typeof(App).GetMethod("BuildMsiInstallCommandLine", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var args = "IPSERVERDATABASE=localhost\\SQLPASS NOMEDATABASE=PassepartoutRetail PASSWORDDATABASE=Secret123!";
        var result = (string)method!.Invoke(null, [args])!;

        Assert.Contains("IPSERVERDATABASE=localhost\\SQLPASS", result, StringComparison.Ordinal);
        Assert.Contains("IpServerDatabase=localhost\\SQLPASS", result, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost,50261", result, StringComparison.OrdinalIgnoreCase);
    }
}
