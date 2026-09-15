namespace PrivatePrep.Data;

/// <summary>PostgreSQL feature flags. V1 is Postgres-only; leftover storage keys are ignored.</summary>
public sealed class DatabaseFeatureOptions
{
    public const string SectionName = "DatabaseFeatures";

    /// <summary>When true, the Postgres health check is registered.</summary>
    public bool PostgresEnabled { get; set; } = true;
}
