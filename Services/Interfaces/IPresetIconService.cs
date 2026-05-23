using KlevaDeploy.Models;

namespace KlevaDeploy.Services.Interfaces;

public interface IPresetIconService
{
    string ImportLightIcon(string presetId, string sourcePath);
    string ImportDarkIcon(string presetId, string sourcePath);
    void DeletePresetIcons(string presetId);
    IReadOnlyList<PresetIconLibraryItem> GetLibraryIcons();
    PresetIconLibraryItem ImportLibraryIcon(string sourcePath);
}
