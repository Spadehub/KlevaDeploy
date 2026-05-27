using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace KlevaDeploy.Models;

public enum AppTheme { Dark, Light }
public enum PresetsViewMode { List, Grid }

public class UserPreferences
{
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public bool SuppressRequiredProcessWarning { get; set; } = false;
    public PresetsViewMode PresetsViewMode { get; set; } = PresetsViewMode.List;
    public string? SelectedPortalId { get; set; }
    public List<PortalPreference> Portals { get; set; } = new()
    {
        new PortalPreference
        {
            Id = "passepartout",
            Name = "Passepartout",
            HomeUrl = "https://download.passepartout.cloud/."
        }
    };

    public string SelectedPortalHomeUrl { get; set; } = "https://download.passepartout.cloud/.";
    public List<string> RecentPortalHomeUrls { get; set; } = new() { "https://download.passepartout.cloud/." };

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
                var prefs = JsonSerializer.Deserialize<UserPreferences>(json) ?? new UserPreferences();
                prefs.RecentPortalHomeUrls ??= new();
                if (prefs.RecentPortalHomeUrls.Count == 0)
                    prefs.RecentPortalHomeUrls.Add("https://download.passepartout.cloud/.");
                if (string.IsNullOrWhiteSpace(prefs.SelectedPortalHomeUrl))
                    prefs.SelectedPortalHomeUrl = prefs.RecentPortalHomeUrls[0];

                prefs.Portals ??= new();
                if (prefs.Portals.Count == 0)
                {
                    foreach (var url in prefs.RecentPortalHomeUrls)
                    {
                        var trimmed = (url ?? string.Empty).Trim();
                        if (string.IsNullOrWhiteSpace(trimmed)) continue;
                        prefs.Portals.Add(new PortalPreference
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Name = GuessPortalName(trimmed),
                            HomeUrl = trimmed
                        });
                    }
                }

                if (prefs.Portals.Count == 0)
                {
                    prefs.Portals.Add(new PortalPreference
                    {
                        Id = "passepartout",
                        Name = "Passepartout",
                        HomeUrl = "https://download.passepartout.cloud/."
                    });
                }

                foreach (var portal in prefs.Portals)
                {
                    if (portal is null) continue;
                    portal.Id = string.IsNullOrWhiteSpace(portal.Id) ? Guid.NewGuid().ToString("N") : portal.Id;
                    portal.Name = string.IsNullOrWhiteSpace(portal.Name) ? GuessPortalName(portal.HomeUrl) : portal.Name;
                    portal.HomeUrl = string.IsNullOrWhiteSpace(portal.HomeUrl) ? "https://download.passepartout.cloud/." : portal.HomeUrl;
                }

                if (string.IsNullOrWhiteSpace(prefs.SelectedPortalId))
                {
                    var byHomeUrl = prefs.Portals.FirstOrDefault(p =>
                        string.Equals((p.HomeUrl ?? string.Empty).Trim(), prefs.SelectedPortalHomeUrl.Trim(), StringComparison.OrdinalIgnoreCase));
                    prefs.SelectedPortalId = byHomeUrl?.Id ?? prefs.Portals[0].Id;
                }
                return prefs;
            }
        }
        catch { /* Fallback to defaults */ }
        return new UserPreferences();
    }

    private static string GuessPortalName(string? homeUrl)
    {
        if (string.IsNullOrWhiteSpace(homeUrl)) return "Portale";
        if (!Uri.TryCreate(homeUrl.Trim(), UriKind.Absolute, out var uri)) return "Portale";
        if (string.Equals(uri.Host, "download.passepartout.cloud", StringComparison.OrdinalIgnoreCase)) return "Passepartout";
        return uri.Host;
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

public sealed class PortalPreference
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Portale";
    public string HomeUrl { get; set; } = "https://download.passepartout.cloud/.";
    public string? LastUsername { get; set; }
    public string? LogoLightPath { get; set; }
    public string? LogoDarkPath { get; set; }
}
