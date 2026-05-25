using System.IO;
using System.Text.Json;

namespace KlevaDeploy.Models;

public sealed class InstallerUpdateState
{
    public Dictionary<string, InstallerUpdateStateEntry> Entries { get; set; } = new();

    public static InstallerUpdateState Load(string storageDir)
    {
        try
        {
            var path = GetPath(storageDir);
            if (!File.Exists(path)) return new InstallerUpdateState();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<InstallerUpdateState>(json) ?? new InstallerUpdateState();
        }
        catch
        {
            return new InstallerUpdateState();
        }
    }

    public void Save(string storageDir)
    {
        var path = GetPath(storageDir);
        var dir = Path.GetDirectoryName(path);
        if (dir is not null) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private static string GetPath(string storageDir) => Path.Combine(storageDir, "installer_update_state.json");
}

public sealed class InstallerUpdateStateEntry
{
    public string LastKnownEtag { get; set; } = string.Empty;
    public DateTimeOffset? LastKnownLastModifiedUtc { get; set; }
    public DateTimeOffset? LastCheckedUtc { get; set; }
    public DateTimeOffset? LastDownloadedUtc { get; set; }
    public long? LastDownloadedBytes { get; set; }
    public string LastResolvedDownloadUrl { get; set; } = string.Empty;
    public string LastDownloadedFromUrl { get; set; } = string.Empty;
}

