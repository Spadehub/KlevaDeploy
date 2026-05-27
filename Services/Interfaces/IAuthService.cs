using System;

namespace KlevaDeploy.Services.Interfaces;

public interface IAuthService
{
    bool IsAuthenticated { get; }
    int AuthenticatedPortalCount { get; }

    event EventHandler? AuthStateChanged;

    bool IsAuthenticatedForUrl(string url);
    bool IsAuthenticatedForPortalHomeUrl(string portalHomeUrl);
    /// <summary>
    /// Performs HTTP POST login to the Passepartout "area riservata".
    /// Stores the auth cookie in the shared CookieContainer for the session.
    /// </summary>
    Task<bool> LoginAsync(string username, string password, CancellationToken ct = default);
    Task<bool> LoginAsync(string username, string password, string portalHomeUrl, CancellationToken ct = default);
    Task<bool> TryRestoreSessionAsync(CancellationToken ct = default);
    void LogoutPortal(string portalHomeUrl);
    void Logout();
}
