using System.Security.Cryptography;

namespace Plutonium.Testing;

/// <summary>Hashes files and text with SHA-256 using the lock-file representation.</summary>
public static class Hashing
{
    /// <summary>Computes the lowercase hexadecimal SHA-256 hash of a file.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The 64-character lowercase hash.</returns>
    public static string Sha256File(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>Computes the lowercase hexadecimal SHA-256 hash of UTF-8 text.</summary>
    /// <param name="text">The text to hash.</param>
    /// <returns>The 64-character lowercase hash.</returns>
    public static string Sha256Text(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))
            .ToLowerInvariant();
    }
}
