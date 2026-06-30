using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace KlevaDeploy.Editor;

public sealed class ScriptSyntaxColorizer : DocumentColorizingTransformer
{
    private static readonly Regex PowerShellKeywords = new(@"\b(param|if|else|elseif|foreach|for|while|switch|try|catch|finally|function|return|throw|Write-Output|Write-Warning|Write-Error|Start-Process|Get-ChildItem|Set-Content|Test-Path|Join-Path|New-Item)\b", RegexOptions.Compiled);
    private static readonly Regex BatchKeywords = new(@"\b(if|echo|set|setlocal|endlocal|call|goto|exit|for)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BashKeywords = new(@"\b(if|then|fi|for|do|done|case|esac|function|echo|export|grep|mkdir)\b", RegexOptions.Compiled);
    private static readonly Regex VariablePattern = new(@"(\$[\w:]+|%[\w_]+%)", RegexOptions.Compiled);
    private static readonly Regex StringPattern = new(@"'[^']*'|""[^""]*""", RegexOptions.Compiled);
    private static readonly Regex CommentPattern = new(@"#.*$|::.*$", RegexOptions.Compiled | RegexOptions.Multiline);

    private string _language = "text";

    public void SetLanguage(string language)
    {
        _language = language ?? "text";
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        var text = CurrentContext.Document.GetText(line);
        ApplyRegex(CommentPattern, text, line.Offset, Brushes.SeaGreen, FontStyles.Italic);
        ApplyRegex(StringPattern, text, line.Offset, Brushes.IndianRed, FontStyles.Normal);
        ApplyRegex(VariablePattern, text, line.Offset, Brushes.SteelBlue, FontStyles.Normal);

        var keywordRegex = _language switch
        {
            "powershell" => PowerShellKeywords,
            "batch" => BatchKeywords,
            "bash" => BashKeywords,
            _ => null
        };

        if (keywordRegex is not null)
            ApplyRegex(keywordRegex, text, line.Offset, Brushes.MediumPurple, FontStyles.Normal, FontWeights.SemiBold);
    }

    private void ApplyRegex(Regex regex, string lineText, int lineOffset, Brush foreground, FontStyle fontStyle, FontWeight? fontWeight = null)
    {
        foreach (Match match in regex.Matches(lineText))
        {
            if (!match.Success || match.Length == 0) continue;
            ChangeLinePart(
                lineOffset + match.Index,
                lineOffset + match.Index + match.Length,
                element =>
                {
                    element.TextRunProperties.SetForegroundBrush(foreground);
                    element.TextRunProperties.SetTypeface(new Typeface(
                        element.TextRunProperties.Typeface.FontFamily,
                        fontStyle,
                        fontWeight ?? element.TextRunProperties.Typeface.Weight,
                        element.TextRunProperties.Typeface.Stretch));
                });
        }
    }
}
