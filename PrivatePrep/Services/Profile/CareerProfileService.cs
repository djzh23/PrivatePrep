using PrivatePrep.Data;
using PrivatePrep.Models;

namespace PrivatePrep.Services.Profile;

public readonly record struct CareerProfileBackendInfo(
    string EffectiveStorage,
    string ConfiguredCareerProfileStorage,
    bool Degraded,
    string? DegradedReason);

/// <summary>Career profile persistence (PostgreSQL only in V1).</summary>
public sealed class CareerProfileService(CareerProfilePostgresService postgres) : ICareerProfileReader
{
    public CareerProfileBackendInfo GetBackendInfo() =>
        new("postgres", "postgres", Degraded: false, DegradedReason: null);

    public Task<CareerProfile?> GetProfile(string userId) => postgres.GetProfile(userId);

    public Task<CvFingerprint?> GetCvFingerprintAsync(string userId, CancellationToken cancellationToken = default) =>
        postgres.GetCvFingerprintAsync(userId, cancellationToken);

    public Task SaveProfile(string userId, CareerProfile profile) => postgres.SaveProfile(userId, profile);

    public Task SetOnboarding(
        string userId,
        string field,
        string fieldLabel,
        string level,
        string levelLabel,
        string? currentRole,
        List<string> goals) =>
        postgres.SetOnboarding(userId, field, fieldLabel, level, levelLabel, currentRole, goals);

    public Task SkipOnboardingAsync(string userId) => postgres.SkipOnboardingAsync(userId);

    public Task SetCvFingerprintAsync(string userId, string contentHash, int contentLength, CancellationToken cancellationToken = default) =>
        postgres.SetCvFingerprintAsync(userId, contentHash, contentLength, cancellationToken);

    public Task ClearCvDerivedDataAsync(string userId, CancellationToken cancellationToken = default) =>
        postgres.ClearCvDerivedDataAsync(userId, cancellationToken);

    public Task SetSkills(string userId, List<string> skills) => postgres.SetSkills(userId, skills);

    public Task<string> AddTargetJob(string userId, string title, string? company, string? description) =>
        postgres.AddTargetJob(userId, title, company, description);

    public Task RemoveTargetJob(string userId, string jobId) => postgres.RemoveTargetJob(userId, jobId);

    public Task<OnboardingDraft?> GetOnboardingDraftAsync(string userId) =>
        postgres.GetOnboardingDraftAsync(userId);

    public Task SaveOnboardingDraftAsync(string userId, OnboardingDraft draft) =>
        postgres.SaveOnboardingDraftAsync(userId, draft);

    public Task SetCoachTourCompletedAsync(string userId) => postgres.SetCoachTourCompletedAsync(userId);

    public string BuildProfileContext(CareerProfile profile, ProfileContextToggles toggles) =>
        CareerProfileContextBuilder.Build(profile, toggles);

    public Task BumpProfileCacheVersionAsync(string userId, CancellationToken cancellationToken = default) =>
        postgres.BumpProfileCacheVersionAsync(userId, cancellationToken);

    public Task<string> GetProfileCacheVersionAsync(string userId, CancellationToken cancellationToken = default) =>
        postgres.GetProfileCacheVersionAsync(userId, cancellationToken);
}
