using KnxMonitor.Core.DTOs;

namespace KnxMonitor.Core.Interfaces;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request);
    Task<RefreshTokenResponse?> RefreshTokenAsync(RefreshTokenRequest request);
    Task<bool> LogoutAsync(string refreshToken);
    Task<bool> RevokeAllTokensAsync(int userId);
    Task<bool> NeedsInitialSetupAsync();
    Task<LoginResponse?> InitialSetupAsync(InitialSetupRequest request);
}
