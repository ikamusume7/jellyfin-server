using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Models;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Api.Controllers;

/// <summary>
/// Tag statistics controller.
/// </summary>
[Route("Tags")]
[Authorize(Policy = Policies.RequiresElevation)]
public class TagStatsController : BaseJellyfinApiController
{
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="TagStatsController"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    public TagStatsController(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Gets tag usage statistics across all items, optionally filtered to a library.
    /// </summary>
    /// <param name="libraryId">Optional library (parent folder) item ID to scope the statistics.</param>
    /// <response code="200">Tag stats returned.</response>
    /// <returns>A list of tags with their usage count.</returns>
    [HttpGet("Stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<TagStatDto>> GetTagStats([FromQuery] Guid? libraryId = null)
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[]
            {
                BaseItemKind.Series,
                BaseItemKind.Season,
                BaseItemKind.Movie
            },
            Recursive = true,
            EnableTotalRecordCount = false,
            DtoOptions = new DtoOptions
            {
                Fields = new[] { ItemFields.Tags },
                EnableImages = false,
                EnableUserData = false
            }
        };

        if (libraryId.HasValue)
        {
            query.AncestorIds = new[] { libraryId.Value };
        }

        var items = _libraryManager.GetItemList(query);

        var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            foreach (var tag in item.Tags)
            {
                tagCounts.TryGetValue(tag, out var count);
                tagCounts[tag] = count + 1;
            }
        }

        var result = tagCounts
            .Select(kvp => new TagStatDto { Name = kvp.Key, Count = kvp.Value })
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.Name)
            .ToList();

        return result;
    }

    /// <summary>
    /// Removes specified tags from all items, or removes all tags with usage count below a threshold.
    /// </summary>
    /// <param name="request">The batch remove request.</param>
    /// <response code="200">Tags removed successfully.</response>
    /// <returns>A summary of the operation.</returns>
    [HttpPost("BatchRemove")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<BatchRemoveResult>> BatchRemoveTags([FromBody, Required] BatchRemoveRequest request)
    {
        var tagsToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (request.Tags is not null)
        {
            foreach (var t in request.Tags)
            {
                tagsToRemove.Add(t);
            }
        }

        // If MinCount is specified, also collect all tags with usage below that threshold
        if (request.MinCount > 0)
        {
            var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[]
                {
                    BaseItemKind.Series,
                    BaseItemKind.Season,
                    BaseItemKind.Movie
                },
                Recursive = true,
                EnableTotalRecordCount = false,
                DtoOptions = new DtoOptions
                {
                    Fields = new[] { ItemFields.Tags },
                    EnableImages = false,
                    EnableUserData = false
                }
            });

            var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                foreach (var tag in item.Tags)
                {
                    tagCounts.TryGetValue(tag, out var count);
                    tagCounts[tag] = count + 1;
                }
            }

            foreach (var kvp in tagCounts)
            {
                if (kvp.Value < request.MinCount)
                {
                    tagsToRemove.Add(kvp.Key);
                }
            }
        }

        if (tagsToRemove.Count == 0)
        {
            return new BatchRemoveResult { RemovedTags = 0, AffectedItems = 0 };
        }

        // Find all items that have any of the tags to remove
        // Use full DtoOptions so the entire item is loaded before saving back
        var allItems = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[]
            {
                BaseItemKind.Series,
                BaseItemKind.Season,
                BaseItemKind.Movie
            },
            Recursive = true,
            EnableTotalRecordCount = false,
            DtoOptions = new DtoOptions(true)
        });

        var affectedCount = 0;
        foreach (var item in allItems)
        {
            var originalTags = item.Tags;
            var newTags = originalTags.Where(t => !tagsToRemove.Contains(t)).ToArray();
            if (newTags.Length < originalTags.Length)
            {
                item.Tags = newTags;
                await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
                affectedCount++;
            }
        }

        return new BatchRemoveResult
        {
            RemovedTags = tagsToRemove.Count,
            AffectedItems = affectedCount
        };
    }
}
