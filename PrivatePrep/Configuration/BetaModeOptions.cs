namespace PrivatePrep.Configuration;

public sealed class BetaModeOptions
{
    public const string SectionName = "BetaMode";

    public bool Enabled { get; set; }

    /// <summary>
    /// Comma-separated emails allowed while <see cref="Enabled"/> is true.
    /// Compared case-insensitively against the Clerk JWT <c>email</c> claim.
    /// </summary>
    public string AllowedEmails { get; set; } = "";
}
