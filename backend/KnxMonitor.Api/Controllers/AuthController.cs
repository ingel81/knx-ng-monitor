using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    [HttpPost("login")]
    [AllowAnonymous]
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

    [HttpPost("refresh")]
    [AllowAnonymous]
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

    [HttpGet("needs-setup")]
    [AllowAnonymous]
    public async Task<IActionResult> NeedsInitialSetup()
    {
        var needsSetup = await _authService.NeedsInitialSetupAsync();
        return Ok(new { needsSetup });
    }

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
