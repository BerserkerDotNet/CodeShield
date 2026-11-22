using System.Security.Cryptography;

namespace CodeShield.Abstractions;

public static class Hashing
{
    public static string Sha256Hex(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

