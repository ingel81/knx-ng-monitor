using System.Security.Cryptography;
using System.Text;

namespace KnxMonitor.ProjectParser.Helpers;

internal static class KeyringCrypto
{
    private static readonly byte[] KeyringSalt = Encoding.ASCII.GetBytes("1.keyring.ets.knx.org");

    public static byte[] HashPassword(string password)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        using var pbkdf2 = new Rfc2898DeriveBytes(passwordBytes, KeyringSalt, 65536, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(16);
    }

    public static byte[] DeriveIv(string createdAttribute)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(createdAttribute));
        var iv = new byte[16];
        Buffer.BlockCopy(digest, 0, iv, 0, 16);
        return iv;
    }

    public static byte[] DecryptAes128Cbc(byte[] ciphertext, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.Key = key;
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
    }

    /// <summary>
    /// Extracts a password/string value from a decrypted AES-CBC block.
    /// Format: first 8 bytes = random nonce, last byte = padding length, rest = UTF-8 string.
    /// </summary>
    public static string ExtractPasswordString(byte[] decrypted)
    {
        if (decrypted.Length == 0) return string.Empty;
        int padLen = decrypted[^1];
        int start = 8;
        int end = decrypted.Length - padLen;
        if (end < start) return string.Empty;
        return Encoding.UTF8.GetString(decrypted, start, end - start);
    }
}
