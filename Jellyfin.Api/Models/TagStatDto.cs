namespace Jellyfin.Api.Models;

/// <summary>
/// Tag statistics data transfer object.
/// </summary>
public class TagStatDto
{
    /// <summary>
    /// Gets or sets the tag name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the number of items using this tag.
    /// </summary>
    public int Count { get; set; }
}
