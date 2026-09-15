using PrivatePrep.Models;

namespace PrivatePrep.Services.Profile;

public interface ICareerProfileReader
{
    Task<CareerProfile?> GetProfile(string userId);
    Task<string?> GetCvRawTextAsync(string userId, CancellationToken cancellationToken = default);
}
