namespace KlevaDeploy.Services.Interfaces;

public interface IAppConfigService
{
    AppConfig Config { get; }
}

public sealed class AppConfig
{
    public InstallerServiceConfig InstallerService { get; set; } = new();
    public AuthServiceConfig AuthService { get; set; } = new();
    public LicenseScraperServiceConfig LicenseScraperService { get; set; } = new();
    public AppUpdateServiceConfig AppUpdateService { get; set; } = new();
    public MsiConfig Msi { get; set; } = new();
}

public sealed class InstallerServiceConfig
{
    public InstallerNormalizationConfig Normalization { get; set; } = new();
}

public sealed class InstallerNormalizationConfig
{
    public string SqlPassFullInstallerUrl { get; set; } = string.Empty;
    public string SqlPassFullInstallerFileName { get; set; } = string.Empty;
    public string RetailDefaultArgs { get; set; } = string.Empty;
    public string DefaultSqlPassSaPassword { get; set; } = string.Empty;
    public string DefaultRetailSqlServer { get; set; } = string.Empty;
    public string DefaultRetailSqlUser { get; set; } = string.Empty;
    public string DefaultRetailDbName { get; set; } = string.Empty;
}

public sealed class AuthServiceConfig
{
    public string LoginPageUrl { get; set; } = string.Empty;
    public string LoginPostUrl { get; set; } = string.Empty;
    public string DownloadsHomeUrl { get; set; } = string.Empty;
}

public sealed class LicenseScraperServiceConfig
{
    public string LicenseExcelUrl { get; set; } = string.Empty;
}

public sealed class AppUpdateServiceConfig
{
    public string Owner { get; set; } = string.Empty;
    public string Repo { get; set; } = string.Empty;
    public string LegacyRepo { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public string TokenEnvVar { get; set; } = string.Empty;
}

public sealed class MsiConfig
{
    public string SecureCustomPropertiesKey { get; set; } = string.Empty;
}
