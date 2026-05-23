using System.IO;
using System.Text.Json;

namespace KlevaDeploy.Models;

public enum AppTheme { Dark, Light }
public enum PresetsViewMode { List, Grid }

public class UserPreferences
{
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public bool SuppressRequiredProcessWarning { get; set; } = false;
    public PresetsViewMode PresetsViewMode { get; set; } = PresetsViewMode.List;

    private static readonly string StoragePath = Path.Combine(GetStorageDir(), "user_preferences.json");

    private static string GetStorageDir()
    {
        var overrideDir = Environment.GetEnvironmentVariable("KLEVADEPLOY_STORAGE_DIR");
        return string.IsNullOrWhiteSpace(overrideDir)
            ? Path.Combine(AppContext.BaseDirectory, "Data")
            : overrideDir;
    }

    public static UserPreferences Load()
    {
        try
        {
            if (File.Exists(StoragePath))
            {
                var json = File.ReadAllText(StoragePath);
                return JsonSerializer.Deserialize<UserPreferences>(json) ?? new UserPreferences();
            }
        }
        catch { /* Fallback to defaults */ }
        return new UserPreferences();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(StoragePath);
            if (dir != null) Directory.CreateDirectory(dir);
            
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StoragePath, json);
        }
        catch { /* Ignore save errors */ }
    }
}
