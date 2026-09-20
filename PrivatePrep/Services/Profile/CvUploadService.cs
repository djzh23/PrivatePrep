namespace PrivatePrep.Services.Profile;

public interface ICvUploadService
{
    Task<CvUploadResult> RegisterTextAsync(string cvText, string userId, CancellationToken cancellationToken = default);
}

public sealed record CvUploadResult(
    string ContentHash,
    int ContentLength,
    string ExtractedText);

public sealed class CvUploadService(CareerProfileService profiles) : ICvUploadService
{
    public async Task<CvUploadResult> RegisterTextAsync(
        string cvText,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var text = (cvText ?? string.Empty).Trim();
        if (text.Length == 0)
            throw new InvalidOperationException("CV-Text darf nicht leer sein.");

        if (text.Length > CareerProfileStorageLimits.CvRawSeparateKeyMax)
            text = text[..CareerProfileStorageLimits.CvRawSeparateKeyMax];

        var hash = CvContentHasher.Sha256Hex(text);
        await profiles.SetCvFingerprintAsync(userId, hash, text.Length, cancellationToken).ConfigureAwait(false);
        return new CvUploadResult(hash, text.Length, text);
    }
}
