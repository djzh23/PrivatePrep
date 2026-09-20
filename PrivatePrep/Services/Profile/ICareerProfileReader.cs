using PrivatePrep.Models;

namespace PrivatePrep.Services.Profile;

public readonly record struct CvFingerprint(string ContentHash, int ContentLength);

public interface ICareerProfileReader
{
    Task<CareerProfile?> GetProfile(string userId);
    Task<CvFingerprint?> GetCvFingerprintAsync(string userId, CancellationToken cancellationToken = default);
}
