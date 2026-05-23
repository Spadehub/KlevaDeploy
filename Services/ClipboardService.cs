using System.Windows;
using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.Services;

public sealed class ClipboardService : IClipboardService
{
    public void SetText(string text) => Clipboard.SetText(text);
}
