using System.ComponentModel.DataAnnotations;

namespace PrivatePrep.Models;

public class CheckoutRequest
{
    [Required]
    [StringLength(20, MinimumLength = 3)]
    public string Plan { get; set; } = "";

    [StringLength(320)]
    [EmailAddress]
    public string? Email { get; set; }

    [StringLength(128)]
    public string? UserId { get; set; }
}

public class SyncPlanRequest
{
    [StringLength(320)]
    [EmailAddress]
    public string? Email { get; set; }
}
