using System.IO;
using System.Text.RegularExpressions;

namespace KlevaDeploy.Services;

internal static class LogSanitizer
{
    private static readonly Regex[] SecretPatterns =
    [
        new Regex(@"(?i)(/SAPWD=)(?:""[^""]*""|'[^']*'|[^\s;]+)", RegexOptions.Compiled),
        new Regex(@"(?i)(\bSAPWD=)(?:""[^""]*""|'[^']*'|[^\s;]+)", RegexOptions.Compiled),
        new Regex(@"(?i)(\bPASSWORDDATABASE=)(?:""[^""]*""|'[^']*'|[^\s;]+)", RegexOptions.Compiled),
        new Regex(@"(?i)(\bPASSWORD=)(?:""[^""]*""|'[^']*'|[^\s;]+)", RegexOptions.Compiled),
        new Regex(@"(?i)(\b(?:password|token|secret|client_secret|access_token|api_key|apikey)\s*[:=]\s*)(?:""[^""]*""|'[^']*'|[^\s,;]+)", RegexOptions.Compiled),
        new Regex(@"(?i)([?&](?:token|access_token|sig|signature|api_key|apikey|client_secret|password)=)[^&\s]+", RegexOptions.Compiled),
        new Regex(@"(?i)(\bAuthorization:\s*Bearer\s+)\S+", RegexOptions.Compiled),
        new Regex(@"(?i)(\bAuthorization:\s*Basic\s+)[A-Za-z0-9+/=]+", RegexOptions.Compiled)
    ];

    public static string Sanitize(string level, string? message)
    {
        _ = level;

        var text = (message ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        if (text.Length == 0)
            return string.Empty;

        var sanitizedLines = new List<string>();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = SanitizeLine(rawLine);
            if (!string.IsNullOrWhiteSpace(line))
                sanitizedLines.Add(line);
        }

        return string.Join(Environment.NewLine, sanitizedLines);
    }

    private static string SanitizeLine(string rawLine)
    {
        var line = (rawLine ?? string.Empty).TrimEnd();
        if (line.Length == 0)
            return string.Empty;

        if (line.StartsWith("[KlevaDeploy admin wrapper]", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        line = NormalizeDebugArtifactPath(line, "Admin process output captured at:");
        line = NormalizeDebugArtifactPath(line, "Admin wrapper script kept for debugging:");
        line = NormalizeDebugArtifactPath(line, "MSI-DEBUG append path:");

        foreach (var pattern in SecretPatterns)
            line = pattern.Replace(line, static match => $"{match.Groups[1].Value}*****");

        return line;
    }

    private static string NormalizeDebugArtifactPath(string line, string prefix)
    {
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return line;

        var rawPath = line[prefix.Length..].Trim();
        if (rawPath.Length == 0)
            return prefix;

        var trimmed = rawPath.Trim().Trim('"');
        var fileName = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(fileName)
            ? prefix
            : $"{prefix} {fileName}";
    }
}
