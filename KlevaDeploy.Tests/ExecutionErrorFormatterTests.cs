using KlevaDeploy.Services;
using KlevaDeploy.Services.Interfaces;

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
}
