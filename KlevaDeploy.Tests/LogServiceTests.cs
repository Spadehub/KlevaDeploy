using KlevaDeploy.Services;

namespace KlevaDeploy.Tests;

public sealed class LogServiceTests
{
    [Fact]
    public void AppendRaw_RedactsSensitiveValues()
    {
        var log = new LogService();

        log.AppendRaw("CMD", @"PASSWORDDATABASE=Secret123 SAPWD=""AnotherSecret!"" token=abcdef");

        var entry = Assert.Single(log.Entries);
        Assert.Equal("CMD", entry.Level);
        Assert.Contains("PASSWORDDATABASE=*****", entry.Message, StringComparison.Ordinal);
        Assert.Contains("SAPWD=*****", entry.Message, StringComparison.Ordinal);
        Assert.Contains("token=*****", entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret123", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("AnotherSecret!", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("abcdef", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AppendRaw_SuppressesAdminWrapperBanner()
    {
        var log = new LogService();

        log.AppendRaw("STDOUT", "[KlevaDeploy admin wrapper] 2026-07-06T16:47:06");

        Assert.Empty(log.Entries);
    }

    [Fact]
    public void AppendRaw_NormalizesDebugArtifactPaths()
    {
        var log = new LogService();

        log.AppendRaw("STDOUT", @"Admin wrapper script kept for debugging: E:\Temp\KlevaDeploy_123.admin.ps1");

        var entry = Assert.Single(log.Entries);
        Assert.Equal("Admin wrapper script kept for debugging: KlevaDeploy_123.admin.ps1", entry.Message);
    }

    [Fact]
    public void Error_SanitizesExceptionMessage()
    {
        var log = new LogService();

        log.Error("Install failed", new InvalidOperationException("Authorization: Bearer super-secret-token"));

        var entry = Assert.Single(log.Entries);
        Assert.Equal("ERROR", entry.Level);
        Assert.Contains("Authorization: Bearer *****", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-token", entry.Message, StringComparison.Ordinal);
    }
}
