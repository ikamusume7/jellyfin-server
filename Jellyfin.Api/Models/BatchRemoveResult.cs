namespace Jellyfin.Api.Models;

/// <summary>
/// Result of a batch tag removal.
/// </summary>
public class BatchRemoveResult
{
    /// <summary>
    /// Gets or sets the number of distinct tags removed.
    /// </summary>
    public int RemovedTags { get; set; }

    /// <summary>
    /// Gets or sets the number of items affected.
    /// </summary>
    public int AffectedItems { get; set; }
}
