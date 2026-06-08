using System.IO;
using System.Reflection;
using System.Text.Json;
using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.Services;

public sealed class AppConfigService : IAppConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AppConfig Config { get; }

    public AppConfigService()
    {
        Config = LoadConfig();
    }

    private static AppConfig LoadConfig()
    {
        var path = ResolveConfigPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return LoadBundledFallback();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    private static string ResolveConfigPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("KLEVADEPLOY_CONFIG_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath.Trim();

        var storageDir = Environment.GetEnvironmentVariable("KLEVADEPLOY_STORAGE_DIR");
        if (string.IsNullOrWhiteSpace(storageDir))
            storageDir = Path.Combine(AppContext.BaseDirectory, "Data");

        var fromData = Path.Combine(storageDir, "appsettings.json");
        if (File.Exists(fromData)) return fromData;

        return Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    }

    private static AppConfig LoadBundledFallback()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var s = asm.GetManifestResourceStream("KlevaDeploy.appsettings.json");
            if (s is null) return new AppConfig();
            using var sr = new StreamReader(s);
            var json = sr.ReadToEnd();
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }
}
