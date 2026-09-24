using PrivatePrep.Services.Profile;

namespace PrivatePrep.Services.Reports;

/// <summary>
/// SHA-256 hex fingerprint for arbitrary text (CV or job description), used to detect whether the
/// input to an analysis changed since the last one. Delegates the actual hashing to
/// <see cref="CvContentHasher"/> (already SHA-256 hex, lowercase) instead of duplicating it — the
/// only difference here is empty/null input returns an empty string rather than the hash of "".
/// </summary>
public static class TextHasher
{
    public static string ComputeHash(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return CvContentHasher.Sha256Hex(text);
    }
}
