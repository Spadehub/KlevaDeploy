using KlevaDeploy.Models;

namespace KlevaDeploy.Services.Interfaces;

public interface IPreferencesService
{
    UserPreferences Preferences { get; }
    void Save();
}

