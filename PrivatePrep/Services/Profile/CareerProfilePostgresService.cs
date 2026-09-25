using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PrivatePrep.Data;
using PrivatePrep.Data.Entities;
using PrivatePrep.Models;

namespace PrivatePrep.Services.Profile;

/// <summary>Career profiles in Supabase/PostgreSQL via EF Core.</summary>
public sealed class CareerProfilePostgresService(PrivatePrepDbContext db)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<CareerProfile?> GetProfile(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        var row = await db.CareerProfiles
            .AsNoTracking()
            .Where(x => x.ClerkUserId == userId)
            .Select(x => new { x.ProfileJson, x.CreatedAt, x.CvContentHash, x.CvContentLength })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
            return null;

        return DeserializeProfile(row.ProfileJson, userId, row.CvContentHash, row.CvContentLength);
    }

    public async Task<CvFingerprint?> GetCvFingerprintAsync(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        var row = await db.CareerProfiles
            .AsNoTracking()
            .Where(x => x.ClerkUserId == userId)
            .Select(x => new { x.CvContentHash, x.CvContentLength })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null || string.IsNullOrWhiteSpace(row.CvContentHash))
            return null;

        return new CvFingerprint(row.CvContentHash.Trim(), row.CvContentLength ?? 0);
    }

    public async Task SaveProfile(string userId, CareerProfile profile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);


        var existing = await db.CareerProfiles
            .FirstOrDefaultAsync(x => x.ClerkUserId == userId, cancellationToken)
            .ConfigureAwait(false);

        profile.UserId = userId;
        profile.UpdatedAt = DateTime.UtcNow;
        if (existing is not null)
        {
            if (profile.CreatedAt == default)
                profile.CreatedAt = existing.CreatedAt;
        }
        else if (profile.CreatedAt == default)
            profile.CreatedAt = DateTime.UtcNow;

        StripNonPersistedCvFields(profile);
        profile.CvSummary = Truncate(profile.CvSummary, CareerProfileStorageLimits.CvSummaryMaxChars);
        profile.CvSummaryEn = Truncate(profile.CvSummaryEn, CareerProfileStorageLimits.CvSummaryMaxChars);
        foreach (var job in profile.TargetJobs)
        {
            job.Description = Truncate(job.Description, CareerProfileStorageLimits.TargetJobDescriptionMax);
        }

        var now = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(profile, JsonOpts);

        if (existing is null)
        {
            db.CareerProfiles.Add(new CareerProfileEntity
            {
                ClerkUserId = userId,
                CreatedAt = profile.CreatedAt,
                UpdatedAt = now,
                ProfileJson = json,
                CacheVersion = 1,
            });
        }
        else
        {
            existing.UpdatedAt = now;
            existing.ProfileJson = json;
            existing.CacheVersion = existing.CacheVersion + 1;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetOnboarding(
        string userId,
        string field,
        string fieldLabel,
        string level,
        string levelLabel,
        string? currentRole,
        List<string> goals,
        CancellationToken cancellationToken = default)
    {
        var profile = await GetProfile(userId, cancellationToken).ConfigureAwait(false)
            ?? new CareerProfile { UserId = userId, CreatedAt = DateTime.UtcNow };
        profile.Field = field;
        profile.FieldLabel = fieldLabel;
        profile.Level = level;
        profile.LevelLabel = levelLabel;
        profile.CurrentRole = currentRole;
        profile.Goals = goals;
        profile.OnboardingCompleted = true;
        profile.OnboardingDraft = null; // clear draft after completion
        await SaveProfile(userId, profile, cancellationToken).ConfigureAwait(false);
    }

    public async Task SkipOnboardingAsync(string userId, CancellationToken cancellationToken = default)
    {
        var profile = await GetProfile(userId, cancellationToken).ConfigureAwait(false)
            ?? new CareerProfile { UserId = userId, CreatedAt = DateTime.UtcNow };
        profile.OnboardingCompleted = true;
        await SaveProfile(userId, profile, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetCvFingerprintAsync(
        string userId,
        string contentHash,
        int contentLength,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (!CvContentHasher.IsSha256Hex(contentHash))
            throw new ArgumentException("CV content hash must be a 64-character SHA-256 hex string.", nameof(contentHash));
        if (contentLength < 0)
            throw new ArgumentOutOfRangeException(nameof(contentLength));

        var normalizedHash = contentHash.Trim().ToLowerInvariant();
        var profile = await GetProfile(userId, cancellationToken).ConfigureAwait(false)
            ?? new CareerProfile { UserId = userId, CreatedAt = DateTime.UtcNow };
        profile.CvUploadedAt = DateTime.UtcNow;
        StripNonPersistedCvFields(profile);
        profile.CvSummary = Truncate(profile.CvSummary, CareerProfileStorageLimits.CvSummaryMaxChars);
        profile.CvSummaryEn = Truncate(profile.CvSummaryEn, CareerProfileStorageLimits.CvSummaryMaxChars);

        var existing = await db.CareerProfiles
            .FirstOrDefaultAsync(x => x.ClerkUserId == userId, cancellationToken)
            .ConfigureAwait(false);

        var now = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(profile, JsonOpts);

        if (existing is null)
        {
            db.CareerProfiles.Add(new CareerProfileEntity
            {
                ClerkUserId = userId,
                CreatedAt = profile.CreatedAt == default ? now : profile.CreatedAt,
                UpdatedAt = now,
                ProfileJson = json,
                CvContentHash = normalizedHash,
                CvContentLength = contentLength,
                CacheVersion = 1,
            });
        }
        else
        {
            existing.UpdatedAt = now;
            existing.ProfileJson = json;
            existing.CvContentHash = normalizedHash;
            existing.CvContentLength = contentLength;
            existing.CacheVersion = existing.CacheVersion + 1;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes CV-derived profile data and the stored fingerprint so a new upload cannot mix with the old CV.
    /// Keeps field, level, goals, story, target jobs and onboarding flags.
    /// </summary>
    public async Task ClearCvDerivedDataAsync(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var existing = await db.CareerProfiles
            .FirstOrDefaultAsync(x => x.ClerkUserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
            return;

        var profile = DeserializeProfile(existing.ProfileJson, userId);
        profile.CurrentRole = null;
        profile.Skills = [];
        profile.Experience = [];
        profile.EducationEntries = [];
        profile.Languages = [];
        profile.CvSummary = null;
        profile.CvSummaryEn = null;
        profile.CvUploadedAt = null;
        StripNonPersistedCvFields(profile);

        var now = DateTime.UtcNow;
        profile.UpdatedAt = now;
        existing.UpdatedAt = now;
        existing.ProfileJson = JsonSerializer.Serialize(profile, JsonOpts);
        existing.CvContentHash = null;
        existing.CvContentLength = null;
        existing.CacheVersion += 1;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetSkills(string userId, List<string> skills, CancellationToken cancellationToken = default)
    {
        var profile = await GetProfile(userId, cancellationToken).ConfigureAwait(false)
            ?? new CareerProfile { UserId = userId, CreatedAt = DateTime.UtcNow };
        profile.Skills = skills;
        await SaveProfile(userId, profile, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> AddTargetJob(
        string userId,
        string title,
        string? company,
        string? description,
        CancellationToken cancellationToken = default)
    {
        var profile = await GetProfile(userId, cancellationToken).ConfigureAwait(false)
            ?? new CareerProfile { UserId = userId, CreatedAt = DateTime.UtcNow };

        if (profile.TargetJobs.Count >= 3)
            throw new InvalidOperationException("Maximal 3 Wunschstellen erlaubt. Lösche eine bevor du eine neue hinzufügst.");

        var job = new TargetJob
        {
            Title = title,
            Company = company,
            Description = description is { Length: > CareerProfileStorageLimits.TargetJobDescriptionMax }
                ? description[..CareerProfileStorageLimits.TargetJobDescriptionMax]
                : description,
        };
        profile.TargetJobs.Add(job);
        await SaveProfile(userId, profile, cancellationToken).ConfigureAwait(false);
        return job.Id;
    }

    public async Task RemoveTargetJob(string userId, string jobId, CancellationToken cancellationToken = default)
    {
        var profile = await GetProfile(userId, cancellationToken).ConfigureAwait(false)
            ?? new CareerProfile { UserId = userId, CreatedAt = DateTime.UtcNow };
        profile.TargetJobs.RemoveAll(j => j.Id == jobId);
        await SaveProfile(userId, profile, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OnboardingDraft?> GetOnboardingDraftAsync(string userId, CancellationToken cancellationToken = default)
    {
        var profile = await GetProfile(userId, cancellationToken).ConfigureAwait(false);
        return profile?.OnboardingDraft;
    }

    public async Task SaveOnboardingDraftAsync(string userId, OnboardingDraft draft, CancellationToken cancellationToken = default)
    {
        var profile = await GetProfile(userId, cancellationToken).ConfigureAwait(false)
            ?? new CareerProfile { UserId = userId, CreatedAt = DateTime.UtcNow };
        draft.UpdatedAt = DateTime.UtcNow;
        profile.OnboardingDraft = draft;
        await SaveProfile(userId, profile, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetCoachTourCompletedAsync(string userId, CancellationToken cancellationToken = default)
    {
        var profile = await GetProfile(userId, cancellationToken).ConfigureAwait(false)
            ?? new CareerProfile { UserId = userId, CreatedAt = DateTime.UtcNow };
        profile.OnboardingCoachTourCompleted = true;
        await SaveProfile(userId, profile, cancellationToken).ConfigureAwait(false);
    }

    public async Task BumpProfileCacheVersionAsync(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId))
            return;


        var row = await db.CareerProfiles
            .FirstOrDefaultAsync(x => x.ClerkUserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
            return;

        row.CacheVersion++;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetProfileCacheVersionAsync(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId))
            return "0";

        var v = await db.CareerProfiles
            .AsNoTracking()
            .Where(x => x.ClerkUserId == userId)
            .Select(x => x.CacheVersion)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return v.ToString();
    }

    private static CareerProfile DeserializeProfile(
        string profileJson,
        string userId,
        string? contentHash = null,
        int? contentLength = null)
    {
        var profile = JsonSerializer.Deserialize<CareerProfile>(profileJson, JsonOpts) ?? new CareerProfile();
        profile.UserId = userId;
        profile.Goals ??= [];
        profile.Skills ??= [];
        profile.Experience ??= [];
        profile.EducationEntries ??= [];
        profile.Languages ??= [];
        profile.TargetJobs ??= [];
        StripNonPersistedCvFields(profile);
        if (!string.IsNullOrWhiteSpace(contentHash))
        {
            profile.CvContentHash = contentHash.Trim().ToLowerInvariant();
            profile.CvContentLength = contentLength;
        }

        return profile;
    }

    private static void StripNonPersistedCvFields(CareerProfile profile)
    {
        profile.CvRawText = null;
        profile.CvContentHash = null;
        profile.CvContentLength = null;
    }

    private static string? Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s))
            return s;
        return s.Length > max ? s[..max] : s;
    }
}
