using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PrivatePrep.Data.Entities;

[Table("career_profiles")]
public sealed class CareerProfileEntity
{
    [Key]
    [Column("clerk_user_id")]
    public string ClerkUserId { get; set; } = "";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [Column("profile_json", TypeName = "jsonb")]
    public string ProfileJson { get; set; } = "{}";

    [Column("cv_content_hash")]
    [MaxLength(64)]
    public string? CvContentHash { get; set; }

    [Column("cv_content_length")]
    public int? CvContentLength { get; set; }

    [Column("cache_version")]
    public long CacheVersion { get; set; }
}
