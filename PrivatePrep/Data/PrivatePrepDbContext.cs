using Microsoft.EntityFrameworkCore;
using PrivatePrep.Data.Entities;

namespace PrivatePrep.Data;

public sealed class PrivatePrepDbContext(DbContextOptions<PrivatePrepDbContext> options) : DbContext(options)
{
    public DbSet<AppUserEntity> AppUsers => Set<AppUserEntity>();

    public DbSet<CareerProfileEntity> CareerProfiles => Set<CareerProfileEntity>();

    public DbSet<TokenUsageGlobalDailyEntity> TokenUsageGlobalDaily => Set<TokenUsageGlobalDailyEntity>();

    public DbSet<TokenUsageDailyUserEntity> TokenUsageDailyUsers => Set<TokenUsageDailyUserEntity>();

    public DbSet<TokenUsageDailyUserModelEntity> TokenUsageDailyUserModels => Set<TokenUsageDailyUserModelEntity>();

    public DbSet<TokenUsageDailyUserToolEntity> TokenUsageDailyUserTools => Set<TokenUsageDailyUserToolEntity>();

    public DbSet<TokenUsageRegisteredUserEntity> TokenUsageRegisteredUsers => Set<TokenUsageRegisteredUserEntity>();

    public DbSet<UserUsageDailyEntity> UserUsageDaily => Set<UserUsageDailyEntity>();

    public DbSet<UserPlanEntity> UserPlans => Set<UserPlanEntity>();

    public DbSet<InboxJobEntity> InboxJobs => Set<InboxJobEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUserEntity>(e =>
        {
            e.ToTable("app_users");
            e.HasKey(x => x.ClerkUserId);
        });

        modelBuilder.Entity<CareerProfileEntity>(e =>
        {
            e.ToTable("career_profiles");
            e.HasKey(x => x.ClerkUserId);
            e.HasIndex(x => x.UpdatedAt);
        });

        modelBuilder.Entity<TokenUsageGlobalDailyEntity>(e =>
        {
            e.ToTable("token_usage_global_daily");
            e.HasKey(x => x.UsageDate);
        });

        modelBuilder.Entity<TokenUsageDailyUserEntity>(e =>
        {
            e.ToTable("token_usage_daily_user");
            e.HasKey(x => new { x.ClerkUserId, x.UsageDate });
            e.Property(x => x.CostUsd).HasPrecision(18, 6);
        });

        modelBuilder.Entity<TokenUsageDailyUserModelEntity>(e =>
        {
            e.ToTable("token_usage_daily_user_model");
            e.HasKey(x => new { x.ClerkUserId, x.UsageDate, x.ModelKey });
            e.Property(x => x.CostUsd).HasPrecision(18, 6);
        });

        modelBuilder.Entity<TokenUsageDailyUserToolEntity>(e =>
        {
            e.ToTable("token_usage_daily_user_tool");
            e.HasKey(x => new { x.ClerkUserId, x.UsageDate, x.Tool });
            e.Property(x => x.CostUsd).HasPrecision(18, 6);
        });

        modelBuilder.Entity<TokenUsageRegisteredUserEntity>(e =>
        {
            e.ToTable("token_usage_registered_users");
            e.HasKey(x => x.ClerkUserId);
        });

        modelBuilder.Entity<UserUsageDailyEntity>(e =>
        {
            e.ToTable("user_usage_daily");
            e.HasKey(x => new { x.ClerkUserId, x.UsageDate });
        });

        modelBuilder.Entity<UserPlanEntity>(e =>
        {
            e.ToTable("user_plan");
            e.HasKey(x => x.ClerkUserId);
        });

        modelBuilder.Entity<InboxJobEntity>(e =>
        {
            e.ToTable("inbox_jobs");
            e.HasKey(x => x.Id);
            e.Property(x => x.SourceKind).HasConversion<int>();
            e.Property(x => x.Status).HasConversion<int>();
            e.HasIndex(x => new { x.UserId, x.SourceUrl }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.Status });
            e.HasIndex(x => new { x.UserId, x.ExtractedAt }).IsDescending(false, true);
        });

    }
}
