using System.ComponentModel.DataAnnotations;

namespace PrivatePrep.Models;

public class OnboardingRequest
{
    [Required]
    [StringLength(80)]
    public string Field { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string FieldLabel { get; set; } = string.Empty;

    [Required]
    [StringLength(80)]
    public string Level { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string LevelLabel { get; set; } = string.Empty;

    [StringLength(200)]
    public string? CurrentRole { get; set; }

    [MaxLength(30)]
    public List<string>? Goals { get; set; }
}

public class UpdateSkillsRequest
{
    [MaxLength(30)]
    public List<string>? Skills { get; set; }
}

public class UploadCvRequest
{
    [Required]
    [StringLength(500_000)]
    public string Text { get; set; } = string.Empty;
}

public class UploadCvPdfRequest
{
    [Required]
    [StringLength(5_000_000)]
    public string Base64Pdf { get; set; } = string.Empty;
}

public class AddTargetJobRequest
{
    [Required]
    [StringLength(300)]
    public string Title { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Company { get; set; }

    [StringLength(12_000)]
    public string? Description { get; set; }
}

public class SaveOnboardingDraftRequest
{
    [StringLength(80)]
    public string? Field { get; set; }

    [StringLength(80)]
    public string? Level { get; set; }

    [StringLength(200)]
    public string? CurrentRole { get; set; }

    [MaxLength(20)]
    public List<string>? Goals { get; set; }

    public int? LastStep { get; set; }
}
