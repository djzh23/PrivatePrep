namespace PrivatePrep.Services.Privacy;

/// <summary>
/// Best-effort filter for obvious contact data. Not anonymization and not a name detector.
/// </summary>
public interface IPiiScrubberService
{
    string ScrubBestEffort(string input);
}
