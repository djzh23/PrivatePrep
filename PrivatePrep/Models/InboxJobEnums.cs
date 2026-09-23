namespace PrivatePrep.Models;

/// <summary>Where a collected job posting came from.</summary>
public enum InboxSourceKind
{
    Generic = 0,
    LinkedIn = 1,
    StepStone = 2,
    Manual = 3,
}

/// <summary>Lifecycle of one inbox entry.</summary>
public enum InboxJobStatus
{
    New = 0,
    Analyzed = 1,
    Archived = 2,
}
