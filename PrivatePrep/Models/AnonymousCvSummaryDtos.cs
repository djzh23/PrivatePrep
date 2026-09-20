namespace PrivatePrep.Models;

public sealed class AnonymousCvSummaryRequest
{
    /// <summary><c>de</c> or <c>en</c>; default German.</summary>
    public string? Language { get; set; }

    /// <summary>Optional CV text from the browser. Used in-memory only; not stored.</summary>
    public string? CvText { get; set; }
}

public sealed record AnonymousCvSummaryResponse(string Summary);
