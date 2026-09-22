using KnxMonitor.Core.DTOs;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnxMonitor.Api.Controllers;

/// <summary>Saved group-address selections of the charts view.</summary>
[Authorize]
[ApiController]
[Route("api/chart-selections")]
public class ChartSelectionsController : ControllerBase
{
    /// <summary>Same cap as the chart itself: more series than this are not drawn anyway.</summary>
    public const int MaxAddresses = 8;
    public const int MaxNameLength = 100;
    private const int MaxAddressLength = 20;

    private readonly IChartSelectionRepository _repository;

    public ChartSelectionsController(IChartSelectionRepository repository)
    {
        _repository = repository;
    }

    /// <summary>All saved selections, ordered by name.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ChartSelectionDto>>> GetAll(CancellationToken ct)
    {
        var rows = await _repository.GetAllOrderedAsync(ct);
        return Ok(rows.Select(ToDto));
    }

    /// <summary>Saves a selection under a name; an existing one with that name is overwritten.</summary>
    /// <remarks>
    /// The name is trimmed and matched case-insensitively, so saving "pumpe" updates "Pumpe" (and
    /// adopts the new spelling). Addresses are trimmed and de-duplicated, keeping their order.
    /// Validation: the name must be 1–100 characters, and there must be 1–8 addresses of at most
    /// 20 characters each — otherwise 400 and nothing is stored.
    /// </remarks>
    /// <returns>The selection as stored.</returns>
    [HttpPut]
    public async Task<ActionResult<ChartSelectionDto>> Save([FromBody] SaveChartSelectionDto dto, CancellationToken ct)
    {
        var name = (dto.Name ?? string.Empty).Trim();
        if (name.Length == 0 || name.Length > MaxNameLength)
        {
            return BadRequest(new { Message = $"Name must be 1 to {MaxNameLength} characters" });
        }

        var addresses = (dto.Addresses ?? new List<string>())
            .Select(a => (a ?? string.Empty).Trim())
            .Where(a => a.Length > 0)
            .Distinct()
            .ToList();
        if (addresses.Count == 0 || addresses.Count > MaxAddresses)
        {
            return BadRequest(new { Message = $"Between 1 and {MaxAddresses} addresses are required" });
        }
        // A comma would split one address into two on the way back out.
        if (addresses.Any(a => a.Length > MaxAddressLength || a.Contains(',')))
        {
            return BadRequest(new { Message = "Invalid group address" });
        }

        var saved = await _repository.UpsertByNameAsync(name, addresses, ct);
        return Ok(ToDto(saved));
    }

    /// <summary>Deletes a saved selection.</summary>
    /// <response code="204">Deleted.</response>
    /// <response code="404">No selection with that id.</response>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var row = await _repository.GetByIdAsync(id);
        if (row is null)
        {
            return NotFound();
        }
        await _repository.DeleteAsync(row);
        return NoContent();
    }

    private static ChartSelectionDto ToDto(ChartSelection s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Addresses = s.Addresses.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
        UpdatedAt = s.UpdatedAt
    };
}
