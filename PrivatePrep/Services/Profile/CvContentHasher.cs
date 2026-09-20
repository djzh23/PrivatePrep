using System.Security.Cryptography;
using System.Text;

namespace PrivatePrep.Services.Profile;

/// <summary>SHA-256 hex fingerprint for CV text. Raw text is never stored.</summary>
public static class CvContentHasher
{
    public const int HexLength = 64;

    public static string Sha256Hex(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool IsSha256Hex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var s = value.Trim();
        if (s.Length != HexLength)
            return false;

        foreach (var c in s)
        {
            var ok = c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');
            if (!ok)
                return false;
        }

        return true;
    }

    public static bool HexEquals(string? left, string? right)
    {
        if (!IsSha256Hex(left) || !IsSha256Hex(right))
            return false;

        var a = left!.Trim().ToLowerInvariant();
        var b = right!.Trim().ToLowerInvariant();
        var leftBytes = Encoding.UTF8.GetBytes(a);
        var rightBytes = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
