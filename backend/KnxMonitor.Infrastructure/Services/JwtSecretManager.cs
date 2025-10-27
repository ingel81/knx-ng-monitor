using System.Security.Cryptography;

namespace KnxMonitor.Infrastructure.Services;

/// <summary>
/// Manages JWT secret generation and persistence
/// </summary>
public static class JwtSecretManager
{
    private const string SecretFileName = ".jwt-secret";
    private const int SecretLengthBytes = 64; // 512 bits

    /// <summary>
    /// Gets or generates a JWT secret.
    /// Secret is stored in ./data/.jwt-secret and reused across restarts.
    /// </summary>
    /// <param name="dataDirectory">Directory where secret file is stored (default: ./data)</param>
    /// <returns>Base64-encoded secret string (min 64 characters)</returns>
    public static string GetOrGenerateSecret(string dataDirectory = "./data")
    {
        // Ensure data directory exists
        if (!Directory.Exists(dataDirectory))
        {
            Directory.CreateDirectory(dataDirectory);
        }

        var secretFilePath = Path.Combine(dataDirectory, SecretFileName);

        // Try to read existing secret
        if (File.Exists(secretFilePath))
        {
            try
            {
                var existingSecret = File.ReadAllText(secretFilePath).Trim();

                // Validate existing secret (must be at least 32 characters)
                if (!string.IsNullOrWhiteSpace(existingSecret) && existingSecret.Length >= 32)
                {
                    return existingSecret;
                }
            }
            catch
            {
                // If reading fails, generate new secret below
            }
        }

        // Generate new cryptographically secure secret
        var secretBytes = new byte[SecretLengthBytes];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(secretBytes);
        }

        var secret = Convert.ToBase64String(secretBytes);

        // Save secret to file
        try
        {
            File.WriteAllText(secretFilePath, secret);

            // Set file permissions (Unix only - no-op on Windows)
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(secretFilePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite); // 600
            }
        }
        catch (Exception ex)
        {
            // Log warning but continue with generated secret
            Console.WriteLine($"Warning: Could not save JWT secret to file: {ex.Message}");
        }

        return secret;
    }
}
