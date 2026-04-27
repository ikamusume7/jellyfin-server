using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Api.Controllers;

/// <summary>
/// Returns items sorted by the air date of their most recently broadcast episode.
/// </summary>
[Route("Items")]
[Authorize]
public class LatestAirDateController : BaseJellyfinApiController
{
    private readonly ILibraryManager _libraryManager;
    private readonly IDtoService _dtoService;

    /// <summary>
    /// Initializes a new instance of the <see cref="LatestAirDateController"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="dtoService">Instance of the <see cref="IDtoService"/> interface.</param>
    public LatestAirDateController(ILibraryManager libraryManager, IDtoService dtoService)
    {
        _libraryManager = libraryManager;
        _dtoService = dtoService;
    }

    /// <summary>
    /// Gets series/movies sorted by the PremiereDate of their most recently aired episode.
    /// For movies, the movie's own PremiereDate is used.
    /// </summary>
    /// <param name="libraryId">Optional library ID to scope results.</param>
    /// <param name="tags">Comma-separated tag filter.</param>
    /// <param name="years">Single production year filter.</param>
    /// <param name="startIndex">Pagination start index.</param>
    /// <param name="limit">Maximum number of items to return.</param>
    /// <param name="sortOrder">Ascending or Descending.</param>
    /// <response code="200">Sorted items returned.</response>
    /// <returns>Paged list of items sorted by latest episode air date.</returns>
    [HttpGet("ByLatestAirDate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<QueryResult<BaseItemDto>> GetByLatestAirDate(
        [FromQuery] Guid? libraryId = null,
        [FromQuery] string? tags = null,
        [FromQuery] int? years = null,
        [FromQuery] int startIndex = 0,
        [FromQuery] int limit = 40,
        [FromQuery] string sortOrder = "Descending")
    {
        // Step 1: Load all episodes in the target library to build a
        //         (SeriesId → max PremiereDate) map.
        var episodeQuery = new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            Recursive = true,
            EnableTotalRecordCount = false,
            DtoOptions = new DtoOptions
            {
                EnableImages = false,
                EnableUserData = false
            }
        };

        if (libraryId.HasValue)
        {
            episodeQuery.AncestorIds = new[] { libraryId.Value };
        }

        var episodes = _libraryManager.GetItemList(episodeQuery);

        var latestAirBySeriesId = new Dictionary<Guid, DateTime>();
        foreach (var ep in episodes.OfType<Episode>())
        {
            if (ep.SeriesId.Equals(Guid.Empty) || !ep.PremiereDate.HasValue)
            {
                continue;
            }

            if (!latestAirBySeriesId.TryGetValue(ep.SeriesId, out var existing)
                || ep.PremiereDate.Value > existing)
            {
                latestAirBySeriesId[ep.SeriesId] = ep.PremiereDate.Value;
            }
        }

        // Step 2: Load series and movies that match the requested filters.
        var tagArr = string.IsNullOrEmpty(tags)
            ? Array.Empty<string>()
            : tags.Split(',', StringSplitOptions.RemoveEmptyEntries);

        var dtoOptions = new DtoOptions
        {
            EnableImages = true,
            EnableUserData = false,
            Fields = new[] { ItemFields.Tags, ItemFields.Genres, ItemFields.ChildCount }
        };

        var itemQuery = new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Series, BaseItemKind.Movie },
            Recursive = true,
            EnableTotalRecordCount = false,
            Tags = tagArr,
            Years = years.HasValue ? new[] { years.Value } : Array.Empty<int>(),
            DtoOptions = dtoOptions
        };

        if (libraryId.HasValue)
        {
            itemQuery.AncestorIds = new[] { libraryId.Value };
        }

        var allItems = _libraryManager.GetItemList(itemQuery);

        // Step 3: Determine the effective "latest air date" for each item.
        //   Series → max PremiereDate of their episodes (from latestAirBySeriesId).
        //   Movies → the movie's own PremiereDate.
        //   Fallback → DateTime.MinValue so items without dates sort to the end.
        DateTime GetEffectiveDate(BaseItem item) =>
            latestAirBySeriesId.TryGetValue(item.Id, out var d)
                ? d
                : (item.PremiereDate ?? DateTime.MinValue);

        // Step 4: Sort.
        var sorted = sortOrder.Equals("Ascending", StringComparison.OrdinalIgnoreCase)
            ? allItems.OrderBy(GetEffectiveDate).ToList()
            : allItems.OrderByDescending(GetEffectiveDate).ToList();

        var totalCount = sorted.Count;

        // Step 5: Paginate.
        var page = sorted.Skip(startIndex).Take(limit).ToList();

        // Step 6: Serialize to DTOs.
        var dtos = page
            .Select(item => _dtoService.GetBaseItemDto(item, dtoOptions))
            .ToArray();

        return new QueryResult<BaseItemDto>(startIndex, totalCount, dtos);
    }
}
