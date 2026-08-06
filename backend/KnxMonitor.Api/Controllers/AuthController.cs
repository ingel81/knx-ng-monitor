using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using KnxMonitor.Core.DTOs;
using KnxMonitor.Core.Interfaces;

namespace KnxMonitor.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    /// <summary>Authenticates a user and issues an access token plus a refresh token.</summary>
    /// <remarks>
    /// Open to anonymous callers and rate limited to 10 requests per minute (fixed window,
    /// shared by all callers of the auth endpoints). Token lifetimes come from configuration:
    /// <c>Jwt:AccessTokenExpirationMinutes</c> (15) and <c>Jwt:RefreshTokenExpirationDays</c> (7).
    /// Only a SHA-256 hash of the refresh token is persisted, so the raw value is returned here once.
    /// </remarks>
    /// <returns>Access token, refresh token, the access token's UTC expiry and the username.</returns>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Username and password are required" });
        }

        var response = await _authService.LoginAsync(request);

        if (response == null)
        {
            _logger.LogWarning("Failed login attempt for user: {Username}", request.Username);
            return Unauthorized(new { message = "Invalid username or password" });
        }

        _logger.LogInformation("User {Username} logged in successfully", request.Username);
        return Ok(response);
    }

    /// <summary>Exchanges a refresh token for a new access token and a new refresh token.</summary>
    /// <remarks>
    /// Open to anonymous callers (the refresh token itself is the credential) and rate limited
    /// like login. Refresh tokens rotate: the presented token is revoked and its replacement
    /// stored in the same save, so a token can only be redeemed once. Presenting an already
    /// rotated token is treated as theft and revokes every refresh token of that user.
    /// </remarks>
    /// <returns>A new access token, a new refresh token and the access token's UTC expiry.</returns>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return BadRequest(new { message = "Refresh token is required" });
        }

        var response = await _authService.RefreshTokenAsync(request);

        if (response == null)
        {
            _logger.LogWarning("Failed refresh token attempt");
            return Unauthorized(new { message = "Invalid or expired refresh token" });
        }

        return Ok(response);
    }

    /// <summary>Revokes the supplied refresh token, ending that one session.</summary>
    /// <remarks>
    /// Other sessions of the same user keep working. Access tokens are validated by signature
    /// and lifetime only, so an already issued access token stays usable until it expires.
    /// </remarks>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return BadRequest(new { message = "Refresh token is required" });
        }

        var success = await _authService.LogoutAsync(request.RefreshToken);

        if (!success)
        {
            return BadRequest(new { message = "Logout failed" });
        }

        _logger.LogInformation("User logged out successfully");
        return Ok(new { message = "Logged out successfully" });
    }

    /// <summary>Revokes every refresh token of the calling user, ending all their sessions.</summary>
    /// <remarks>
    /// The user is taken from the access token's subject claim. As with a single logout,
    /// access tokens already handed out remain valid until they expire.
    /// </remarks>
    [HttpPost("logout-all")]
    [Authorize]
    public async Task<IActionResult> LogoutAll()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value;
        if (!int.TryParse(sub, out var userId))
        {
            return Unauthorized();
        }

        await _authService.RevokeAllTokensAsync(userId);
        _logger.LogInformation("All sessions revoked for user {UserId}", userId);
        return Ok(new { message = "All sessions revoked" });
    }

    /// <summary>Reports whether this instance still has no user account and needs the initial setup.</summary>
    /// <returns><c>needsSetup</c> — true while not a single user exists.</returns>
    [HttpGet("needs-setup")]
    [AllowAnonymous]
    public async Task<IActionResult> NeedsInitialSetup()
    {
        var needsSetup = await _authService.NeedsInitialSetupAsync();
        return Ok(new { needsSetup });
    }

    /// <summary>Creates the first user account and signs it in.</summary>
    /// <remarks>
    /// Open to anonymous callers, but only while the instance has no user at all — once one
    /// exists the call is rejected. The password must be at least 8 characters long.
    /// </remarks>
    /// <returns>The same token set as a login: access token, refresh token, expiry and username.</returns>
    [HttpPost("setup")]
    [AllowAnonymous]
    public async Task<IActionResult> InitialSetup([FromBody] InitialSetupRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Username and password are required" });
        }

        if (request.Password.Length < 8)
        {
            return BadRequest(new { message = "Password must be at least 8 characters long" });
        }

        var response = await _authService.InitialSetupAsync(request);

        if (response == null)
        {
            return BadRequest(new { message = "Initial setup already completed or invalid data" });
        }

        _logger.LogInformation("Initial setup completed for user: {Username}", request.Username);
        return Ok(response);
    }
}
