namespace Jellyfin.Api.Models;

/// <summary>
/// Request body for batch tag removal.
/// </summary>
public class BatchRemoveRequest
{
    /// <summary>
    /// Gets or sets the tags to remove.
    /// </summary>
    public string[]? Tags { get; set; }

    /// <summary>
    /// Gets or sets the minimum count threshold. Tags with usage below this value will be removed.
    /// </summary>
    public int MinCount { get; set; }
}
