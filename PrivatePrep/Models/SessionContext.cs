namespace PrivatePrep.Models;

public class JobContext
{
    public bool IsAnalyzed { get; set; }
    public string JobTitle { get; set; } = "Unknown Role";
    public string CompanyName { get; set; } = "Unknown Company";
    public string Location { get; set; } = "Not specified";
    public List<string> KeyRequirements { get; set; } = new();
    public List<string> Keywords { get; set; } = new();
    public string RawJobText { get; set; } = "";
}
