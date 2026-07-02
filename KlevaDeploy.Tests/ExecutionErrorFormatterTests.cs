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
}
