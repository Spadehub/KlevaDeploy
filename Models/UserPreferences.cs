using System.IO;
using System.Text.Json;

namespace KlevaDeploy.Models;

public enum AppTheme { Dark, Light }

public class UserPreferences
{
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public bool SuppressRequiredProcessWarning { get; set; } = false;

    private static readonly string StoragePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KlevaDeploy",
        "user_preferences.json");

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
