using KlevaDeploy.Models;
using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.Services;

public sealed class PreferencesService : IPreferencesService
{
    public UserPreferences Preferences { get; }

    public PreferencesService()
    {
        Preferences = UserPreferences.Load();
    }

    public void Save() => Preferences.Save();
}

