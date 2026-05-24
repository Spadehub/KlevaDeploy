namespace KlevaDeploy.Services.Interfaces;

public interface IAuthService
{
    bool IsAuthenticated { get; }
    /// <summary>
    /// Performs HTTP POST login to the Passepartout "area riservata".
    /// Stores the auth cookie in the shared CookieContainer for the session.
    /// </summary>
    Task<bool> LoginAsync(string username, string password, CancellationToken ct = default);
    Task<bool> TryRestoreSessionAsync(CancellationToken ct = default);
    void Logout();
}
