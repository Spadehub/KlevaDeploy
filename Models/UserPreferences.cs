using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace KlevaDeploy.Models;

public enum AppTheme { Dark, Light }
public enum AppThemeStyle { Default, FluentClean }
public enum PresetsViewMode { List, Grid }

public class UserPreferences
{
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public AppThemeStyle ThemeStyle { get; set; } = AppThemeStyle.Default;
    public bool SuppressRequiredProcessWarning { get; set; } = false;
    public PresetsViewMode PresetsViewMode { get; set; } = PresetsViewMode.List;
    public string? SelectedPortalId { get; set; }
    public List<PortalPreference> Portals { get; set; } = new();
    public List<ProcessArgumentProfile> ProcessArgumentProfiles { get; set; } = new();
    public Dictionary<string, int> ProcessOrderOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string? SelectedPortalHomeUrl { get; set; }
    public List<string> RecentPortalHomeUrls { get; set; } = new();

    private static readonly string StoragePath = Path.Combine(GetStorageDir(), "user_preferences.json");
    private static readonly string DefaultPortalsPath = Path.Combine(GetStorageDir(), "Defaults", "portals.json");

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
                prefs.ProcessArgumentProfiles ??= new();
                prefs.ProcessOrderOverrides = prefs.ProcessOrderOverrides is null
                    ? new(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, int>(prefs.ProcessOrderOverrides, StringComparer.OrdinalIgnoreCase);

                prefs.Portals ??= new();
                if (prefs.Portals.Count == 0)
                {
                    prefs.Portals = LoadDefaultPortals().ToList();
                }

                if (prefs.Portals.Count > 0)
                {
                    var fallbackHomeUrl = (prefs.Portals[0].HomeUrl ?? string.Empty).Trim();
                    prefs.SelectedPortalHomeUrl = string.IsNullOrWhiteSpace(prefs.SelectedPortalHomeUrl)
                        ? fallbackHomeUrl
                        : prefs.SelectedPortalHomeUrl.Trim();

                    prefs.RecentPortalHomeUrls = prefs.RecentPortalHomeUrls
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (!string.IsNullOrWhiteSpace(fallbackHomeUrl) &&
                        !prefs.RecentPortalHomeUrls.Any(x => string.Equals(x, fallbackHomeUrl, StringComparison.OrdinalIgnoreCase)))
                    {
                        prefs.RecentPortalHomeUrls.Insert(0, fallbackHomeUrl);
                    }
                }

                foreach (var portal in prefs.Portals)
                {
                    if (portal is null) continue;
                    portal.Id = string.IsNullOrWhiteSpace(portal.Id) ? Guid.NewGuid().ToString("N") : portal.Id;
                    portal.Name = string.IsNullOrWhiteSpace(portal.Name) ? GuessPortalName(portal.HomeUrl) : portal.Name;
                    portal.HomeUrl = (portal.HomeUrl ?? string.Empty).Trim();
                }

                if (string.IsNullOrWhiteSpace(prefs.SelectedPortalId))
                {
                    var byHomeUrl = prefs.Portals.FirstOrDefault(p =>
                        string.Equals((p.HomeUrl ?? string.Empty).Trim(), (prefs.SelectedPortalHomeUrl ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
                    prefs.SelectedPortalId = byHomeUrl?.Id ?? prefs.Portals[0].Id;
                }
                return prefs;
            }
        }
        catch { /* Fallback to defaults */ }
        return CreateNewWithDefaults();
    }

    private static string GuessPortalName(string? homeUrl)
    {
        if (string.IsNullOrWhiteSpace(homeUrl)) return "Portale";
        if (!Uri.TryCreate(homeUrl.Trim(), UriKind.Absolute, out var uri)) return "Portale";
        if (string.Equals(uri.Host, "download.passepartout.cloud", StringComparison.OrdinalIgnoreCase)) return "Passepartout";
        return uri.Host;
    }

    private static UserPreferences CreateNewWithDefaults()
    {
        var portals = LoadDefaultPortals().ToList();
        var selected = portals.FirstOrDefault();
        var selectedHomeUrl = (selected?.HomeUrl ?? string.Empty).Trim();

        return new UserPreferences
        {
            Portals = portals,
            SelectedPortalId = selected?.Id,
            SelectedPortalHomeUrl = string.IsNullOrWhiteSpace(selectedHomeUrl) ? null : selectedHomeUrl,
            RecentPortalHomeUrls = string.IsNullOrWhiteSpace(selectedHomeUrl) ? new List<string>() : new List<string> { selectedHomeUrl }
        };
    }

    private static IReadOnlyList<PortalPreference> LoadDefaultPortals()
    {
        try
        {
            if (!File.Exists(DefaultPortalsPath))
                return Array.Empty<PortalPreference>();

            var json = File.ReadAllText(DefaultPortalsPath);
            var portals = JsonSerializer.Deserialize<List<PortalPreference>>(json) ?? new();
            foreach (var p in portals)
            {
                if (p is null) continue;
                p.Id = string.IsNullOrWhiteSpace(p.Id) ? Guid.NewGuid().ToString("N") : p.Id.Trim();
                p.Name = string.IsNullOrWhiteSpace(p.Name) ? GuessPortalName(p.HomeUrl) : p.Name.Trim();
                p.HomeUrl = (p.HomeUrl ?? string.Empty).Trim();
            }

            return portals.Where(p => p is not null && !string.IsNullOrWhiteSpace(p.HomeUrl)).ToList();
        }
        catch
        {
            return Array.Empty<PortalPreference>();
        }
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

public sealed class ProcessArgumentProfile
{
    public string ProcessId { get; set; } = string.Empty;
    public string SchemaHash { get; set; } = string.Empty;
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool LastRunFailed { get; set; }
}
