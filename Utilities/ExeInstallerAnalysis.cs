using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KlevaDeploy.Utilities;

internal static class ExeInstallerAnalysis
{
    public static string? TryDetectExeInstallerFamily(string installerPath)
    {
        try
        {
            using var fs = File.OpenRead(installerPath);
            var max = 1024 * 1024;
            var buf = new byte[Math.Min(max, (int)Math.Min(fs.Length, max))];
            var read = fs.Read(buf, 0, buf.Length);
            if (read <= 0) return null;

            var text = Encoding.ASCII.GetString(buf, 0, read);
            if (text.Contains("Inno Setup Setup Data", StringComparison.OrdinalIgnoreCase))
                return "Inno Setup";
            if (text.Contains("Nullsoft", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("NSIS", StringComparison.OrdinalIgnoreCase))
                return "NSIS";
            if (text.Contains("InstallShield", StringComparison.OrdinalIgnoreCase))
                return "InstallShield";
            if (text.Contains("WixBundle", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Burn", StringComparison.OrdinalIgnoreCase))
                return "WiX Burn";

            return null;
        }
        catch
        {
            return null;
        }
    }

    public static int CountMsiPathsFrom7ZipSlt(string? stdout)
    {
        var text = stdout ?? string.Empty;
        if (text.Length == 0) return 0;

        string? currentPath = null;
        var count = 0;

        foreach (var raw in SplitNonEmptyLines(text))
        {
            var line = raw.Trim();
            if (line.StartsWith("Path = ", StringComparison.OrdinalIgnoreCase))
            {
                currentPath = line["Path = ".Length..].Trim();
                continue;
            }

            if (line.StartsWith("Attributes = ", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(currentPath) &&
                currentPath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    private static IEnumerable<string> SplitNonEmptyLines(string text)
    {
        if (string.IsNullOrEmpty(text)) yield break;

        using var reader = new StringReader(text);
        while (true)
        {
            var line = reader.ReadLine();
            if (line is null) yield break;
            if (line.Length == 0) continue;
            yield return line;
        }
    }
}
